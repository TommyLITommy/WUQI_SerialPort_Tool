using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MySerialPortAssistant04.Models;
using MySerialPortAssistant04.Protocol.Bluetooth;
using MySerialPortAssistant04.Services.Configuration;
using MySerialPortAssistant04.Services.Logging;
using MySerialPortAssistant04.Services.Web;
using RJCP.IO.Ports;

namespace MySerialPortAssistant04;

/// <summary>
/// 单路串口监控用户控件：串口收发、dbglog 匹配、子窗口与 HCI 转发。
/// </summary>
public partial class SerialPortMonitorControl : UserControl
{
    #region 常量与配置

    private const int MaxLogLines = 2000;
    private const int LogBatchSize = 50;
    private const int LogFlushIntervalMs = 80;
    private const string LogSaveDirectory = "SerialLogs";

    private static int _baseWebPort = 8080;

    #endregion

    #region 事件与公开属性

    public event EventHandler? CloseRequested;
    public event Action<string>? LogPushed;

    public int ControlIndex
    {
        get => _controlIndex;
        set
        {
            _controlIndex = value;
            UpdateTitle();
        }
    }

    /// <summary>当前选中的 COM 口（供主窗口总开关使用）。</summary>
    public string? SelectedSerialPort => CboSerialPort.SelectedItem?.ToString();

    /// <summary>是否正在监听。</summary>
    public bool IsMonitoring => _isMonitoring;

    #endregion

    #region 字段

    private int _controlIndex;
    private bool _isClosing;
    private bool _isMonitoring;

    private readonly List<Window> _subWindows = new();
    private readonly PortSettingsService _portSettings;
    private readonly SerialLogFileWriter _serialLogWriter = new();
    private readonly WiresharkPipeServer _wiresharkPipe = new();
    private readonly EllisysUdpSender _ellisysUdp = new();
    private readonly WebLogServer _webLogServer;
    private readonly int _instanceWebPort;

    private Dictionary<uint, LogEntry> _addressMap = new();
    private List<List<LogEntry>> _coreGroups = new();

    private SerialPortStream? _serialPort;
    private readonly List<byte> _buffer = new();
    private readonly object _lockObj = new();
    private CancellationTokenSource? _cts;
    private Task? _readTask;

    private readonly Queue<string> _logQueue = new();
    private readonly object _logLock = new();
    private Timer? _logFlushTimer;
    private ScrollViewer? _logScrollViewer;
    private bool _isUserScrollingAway;
    private bool _logUiFlushScheduled;
    private bool _suppressLogScrollTracking;
    private const double LogScrollBottomThreshold = 4.0;

    private int _totalPacketsReceived;
    private int _totalPacketsParsed;
    private int _totalPacketsDropped;
    private int _lastSequence = -1;
    private int _sequenceGapCount;
    private int _lastSequenceType6 = -1;
    private int _sequenceGapCountType6;
    private int _lastStatsUpdate;

    #endregion

    public SerialPortMonitorControl()
    {
        InitializeComponent();

        _instanceWebPort = _baseWebPort++;
        _webLogServer = new WebLogServer(_instanceWebPort);
        _portSettings = new PortSettingsService($"LastSerialPort_{GetHashCode()}");

        RefreshSerialPorts();
        _logFlushTimer = new Timer(LogFlushCallback, null, LogFlushIntervalMs, LogFlushIntervalMs);

        Loaded += OnControlLoaded;
    }

    private void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _webLogServer.Start();
            LoadSavedPort();

            _logScrollViewer = TxtLogOutput.Template?.FindName("PART_ContentHost", TxtLogOutput) as ScrollViewer;
            if (_logScrollViewer != null)
                _logScrollViewer.ScrollChanged += OnLogScrollChanged;

