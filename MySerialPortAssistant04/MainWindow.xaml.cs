using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MySerialPortAssistant04.Services.Configuration;
using MySerialPortAssistant04.Services.UI;

namespace MySerialPortAssistant04;

/// <summary>
/// 主窗口：管理多路串口监控、共享 dbglog 路径与一键启停。
/// </summary>
public partial class MainWindow : Window
{
    private const int MinPorts = 1;
    private const int MaxPorts = 2;

    private readonly AppConfigService _appConfig = new();
    private List<SerialPortMonitorControl> _portControls = null!;
    private readonly TaskbarConnectionBadge _taskbarBadge;
    private string _sharedLogFilePath = "";
    private bool _isMasterMonitoring;

        public string SharedLogFilePath
        {
            get => _sharedLogFilePath;
            private set
            {
                _sharedLogFilePath = value;
                foreach (var control in _portControls)
                {
                    control.SetLogFilePath(value);
                }
                _appConfig.SaveLogFilePath(value);
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _taskbarBadge = new TaskbarConnectionBadge(this);
            InitializePorts();
            LoadConfig();
            UpdateTaskbarConnectedCount();
        }

        /// <summary>启动时从 config.txt 恢复共享 dbglog 路径。</summary>
        private void LoadConfig()
        {
            try
            {
                var savedPath = _appConfig.LoadLogFilePath();
                if (string.IsNullOrEmpty(savedPath))
                    return;

                if (File.Exists(savedPath))
                {
                    _sharedLogFilePath = savedPath;
                    foreach (var control in _portControls)
                        control.SetLogFilePath(savedPath);

                    UpdateButtonStates();
                    txtStatus.Text = $"已自动加载日志: {Path.GetFileName(savedPath)}";
                }
                else
                {
                    txtStatus.Text = $"上次日志文件不存在: {Path.GetFileName(savedPath)}";
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"配置加载失败: {ex.Message}";
            }
        }

        private void InitializePorts()
        {
            _portControls = new List<SerialPortMonitorControl>();
            AddPortControl();
            UpdateButtonStates();
        }

        private void AddPortControl()
        {
            if (_portControls.Count >= MaxPorts)
            {
                MessageBox.Show($"最多只能添加 {MaxPorts} 个串口监控", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var control = new SerialPortMonitorControl
            {
                Margin = new Thickness(2)
            };

            control.SetLogFilePath(SharedLogFilePath);
            control.CloseRequested += OnPortControlCloseRequested;
            _portControls.Add(control);

            RebuildLayout();
            RefreshControlIndices();
            UpdateButtonStates();
        }

        /// <summary>
        /// 重建布局 - 关键修复：移除 MaxWidth 限制，让 GridSplitter 可以自由调整
        /// </summary>
        private void RebuildLayout()
        {
            portsContainer.Children.Clear();
            portsContainer.ColumnDefinitions.Clear();

            int count = _portControls.Count;
            if (count == 0) return;

            // 关键修复1：不再设置 Grid 的 MaxWidth，让 Grid 自然填充 ScrollViewer
            // 由于 ScrollViewer.HorizontalScrollBarVisibility="Disabled"，Grid 宽度会被限制

            // 关键修复2：计算初始列宽比例（平均分配）
            double starWeight = 1.0;

            for (int i = 0; i < count; i++)
            {
                // 控件列：使用 Star，只设置 MinWidth，不设置 MaxWidth
                var colDef = new ColumnDefinition
                {
                    Width = new GridLength(starWeight, GridUnitType.Star),
                    MinWidth = 400
                    // 关键：不设置 MaxWidth，让 GridSplitter 可以自由调整
                };
                portsContainer.ColumnDefinitions.Add(colDef);

                // 添加控件
                var control = _portControls[i];
                // 关键：不设置 control.MaxWidth，让控件可以随列宽变化
                Grid.SetColumn(control, i * 2);
                portsContainer.Children.Add(control);

                // 添加 splitter（如果不是最后一个）
                if (i < count - 1)
                {
                    // splitter 列：固定宽度 6 像素
                    var splitterCol = new ColumnDefinition
                    {
                        Width = new GridLength(6, GridUnitType.Pixel),
                        MinWidth = 6,
                        MaxWidth = 6
                    };
                    portsContainer.ColumnDefinitions.Add(splitterCol);

                    var splitter = new GridSplitter
                    {
                        Width = 6,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Background = System.Windows.Media.Brushes.Gray,
                        ShowsPreview = false,
                        ResizeDirection = GridResizeDirection.Columns,
                        ResizeBehavior = GridResizeBehavior.PreviousAndNext
                    };

                    splitter.MouseEnter += (s, e) =>
                        splitter.Background = System.Windows.Media.Brushes.DodgerBlue;
                    splitter.MouseLeave += (s, e) =>
                        splitter.Background = System.Windows.Media.Brushes.Gray;

                    Grid.SetColumn(splitter, i * 2 + 1);
                    portsContainer.Children.Add(splitter);
                }
            }

            // 关键修复3：不再设置 portsContainer.MaxWidth
            // 让 Grid 自然适应 ScrollViewer 的宽度（因为滚动条已禁用）

            portsContainer.UpdateLayout();
        }

        // 窗口大小变化时重新布局（保持比例）
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            if (sizeInfo.WidthChanged && _portControls.Count > 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 重新计算布局以适应新窗口大小
                    RebuildLayout();
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
        }

        private async void OnPortControlCloseRequested(object? sender, EventArgs e)
        {
            var control = sender as SerialPortMonitorControl;
            if (control == null) return;

            if (_portControls.Count <= MinPorts)
            {
                MessageBox.Show($"至少需要保留 {MinPorts} 个串口监控，无法关闭", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                control.ResetCloseButtonState();
                return;
            }

            var result = MessageBox.Show($"确定要关闭 {control.TxtTitle.Text} 吗？", "确认关闭",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                control.ResetCloseButtonState();
                return;
            }

            await RemovePortControlAsync(control);
        }

        private async Task RemovePortControlAsync(SerialPortMonitorControl control)
        {
            await Task.Run(() =>
            {
                control.CloseRequested -= OnPortControlCloseRequested;
                control.Cleanup();
            });

            await Dispatcher.InvokeAsync(() =>
            {
                _portControls.Remove(control);
                RebuildLayout();
                RefreshControlIndices();
                UpdateButtonStates();
                UpdateTaskbarConnectedCount();
            });
        }

        private void RemovePortControl(SerialPortMonitorControl control)
        {
            control.CloseRequested -= OnPortControlCloseRequested;
            control.Cleanup();
            _portControls.Remove(control);
        }

        private void RefreshControlIndices()
        {
            for (int i = 0; i < _portControls.Count; i++)
            {
                _portControls[i].ControlIndex = i + 1;
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            AddPortControl();
        }

        private async void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (_portControls.Count <= MinPorts)
            {
                MessageBox.Show($"至少需要保留 {MinPorts} 个串口监控", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var lastControl = _portControls.Last();
            await RemovePortControlAsync(lastControl);
        }

        /// <summary>
        /// 总开关：一键开启或关闭所有串口的监听
        /// </summary>
        private async void BtnMasterToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_portControls.Count == 0)
            {
                MessageBox.Show("没有可用的串口监控", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_isMasterMonitoring)
            {
                // 当前处于监听状态，执行全部关闭
                await MasterStopAllAsync();
            }
            else
            {
                // 当前处于停止状态，执行全部开启
                await MasterStartAllAsync();
            }
        }

        /// <summary>
        /// 一键开启所有串口监听
        /// 【修复】必须在 UI 线程上执行，因为 StartMonitorInternal 访问了 UI 控件
        /// </summary>
        private async Task MasterStartAllAsync()
        {
            // 检查日志文件是否已加载
            if (string.IsNullOrEmpty(_sharedLogFilePath) || !File.Exists(_sharedLogFilePath))
            {
                MessageBox.Show("请先选择日志文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检查是否有未选择串口的控件
            var controlsWithoutPort = _portControls.Where(c => c.SelectedSerialPort == null).ToList();
            if (controlsWithoutPort.Any())
            {
                var result = MessageBox.Show(
                    $"有 {controlsWithoutPort.Count} 个串口未选择串口号，是否继续开启其他已配置串口？",
                    "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No) return;
            }

            int startedCount = 0;
            List<string> failedMessages = new List<string>();

            // 【关键修复】在 UI 线程上串行启动每个串口，避免跨线程访问 UI 控件
            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var control in _portControls)
                {
                    if (control.SelectedSerialPort != null && !control.IsMonitoring)
                    {
                        try
                        {
                            control.StartMonitorInternal();
                            startedCount++;
                        }
                        catch (Exception ex)
                        {
                            failedMessages.Add($"串口 #{control.ControlIndex} 启动失败: {ex.Message}");
                        }
                    }
                }
            });

            if (failedMessages.Any())
            {
                txtStatus.Text = string.Join(" | ", failedMessages);
            }
            else if (startedCount > 0)
            {
                _isMasterMonitoring = true;
                UpdateMasterToggleButtonState();
                UpdateTaskbarConnectedCount();
                txtStatus.Text = $"已开启 {startedCount} 个串口监听 | 日志: {Path.GetFileName(_sharedLogFilePath)}";
            }
            else
            {
                MessageBox.Show("没有可启动的串口（请检查串口号是否已选择）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 一键关闭所有串口监听
        /// 【修复】必须在 UI 线程上执行，因为 StopMonitorInternal 访问了 UI 控件
        /// </summary>
        private async Task MasterStopAllAsync()
        {
            // 【关键修复】在 UI 线程上串行停止每个串口
            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var control in _portControls)
                {
                    if (control.IsMonitoring)
                    {
                        try
                        {
                            control.StopMonitorInternal();
                        }
                        catch { }
                    }
                }
            });

            _isMasterMonitoring = false;
            UpdateMasterToggleButtonState();
            UpdateTaskbarConnectedCount();
            txtStatus.Text = $"已关闭所有串口监听 | 日志: {Path.GetFileName(_sharedLogFilePath)}";
        }

        /// <summary>
        /// 更新总开关按钮的显示状态
        /// </summary>
        private void UpdateMasterToggleButtonState()
        {
            Dispatcher.Invoke(() =>
            {
                if (_isMasterMonitoring)
                {
                    btnMasterToggle.Content = "⏹ 全部关闭";
                    btnMasterToggle.Background = System.Windows.Media.Brushes.OrangeRed;
                    btnMasterToggle.ToolTip = "一键关闭所有串口的监听";
                }
                else
                {
                    btnMasterToggle.Content = "▶ 全部开启";
                    btnMasterToggle.Background = System.Windows.Media.Brushes.Green;
                    btnMasterToggle.ToolTip = "一键开启所有串口的监听";
                }
            });
        }

        /// <summary>
        /// 当某个子串口自行停止时，同步更新总开关状态
        /// </summary>
        public void NotifyChildMonitorStopped()
        {
            bool anyMonitoring = _portControls.Any(c => c.IsMonitoring);
            if (!anyMonitoring && _isMasterMonitoring)
            {
                _isMasterMonitoring = false;
                UpdateMasterToggleButtonState();
            }

            UpdateTaskbarConnectedCount();
        }

        /// <summary>
        /// 当某个子串口自行启动时，同步更新总开关状态
        /// </summary>
        public void NotifyChildMonitorStarted()
        {
            bool allMonitoring = _portControls.All(c => c.IsMonitoring);
            if (allMonitoring && !_isMasterMonitoring)
            {
                _isMasterMonitoring = true;
                UpdateMasterToggleButtonState();
            }

            UpdateTaskbarConnectedCount();
        }

        /// <summary>
        /// 刷新任务栏图标上的已连接串口数角标。
        /// </summary>
        public void UpdateTaskbarConnectedCount()
        {
            int connected = _portControls.Count(c => c.IsMonitoring);
            _taskbarBadge.Update(connected);
        }

        private void BtnSelectLog_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "日志文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                Title = "选择 dbglog_table.txt（所有串口共用）"
            };

            // 如果有上次的路径，设置初始目录
            if (!string.IsNullOrEmpty(_sharedLogFilePath) && File.Exists(_sharedLogFilePath))
            {
                openFileDialog.InitialDirectory = Path.GetDirectoryName(_sharedLogFilePath);
                openFileDialog.FileName = Path.GetFileName(_sharedLogFilePath);
            }

            if (openFileDialog.ShowDialog() == true)
            {
                SharedLogFilePath = openFileDialog.FileName;
                UpdateButtonStates();
            }
        }

        private void UpdateButtonStates()
        {
            int count = _portControls.Count;
            btnAdd.IsEnabled = count < MaxPorts;
            btnRemove.IsEnabled = count > MinPorts;
            string logInfo = string.IsNullOrEmpty(_sharedLogFilePath) ? "未设置日志" : $"日志: {Path.GetFileName(_sharedLogFilePath)}";
            txtStatus.Text = $"当前: {count}个串口 (最少{MinPorts}, 最多{MaxPorts}) | {logInfo}";
            btnAdd.Opacity = btnAdd.IsEnabled ? 1.0 : 0.5;
            btnRemove.Opacity = btnRemove.IsEnabled ? 1.0 : 0.5;
        }

        protected override async void OnClosed(EventArgs e)
        {
            this.IsEnabled = false;

            // 关闭前确保所有串口都停止监听
            if (_isMasterMonitoring)
            {
                await MasterStopAllAsync();
            }

            var cleanupTasks = new List<Task>();

            foreach (var control in _portControls.ToList())
            {
                var task = Task.Run(() =>
                {
                    try
                    {
                        control.CloseRequested -= OnPortControlCloseRequested;
                        control.Cleanup();
                    }
                    catch { }
                });
                cleanupTasks.Add(task);
            }

            await Task.WhenAll(cleanupTasks).WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(_ => { });
            _portControls.Clear();
            UpdateTaskbarConnectedCount();
            base.OnClosed(e);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            foreach (var control in _portControls.ToList())
            {
                try { control.Cleanup(); } catch { }
            }
        }
    }