            AppendLog($"=== Serial Monitor #{ControlIndex} Started ===");
            AppendLog($"🌐 Web Log: http://localhost:{_instanceWebPort}");
            _wiresharkPipe.Start();
            AppendLog($"🔗 Wireshark 管道: \\\\.\\pipe\\{WiresharkPipeServer.PipeName}");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Init failed: {ex.Message}");
        }
    }

    #region 主窗口协调 API

    public void StartMonitorInternal()
    {
        if (CboSerialPort.SelectedItem == null || _addressMap.Count == 0)
            return;

        string? port = CboSerialPort.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(port)) return;

        _portSettings.SaveLastPort(port);
        InitializeLogFile();
        StartMonitor(port);

        if (_isMonitoring && Window.GetWindow(this) is MainWindow mainWindow)
            mainWindow.NotifyChildMonitorStarted();
    }

    public void StopMonitorInternal()
    {
        StopMonitor();
        CloseLogFile();

        if (Window.GetWindow(this) is MainWindow mainWindow)
            mainWindow.NotifyChildMonitorStopped();
    }

    public void SetLogFilePath(string path, Action? onLoadedCallback = null)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                TxtLogPath.Text = path;
                TxtLogStatus.Text = "Loaded";
                TxtLogStatus.Foreground = Brushes.Green;
                LoadLogFile(path);
                onLoadedCallback?.Invoke();
            }
            else if (!string.IsNullOrEmpty(path))
            {
                TxtLogPath.Text = path;
                TxtLogStatus.Text = "File not exist";
                TxtLogStatus.Foreground = Brushes.Red;
            }
        });
    }

    public void ResetCloseButtonState()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _isClosing = false;
            BtnClose.IsEnabled = true;
        });
    }

    public void Cleanup()
    {
        _isClosing = true;
        _wiresharkPipe.Stop();
        _ellisysUdp.Close();
        _webLogServer.Stop();

        try { _logFlushTimer?.Dispose(); } catch { }

        Task.Run(() =>
        {
            StopMonitor();
            CloseLogFile();
        }).Wait(1000);

        lock (_subWindows)
        {
            var windowsToClose = _subWindows.ToList();
            _subWindows.Clear();
            foreach (var window in windowsToClose)
            {
                try
                {
                    if (window.Dispatcher.CheckAccess())
                        window.Close();
                    else
                        window.Dispatcher.Invoke(window.Close);
                }
                catch { }
            }
        }

        CloseRequested = null;
        LogPushed = null;
    }

    #endregion

    #region UI 事件

    private void UpdateTitle()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (TxtTitle != null)
                TxtTitle.Text = $"Serial Monitor #{ControlIndex}";
        });
    }

    private async void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (_isClosing) return;
        _isClosing = true;
        BtnClose.IsEnabled = false;

        if (_isMonitoring)
        {
            var result = MessageBox.Show(
                "Serial port is monitoring. Close this window?",
                "Confirm Close",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                ResetCloseButtonState();
                return;
            }

            await Task.Run(() =>
            {
                StopMonitor();
                CloseLogFile();
            });
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BtnOpenSelectedSubWindow_Click(object sender, RoutedEventArgs e)
    {
        if (CboSubWindowType.SelectedItem is not ComboBoxItem item) return;
        string windowName = item.Content?.ToString() ?? "";

        Window? newWindow = windowName switch
        {
            "过滤日志窗口" => new FilterLogSubWindow(ControlIndex),
            "波形图窗口" => new WaveformSubWindow(ControlIndex),
            "磁力计方位窗口" => new MagnetometerSubWindow(ControlIndex),
            "姿态窗口" => new Attitude3DWindow(ControlIndex),
            _ => null
        };

        if (newWindow is not ILogReceiver receiver)
        {
            AppendLog("❌ Invalid window type");
            return;
        }

        LogPushed += receiver.EnqueueLog;
        newWindow.Closed += (_, _) =>
        {
            LogPushed -= receiver.EnqueueLog;
            lock (_subWindows) _subWindows.Remove(newWindow);
            AppendLog($"✅ Closed: {windowName}, remaining: {_subWindows.Count}");
        };

        lock (_subWindows) _subWindows.Add(newWindow);
        newWindow.Show();
        AppendLog($"✅ Opened: {windowName} (Owner: #{ControlIndex}), total: {_subWindows.Count}");
    }

    private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Log file 统一由主窗口管理，请使用主窗口工具栏 \"Select log file\" 按钮。",
            "提示",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnStartMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (CboSerialPort.SelectedItem == null)
        {
            MessageBox.Show("Please select serial port!", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_addressMap.Count == 0)
        {
            MessageBox.Show(
                "Please first Load log file！请使用主窗口 \"Select log file\" 按钮设置。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string? port = CboSerialPort.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(port))
            StartMonitorInternal();
    }

    private void BtnStopMonitor_Click(object sender, RoutedEventArgs e)
    {
        StopMonitorInternal();
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        lock (_logLock) _logQueue.Clear();
        Dispatcher.Invoke(() =>
        {
            TxtLogOutput.Clear();
            _isUserScrollingAway = false;
        });
    }

    private void BtnRefreshPort_Click(object sender, RoutedEventArgs e) => RefreshSerialPorts();

    #endregion

    #region 串口列表与配置记忆

    private void RefreshSerialPorts()
    {
        CboSerialPort.Items.Clear();
        try
        {
            using var temp = new SerialPortStream();
            foreach (var port in temp.GetPortNames())
                CboSerialPort.Items.Add(port);

            if (CboSerialPort.Items.Count > 0)
                CboSerialPort.SelectedIndex = 0;
        }
        catch { }
    }

    private void LoadSavedPort()
    {
        string? lastPort = _portSettings.LoadLastPort();
        if (!string.IsNullOrEmpty(lastPort) && CboSerialPort.Items.Contains(lastPort))
            CboSerialPort.SelectedItem = lastPort;
    }

    #endregion

    #region 日志输出

    private void AppendLog(string message)
    {
        lock (_logLock)
        {
            _logQueue.Enqueue(message);
            if (_logQueue.Count > MaxLogLines * 2)
            {
                int remove = _logQueue.Count - MaxLogLines;
                for (int i = 0; i < remove; i++)
                    _logQueue.Dequeue();
            }
        }

        if (LogPushed != null)
        {
            foreach (Action<string> handler in LogPushed.GetInvocationList())
            {
                Task.Run(() =>
                {
                    try { handler(message); } catch { }
                });
            }
        }

        _webLogServer.Enqueue(message);
        _serialLogWriter.WriteLine(message);
    }

    private void LogFlushCallback(object? state)
    {
        lock (_logLock)
        {
            if (_logQueue.Count == 0) return;
        }

        if (_logUiFlushScheduled) return;
        _logUiFlushScheduled = true;

        Dispatcher.BeginInvoke(FlushLogQueueToUi, DispatcherPriority.Background);
    }

    private void OnLogScrollChanged(object sender, ScrollChangedEventArgs args)
    {
        if (_suppressLogScrollTracking || args.ExtentHeightChange != 0)
            return;

        _isUserScrollingAway = !IsLogScrolledToBottom();
    }

    private bool IsLogScrolledToBottom()
    {
        if (_logScrollViewer == null)
            return !_isUserScrollingAway;

        if (_logScrollViewer.ScrollableHeight <= 0)
            return true;

        return _logScrollViewer.VerticalOffset >=
               _logScrollViewer.ScrollableHeight - LogScrollBottomThreshold;
    }

    private void FlushLogQueueToUi()
    {
        _logUiFlushScheduled = false;

        const int maxLinesPerFrame = 400;
        int drained = 0;
        var chunks = new List<string>();

        while (drained < maxLinesPerFrame)
        {
            List<string>? batch = null;
            lock (_logLock)
            {
                if (_logQueue.Count == 0) break;
                int take = Math.Min(LogBatchSize, _logQueue.Count);
                batch = new List<string>(take);
                for (int i = 0; i < take; i++)
                    batch.Add(_logQueue.Dequeue());
            }

            chunks.Add(string.Join(Environment.NewLine, batch) + Environment.NewLine);
            drained += batch.Count;
        }

        if (chunks.Count == 0) return;

        bool stickToBottom = !_isUserScrollingAway && IsLogScrolledToBottom();
        TxtLogOutput.AppendText(string.Concat(chunks));
        TrimLogIfNeeded();

        if (stickToBottom)
            ScheduleScrollLogToEnd();

        lock (_logLock)
        {
            if (_logQueue.Count > 0 && !_logUiFlushScheduled)
            {
                _logUiFlushScheduled = true;
                Dispatcher.BeginInvoke(FlushLogQueueToUi, DispatcherPriority.Background);
            }
        }
    }

    private void TrimLogIfNeeded()
    {
        if (TxtLogOutput.LineCount <= MaxLogLines + 500) return;

        string txt = TxtLogOutput.Text;
        int removeLines = TxtLogOutput.LineCount - MaxLogLines;
        if (removeLines <= 100) return;

        int idx = -1;
        int removed = 0;
        for (int i = 0; i < txt.Length && removed < removeLines; i++)
        {
            if (txt[i] == '\n') removed++;
            if (removed == removeLines) { idx = i + 1; break; }
        }

        if (idx > 0)
            TxtLogOutput.Text = txt[idx..];
    }

    private void ScheduleScrollLogToEnd()
    {
        ScrollLogToEnd();
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isUserScrollingAway)
                ScrollLogToEnd();
        }, DispatcherPriority.Loaded);
    }

    private void ScrollLogToEnd()
    {
        _suppressLogScrollTracking = true;
        try
        {
            int end = TxtLogOutput.Text.Length;
            TxtLogOutput.CaretIndex = end;
            TxtLogOutput.SelectionStart = end;
            TxtLogOutput.SelectionLength = 0;
            TxtLogOutput.ScrollToEnd();

            if (_logScrollViewer != null)
                _logScrollViewer.ScrollToVerticalOffset(_logScrollViewer.ExtentHeight);
        }
        finally
        {
            _suppressLogScrollTracking = false;
        }
    }

    private void UpdateStats()
    {
        if (Interlocked.Increment(ref _lastStatsUpdate) % 10 != 0) return;

        Dispatcher.BeginInvoke(() =>
        {
            TxtReceived.Text = _totalPacketsReceived.ToString();
            TxtParsed.Text = _totalPacketsParsed.ToString();
            TxtDropped.Text = _totalPacketsDropped.ToString();
            TxtGap.Text = $"{_sequenceGapCount}/{_sequenceGapCountType6}";
            lock (_lockObj) TxtBuffer.Text = _buffer.Count.ToString();

            if (CboHciSendMode.SelectedItem is ComboBoxItem item)
            {
                if (item.Content?.ToString() == "命名管道(Wireshark)")
                {
                    TxtPipeStatus.Text = _wiresharkPipe.LastError;
                    TxtPipeStatus.Foreground = _wiresharkPipe.IsConnected ? Brushes.Green : Brushes.OrangeRed;
                }
                else
                {
                    TxtPipeStatus.Text = _ellisysUdp.IsConnected ? "UDP 已连接" : "UDP 未连接";
                    TxtPipeStatus.Foreground = _ellisysUdp.IsConnected ? Brushes.Green : Brushes.OrangeRed;
                }
            }
        }, DispatcherPriority.Background);
    }

    #endregion
}
