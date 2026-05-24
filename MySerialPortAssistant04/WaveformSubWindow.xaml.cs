////////using System;
////////using System.Collections.Generic;
////////using System.Windows;
////////using System.Windows.Controls;
////////using System.Windows.Media;
////////using System.Windows.Shapes;
////////using System.Windows.Threading;

////////namespace MySerialPortAssistant04
////////{
////////    public partial class WaveformSubWindow : Window, ILogReceiver
////////    {
////////        // 数据点类 - 同时保存原始值和解绕值
////////        private class DataPoint
////////        {
////////            public double RawX, RawY, RawZ;                    // 原始真实值 (0-360)
////////            public double UnwrappedX, UnwrappedY, UnwrappedZ;  // 解绕值（用于绘图）
////////        }

////////        private readonly List<DataPoint> _historyData = new List<DataPoint>();
////////        private readonly object _dataLock = new object();

////////        // 配置
////////        private const int SAMPLE_RATE_MS = 20;
////////        private const int MAX_HISTORY_POINTS = 15000;
////////        private const int DISPLAY_POINTS = 1000;
////////        private const double WRAP_THRESHOLD = 180;

////////        // 状态
////////        private bool _isPaused = false;
////////        private bool _isDraggingSlider = false;
////////        private int _viewStartIndex = 0;
////////        private int _timeScale = 1;

////////        // 角度解绕
////////        private double _unwrapOffsetX = 0, _unwrapOffsetY = 0, _unwrapOffsetZ = 0;
////////        private double _lastRawX = 0, _lastRawY = 0, _lastRawZ = 0;
////////        private bool _firstData = true;

////////        // 定时器
////////        private DispatcherTimer? _renderTimer;

////////        // 当前最新原始值（用于实时显示）
////////        private double _currentRawX = 0, _currentRawY = 0, _currentRawZ = 0;

////////        public int ParentPortIndex { get; private set; }
////////        public string ParentPortName => ParentPortIndex > 0 ? $"串口监控 #{ParentPortIndex}" : "未知";

////////        public WaveformSubWindow(int parentPortIndex = 0)
////////        {
////////            ParentPortIndex = parentPortIndex;
////////            InitializeComponent();

////////            Loaded += OnWindowLoaded;
////////            Closed += OnWindowClosed;
////////        }

////////        private void OnWindowLoaded(object sender, RoutedEventArgs e)
////////        {
////////            UpdateParentInfo();

////////            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
////////            _renderTimer.Tick += OnRenderTick;

////////            DrawEmptyWaveform();
////////            _renderTimer.Start();
////////        }

////////        private void OnWindowClosed(object? sender, EventArgs e)
////////        {
////////            _renderTimer?.Stop();
////////        }

////////        private void DrawEmptyWaveform()
////////        {
////////            if (WaveCanvas == null) return;

////////            WaveCanvas.Children.Clear();
////////            YAxisCanvas.Children.Clear();

////////            double w = WaveBorder.ActualWidth > 0 ? WaveBorder.ActualWidth : 1000;
////////            double h = WaveCanvas.ActualHeight > 0 ? WaveCanvas.ActualHeight : 400;

////////            DrawGrid(w, h, 0, 360);
////////            DrawYAxisLabels(0, 360);
////////        }

////////        public void EnqueueLog(string log)
////////        {
////////            if (string.IsNullOrEmpty(log) || !log.Contains("Same_angle")) return;

////////            try
////////            {
////////                double? x = ExtractValue(log, "X");
////////                double? y = ExtractValue(log, "Y");
////////                double? z = ExtractValue(log, "Z");

////////                if (!x.HasValue || !y.HasValue || !z.HasValue) return;

////////                double ux, uy, uz;

////////                if (_firstData)
////////                {
////////                    _lastRawX = x.Value; _lastRawY = y.Value; _lastRawZ = z.Value;
////////                    _firstData = false;
////////                    ux = x.Value; uy = y.Value; uz = z.Value;
////////                }
////////                else
////////                {
////////                    ux = Unwrap(x.Value, ref _lastRawX, ref _unwrapOffsetX);
////////                    uy = Unwrap(y.Value, ref _lastRawY, ref _unwrapOffsetY);
////////                    uz = Unwrap(z.Value, ref _lastRawZ, ref _unwrapOffsetZ);
////////                }

////////                lock (_dataLock)
////////                {
////////                    _historyData.Add(new DataPoint
////////                    {
////////                        RawX = x.Value,
////////                        RawY = y.Value,
////////                        RawZ = z.Value,
////////                        UnwrappedX = ux,
////////                        UnwrappedY = uy,
////////                        UnwrappedZ = uz
////////                    });

////////                    // 更新当前实时值
////////                    _currentRawX = x.Value;
////////                    _currentRawY = y.Value;
////////                    _currentRawZ = z.Value;

////////                    if (_historyData.Count > MAX_HISTORY_POINTS)
////////                        _historyData.RemoveAt(0);

////////                    if (!_isPaused && !_isDraggingSlider)
////////                    {
////////                        int showPoints = DISPLAY_POINTS / _timeScale;
////////                        _viewStartIndex = Math.Max(0, _historyData.Count - showPoints);
////////                    }
////////                }
////////            }
////////            catch { }
////////        }

////////        private static double? ExtractValue(string log, string key)
////////        {
////////            int idx = log.IndexOf(key);
////////            if (idx < 0) return null;

////////            int colon = log.IndexOf(':', idx);
////////            if (colon < 0) return null;

////////            int start = colon + 1;
////////            while (start < log.Length && char.IsWhiteSpace(log[start])) start++;

////////            int end = start;
////////            if (end < log.Length && log[end] == '-') end++;

////////            while (end < log.Length && (char.IsDigit(log[end]) || log[end] == '.')) end++;

////////            if (end == start) return null;

////////            return double.TryParse(log.Substring(start, end - start), out double v) ? v : null;
////////        }

////////        private static double Unwrap(double current, ref double last, ref double offset)
////////        {
////////            double d = current - last;
////////            if (d > WRAP_THRESHOLD) offset -= 360;
////////            else if (d < -WRAP_THRESHOLD) offset += 360;
////////            last = current;
////////            return current + offset;
////////        }

////////        private void OnRenderTick(object? sender, EventArgs e)
////////        {
////////            try
////////            {
////////                DrawWaveform();
////////                UpdateRealTimeDisplay();  // 更新右侧实时数值显示
////////            }
////////            catch (Exception ex)
////////            {
////////                System.Diagnostics.Debug.WriteLine($"Render error: {ex.Message}");
////////            }
////////        }

////////        /// <summary>
////////        /// 更新右侧实时数值显示面板
////////        /// </summary>
////////        private void UpdateRealTimeDisplay()
////////        {
////////            if (TxtRealTimeX == null || TxtRealTimeY == null || TxtRealTimeZ == null) return;

////////            lock (_dataLock)
////////            {
////////                TxtRealTimeX.Text = $"{_currentRawX:F0}°";
////////                TxtRealTimeY.Text = $"{_currentRawY:F0}°";
////////                TxtRealTimeZ.Text = $"{_currentRawZ:F0}°";
////////            }
////////        }

////////        private void DrawWaveform()
////////        {
////////            if (WaveCanvas == null || YAxisCanvas == null) return;

////////            double w = WaveBorder.ActualWidth;
////////            double h = WaveCanvas.ActualHeight;

////////            if (w < 10 || h < 10) return;

////////            DataPoint[] data;
////////            int count;

////////            lock (_dataLock)
////////            {
////////                count = _historyData.Count;
////////                if (count < 2)
////////                {
////////                    DrawEmptyWaveform();
////////                    return;
////////                }

////////                int showPoints = DISPLAY_POINTS / _timeScale;
////////                int endIdx = Math.Min(_viewStartIndex + showPoints, count);
////////                int startIdx = Math.Max(0, endIdx - showPoints);
////////                int dataCount = endIdx - startIdx;

////////                if (dataCount < 2) return;

////////                data = new DataPoint[dataCount];
////////                for (int i = 0; i < dataCount; i++)
////////                    data[i] = _historyData[startIdx + i];
////////            }

////////            // 计算解绕值的范围（用于绘图）
////////            double min = double.MaxValue, max = double.MinValue;
////////            foreach (var pt in data)
////////            {
////////                if (ChkX.IsChecked == true) { min = Math.Min(min, pt.UnwrappedX); max = Math.Max(max, pt.UnwrappedX); }
////////                if (ChkY.IsChecked == true) { min = Math.Min(min, pt.UnwrappedY); max = Math.Max(max, pt.UnwrappedY); }
////////                if (ChkZ.IsChecked == true) { min = Math.Min(min, pt.UnwrappedZ); max = Math.Max(max, pt.UnwrappedZ); }
////////            }

////////            if (min == double.MaxValue) { min = 0; max = 360; }
////////            double range = Math.Max(360, max - min);
////////            min = Math.Floor(min / 30) * 30 - 15;
////////            max = min + range + 30;

////////            // 计算当前显示区域对应的原始角度范围
////////            double rawMin = double.MaxValue, rawMax = double.MinValue;
////////            foreach (var pt in data)
////////            {
////////                if (ChkX.IsChecked == true) { rawMin = Math.Min(rawMin, pt.RawX); rawMax = Math.Max(rawMax, pt.RawX); }
////////                if (ChkY.IsChecked == true) { rawMin = Math.Min(rawMin, pt.RawY); rawMax = Math.Max(rawMax, pt.RawY); }
////////                if (ChkZ.IsChecked == true) { rawMin = Math.Min(rawMin, pt.RawZ); rawMax = Math.Max(rawMax, pt.RawZ); }
////////            }
////////            if (rawMin == double.MaxValue) { rawMin = 0; rawMax = 360; }

////////            // 清空并绘制
////////            WaveCanvas.Children.Clear();
////////            YAxisCanvas.Children.Clear();

////////            DrawGrid(w, h, min, max);
////////            DrawYAxisLabels(min, max);

////////            double step = w / (DISPLAY_POINTS / _timeScale - 1);

////////            if (ChkX.IsChecked == true) DrawLine(data, step, h, min, max, p => p.UnwrappedX, 234, 76, 76);
////////            if (ChkY.IsChecked == true) DrawLine(data, step, h, min, max, p => p.UnwrappedY, 76, 201, 76);
////////            if (ChkZ.IsChecked == true) DrawLine(data, step, h, min, max, p => p.UnwrappedZ, 77, 158, 255);

////////            // 时间指示线
////////            if (_isPaused || _isDraggingSlider)
////////            {
////////                WaveCanvas.Children.Add(new Line
////////                {
////////                    X1 = w / 2,
////////                    Y1 = 0,
////////                    X2 = w / 2,
////////                    Y2 = h,
////////                    Stroke = Brushes.Yellow,
////////                    StrokeThickness = 2,
////////                    StrokeDashArray = new DoubleCollection { 5, 5 }
////////                });
////////            }

////////            UpdateTimeInfo();
////////            UpdateSlider();
////////            TxtParentInfo.Text = $"所属串口: {ParentPortName} | 真实角度: {rawMin:F0}°~{rawMax:F0}° | 显示范围: {min:F0}°~{max:F0}°";
////////        }

////////        private void DrawGrid(double w, double h, double min, double max)
////////        {
////////            double range = max - min;
////////            double interval = range > 720 ? 90 : (range > 360 ? 60 : 30);

////////            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
////////            {
////////                double y = h - (v - min) / range * h;
////////                WaveCanvas.Children.Add(new Line
////////                {
////////                    X1 = 0,
////////                    Y1 = y,
////////                    X2 = w,
////////                    Y2 = y,
////////                    Stroke = Brushes.Gray,
////////                    StrokeThickness = 0.5
////////                });
////////            }

////////            for (int i = 0; i <= 10; i++)
////////            {
////////                double x = w * i / 10;
////////                WaveCanvas.Children.Add(new Line
////////                {
////////                    X1 = x,
////////                    Y1 = 0,
////////                    X2 = x,
////////                    Y2 = h,
////////                    Stroke = Brushes.DarkGray,
////////                    StrokeThickness = 0.5,
////////                    StrokeDashArray = new DoubleCollection { 3, 3 }
////////                });
////////            }
////////        }

////////        private void DrawYAxisLabels(double min, double max)
////////        {
////////            double range = max - min;
////////            double interval = range > 720 ? 90 : (range > 360 ? 60 : 30);
////////            double h = WaveCanvas.ActualHeight;

////////            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
////////            {
////////                double y = h - (v - min) / range * h;

////////                var tb = new TextBlock
////////                {
////////                    Text = $"{v:F0}°",
////////                    Foreground = Brushes.White,
////////                    FontSize = 10,
////////                    Width = 45,
////////                    TextAlignment = TextAlignment.Right
////////                };
////////                YAxisCanvas.Children.Add(tb);
////////                Canvas.SetTop(tb, y - 8);
////////            }
////////        }

////////        private void DrawLine(DataPoint[] data, double step, double h, double min, double range, Func<DataPoint, double> getUnwrapped, byte r, byte g, byte b)
////////        {
////////            var line = new Polyline
////////            {
////////                Stroke = new SolidColorBrush(Color.FromRgb(r, g, b)),
////////                StrokeThickness = 1.5
////////            };

////////            var points = new PointCollection(data.Length);
////////            for (int i = 0; i < data.Length; i++)
////////            {
////////                double y = h - (getUnwrapped(data[i]) - min) / range * h;
////////                points.Add(new Point(i * step, y));
////////            }

////////            line.Points = points;
////////            WaveCanvas.Children.Add(line);
////////        }

////////        private void UpdateTimeInfo()
////////        {
////////            lock (_dataLock)
////////            {
////////                var total = TimeSpan.FromMilliseconds(_historyData.Count * SAMPLE_RATE_MS);
////////                var pos = TimeSpan.FromMilliseconds(_viewStartIndex * SAMPLE_RATE_MS);
////////                TxtTimeInfo.Text = $"{pos.Minutes:D2}:{pos.Seconds:D2} / {total.Minutes:D2}:{total.Seconds:D2}";
////////            }
////////        }

////////        private void UpdateSlider()
////////        {
////////            lock (_dataLock)
////////            {
////////                int max = Math.Max(0, _historyData.Count - DISPLAY_POINTS / _timeScale);
////////                if (!_isDraggingSlider)
////////                {
////////                    SliderHistory.Maximum = max;
////////                    SliderHistory.Value = _viewStartIndex;
////////                }
////////            }
////////        }

////////        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
////////        {
////////            _isPaused = !_isPaused;
////////            if (_isPaused)
////////            {
////////                BtnPlayPause.Content = "▶ 继续";
////////                BtnPlayPause.Background = new SolidColorBrush(Color.FromRgb(0xEA, 0x4C, 0x4C));
////////                TxtStatus.Text = "已暂停 - 可拖动查看历史";
////////                TxtStatus.Foreground = Brushes.Orange;
////////            }
////////            else
////////            {
////////                BtnPlayPause.Content = "⏸ 暂停";
////////                BtnPlayPause.Background = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x4E));
////////                TxtStatus.Text = "实时模式";
////////                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
////////                lock (_dataLock)
////////                {
////////                    _viewStartIndex = Math.Max(0, _historyData.Count - DISPLAY_POINTS / _timeScale);
////////                }
////////            }
////////        }

////////        private void BtnClear_Click(object sender, RoutedEventArgs e)
////////        {
////////            lock (_dataLock)
////////            {
////////                _historyData.Clear();
////////                _viewStartIndex = 0;
////////                _unwrapOffsetX = _unwrapOffsetY = _unwrapOffsetZ = 0;
////////                _firstData = true;
////////                _currentRawX = _currentRawY = _currentRawZ = 0;
////////            }
////////            DrawEmptyWaveform();
////////            UpdateRealTimeDisplay();
////////            TxtTimeInfo.Text = "00:00 / 00:00";
////////            SliderHistory.Value = 0;
////////            SliderHistory.Maximum = 0;
////////        }

////////        private void SliderHistory_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
////////        {
////////            _isDraggingSlider = true;
////////            TxtStatus.Text = "拖动查看历史...";
////////            TxtStatus.Foreground = Brushes.Yellow;
////////        }

////////        private void SliderHistory_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
////////        {
////////            _isDraggingSlider = false;
////////            TxtStatus.Text = _isPaused ? "已暂停 - 可拖动查看历史" : "实时模式";
////////            TxtStatus.Foreground = _isPaused ? Brushes.Orange : new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
////////        }

////////        private void SliderHistory_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
////////        {
////////            if (_isDraggingSlider)
////////            {
////////                _viewStartIndex = (int)e.NewValue;
////////            }
////////        }

////////        private void CmbTimeScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
////////        {
////////            _timeScale = CmbTimeScale.SelectedIndex switch
////////            {
////////                0 => 1,
////////                1 => 2,
////////                2 => 5,
////////                3 => 10,
////////                _ => 1
////////            };

////////            if (!_isPaused && !_isDraggingSlider)
////////            {
////////                lock (_dataLock)
////////                {
////////                    _viewStartIndex = Math.Max(0, _historyData.Count - DISPLAY_POINTS / _timeScale);
////////                }
////////            }
////////        }

////////        private void UpdateParentInfo()
////////        {
////////            TxtParentInfo.Text = $"所属串口: {ParentPortName} | 角度 0~360° | 历史: 5分钟";
////////        }
////////    }
////////}

//////using System;
//////using System.Collections.Generic;
//////using System.Windows;
//////using System.Windows.Controls;
//////using System.Windows.Media;
//////using System.Windows.Shapes;
//////using System.Windows.Threading;
//////using System.Text.RegularExpressions;

//////namespace MySerialPortAssistant04
//////{
//////    public partial class WaveformSubWindow : Window, ILogReceiver
//////    {
//////        private class DataPoint
//////        {
//////            public double RawX, RawY, RawZ;
//////            public double UnwrappedX, UnwrappedY, UnwrappedZ;
//////        }

//////        private readonly List<DataPoint> _historyData = new List<DataPoint>();
//////        private readonly object _dataLock = new object();

//////        private const int SAMPLE_RATE_MS = 20;
//////        private const int MAX_HISTORY_POINTS = 15000;
//////        private const int DISPLAY_POINTS = 1000;
//////        private const double WRAP_THRESHOLD = 180;

//////        private bool _isPaused = false;
//////        private bool _isDraggingSlider = false;
//////        private int _viewStartIndex = 0;
//////        private int _timeScale = 1;

//////        private double _unwrapOffsetX = 0, _unwrapOffsetY = 0, _unwrapOffsetZ = 0;
//////        private double _lastRawX = 0, _lastRawY = 0, _lastRawZ = 0;
//////        private bool _firstData = true;

//////        private DispatcherTimer? _renderTimer;
//////        private DispatcherTimer? _resizeTimer;

//////        private double _currentRawX = 0, _currentRawY = 0, _currentRawZ = 0;

//////        // 渲染优化：复用线条，不重建
//////        private Polyline _lineX, _lineY, _lineZ;
//////        private Line _crossVerticalLine, _crossHorizontalLine;

//////        // Y轴缓动
//////        private double _lastMin = 0, _lastMax = 360;
//////        private const double Y_AXIS_FOLLOW_SPEED = 0.15;

//////        // 鼠标十字光标
//////        private Point _mousePoint;
//////        private bool _isMouseOverChart = false;

//////        public int ParentPortIndex { get; private set; }
//////        public string ParentPortName => ParentPortIndex > 0 ? $"串口监控 #{ParentPortIndex}" : "未知";

//////        public WaveformSubWindow(int parentPortIndex = 0)
//////        {
//////            ParentPortIndex = parentPortIndex;
//////            InitializeComponent();
//////            InitializeReusableLines();
//////            InitializeCrossCursor();

//////            Loaded += OnWindowLoaded;
//////            Closed += OnWindowClosed;
//////            SizeChanged += (s, e) => _resizeTimer?.Start();
//////        }

//////        private void InitializeReusableLines()
//////        {
//////            _lineX = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(234, 76, 76)), StrokeThickness = 1.5 };
//////            _lineY = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(76, 201, 76)), StrokeThickness = 1.5 };
//////            _lineZ = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(77, 158, 255)), StrokeThickness = 1.5 };
//////        }

//////        private void InitializeCrossCursor()
//////        {
//////            _crossVerticalLine = new Line { Stroke = Brushes.Cyan, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } };
//////            _crossHorizontalLine = new Line { Stroke = Brushes.Cyan, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } };
//////        }

//////        private void OnWindowLoaded(object sender, RoutedEventArgs e)
//////        {
//////            UpdateParentInfo();

//////            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
//////            _renderTimer.Tick += OnRenderTick;
//////            _renderTimer.Start();

//////            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
//////            _resizeTimer.Tick += (s, e) => { _resizeTimer.Stop(); DrawEmptyWaveform(); };

//////            WaveCanvas.MouseMove += WaveCanvas_MouseMove;
//////            WaveCanvas.MouseLeave += (s, e) => _isMouseOverChart = false;
//////            WaveCanvas.MouseEnter += (s, e) => _isMouseOverChart = true;

//////            ChkX.Checked += (s, e) => DrawWaveform();
//////            ChkX.Unchecked += (s, e) => DrawWaveform();
//////            ChkY.Checked += (s, e) => DrawWaveform();
//////            ChkY.Unchecked += (s, e) => DrawWaveform();
//////            ChkZ.Checked += (s, e) => DrawWaveform();
//////            ChkZ.Unchecked += (s, e) => DrawWaveform();

//////            DrawEmptyWaveform();
//////        }

//////        private void WaveCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
//////        {
//////            _mousePoint = e.GetPosition(WaveCanvas);
//////        }

//////        private void OnWindowClosed(object? sender, EventArgs e)
//////        {
//////            _renderTimer?.Stop();
//////            _resizeTimer?.Stop();
//////        }

//////        private void DrawEmptyWaveform()
//////        {
//////            if (WaveCanvas == null) return;
//////            WaveCanvas.Children.Clear();
//////            YAxisCanvas.Children.Clear();

//////            WaveCanvas.Children.Add(_lineX);
//////            WaveCanvas.Children.Add(_lineY);
//////            WaveCanvas.Children.Add(_lineZ);
//////            WaveCanvas.Children.Add(_crossVerticalLine);
//////            WaveCanvas.Children.Add(_crossHorizontalLine);

//////            double w = WaveBorder.ActualWidth > 0 ? WaveBorder.ActualWidth : 1000;
//////            double h = WaveCanvas.ActualHeight > 0 ? WaveCanvas.ActualHeight : 400;
//////            DrawGrid(w, h, 0, 360);
//////            DrawYAxisLabels(0, 360);
//////        }

//////        public void EnqueueLog(string log)
//////        {
//////            if (string.IsNullOrEmpty(log) || !log.Contains("Same_angle")) return;

//////            try
//////            {
//////                double x = GetAngle(log, "X");
//////                double y = GetAngle(log, "Y");
//////                double z = GetAngle(log, "Z");

//////                if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) return;

//////                double ux, uy, uz;

//////                if (_firstData)
//////                {
//////                    _lastRawX = x; _lastRawY = y; _lastRawZ = z;
//////                    _firstData = false;
//////                    ux = x; uy = y; uz = z;
//////                }
//////                else
//////                {
//////                    ux = Unwrap(x, ref _lastRawX, ref _unwrapOffsetX);
//////                    uy = Unwrap(y, ref _lastRawY, ref _unwrapOffsetY);
//////                    uz = Unwrap(z, ref _lastRawZ, ref _unwrapOffsetZ);
//////                }

//////                lock (_dataLock)
//////                {
//////                    _historyData.Add(new DataPoint
//////                    {
//////                        RawX = x,
//////                        RawY = y,
//////                        RawZ = z,
//////                        UnwrappedX = ux,
//////                        UnwrappedY = uy,
//////                        UnwrappedZ = uz
//////                    });

//////                    _currentRawX = x;
//////                    _currentRawY = y;
//////                    _currentRawZ = z;

//////                    if (_historyData.Count > MAX_HISTORY_POINTS)
//////                        _historyData.RemoveAt(0);

//////                    if (!_isPaused && !_isDraggingSlider)
//////                    {
//////                        int show = DISPLAY_POINTS / _timeScale;
//////                        _viewStartIndex = Math.Max(0, _historyData.Count - show);
//////                    }
//////                }
//////            }
//////            catch { }
//////        }

//////        private readonly Regex _angleRegex = new Regex(@"([XYZ])[:=]\s*([-\d\.]+)", RegexOptions.Compiled);
//////        private double GetAngle(string log, string axis)
//////        {
//////            var match = _angleRegex.Match(log);
//////            while (match.Success)
//////            {
//////                if (match.Groups[1].Value == axis)
//////                    return double.TryParse(match.Groups[2].Value, out double v) ? v : double.NaN;
//////                match = match.NextMatch();
//////            }
//////            return double.NaN;
//////        }

//////        private static double Unwrap(double current, ref double last, ref double offset)
//////        {
//////            double d = current - last;
//////            if (d > WRAP_THRESHOLD) offset -= 360;
//////            else if (d < -WRAP_THRESHOLD) offset += 360;
//////            last = current;
//////            return current + offset;
//////        }

//////        private void OnRenderTick(object? sender, EventArgs e)
//////        {
//////            try { DrawWaveform(); UpdateRealTimeDisplay(); }
//////            catch { }
//////        }

//////        private void UpdateRealTimeDisplay()
//////        {
//////            if (TxtRealTimeX == null) return;
//////            lock (_dataLock)
//////            {
//////                TxtRealTimeX.Text = $"{_currentRawX:F0}°";
//////                TxtRealTimeY.Text = $"{_currentRawY:F0}°";
//////                TxtRealTimeZ.Text = $"{_currentRawZ:F0}°";
//////            }
//////        }

//////        private void DrawWaveform()
//////        {
//////            if (WaveCanvas == null) return;
//////            double w = WaveBorder.ActualWidth;
//////            double h = WaveCanvas.ActualHeight;
//////            if (w < 10 || h < 10) return;

//////            DataPoint[] data;
//////            lock (_dataLock)
//////            {
//////                int count = _historyData.Count;
//////                if (count < 2) { DrawEmptyWaveform(); return; }
//////                int show = DISPLAY_POINTS / _timeScale;
//////                int end = Math.Min(_viewStartIndex + show, count);
//////                int start = Math.Max(0, end - show);
//////                data = _historyData.GetRange(start, end - start).ToArray();
//////            }

//////            // 计算范围
//////            double min = double.MaxValue, max = double.MinValue;
//////            foreach (var p in data)
//////            {
//////                if (ChkX.IsChecked == true) { min = Math.Min(min, p.UnwrappedX); max = Math.Max(max, p.UnwrappedX); }
//////                if (ChkY.IsChecked == true) { min = Math.Min(min, p.UnwrappedY); max = Math.Max(max, p.UnwrappedY); }
//////                if (ChkZ.IsChecked == true) { min = Math.Min(min, p.UnwrappedZ); max = Math.Max(max, p.UnwrappedZ); }
//////            }

//////            if (min == double.MaxValue) { min = 0; max = 360; }
//////            double targetMin = Math.Floor(min / 30) * 30 - 15;
//////            double targetMax = targetMin + Math.Max(360, max - min) + 30;

//////            _lastMin = _lastMin + (targetMin - _lastMin) * Y_AXIS_FOLLOW_SPEED;
//////            _lastMax = _lastMax + (targetMax - _lastMax) * Y_AXIS_FOLLOW_SPEED;
//////            double range = _lastMax - _lastMin;

//////            // 重绘网格
//////            WaveCanvas.Children.Clear();
//////            WaveCanvas.Children.Add(_lineX);
//////            WaveCanvas.Children.Add(_lineY);
//////            WaveCanvas.Children.Add(_lineZ);
//////            WaveCanvas.Children.Add(_crossVerticalLine);
//////            WaveCanvas.Children.Add(_crossHorizontalLine);
//////            YAxisCanvas.Children.Clear();

//////            DrawGrid(w, h, _lastMin, _lastMax);
//////            DrawYAxisLabels(_lastMin, _lastMax);

//////            double step = w / (data.Length - 1);

//////            _lineX.Points = ChkX.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedX) : new PointCollection();
//////            _lineY.Points = ChkY.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedY) : new PointCollection();
//////            _lineZ.Points = ChkZ.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedZ) : new PointCollection();

//////            // 暂停指示线
//////            if (_isPaused || _isDraggingSlider)
//////            {
//////                WaveCanvas.Children.Add(new Line
//////                {
//////                    X1 = w / 2,
//////                    Y1 = 0,
//////                    X2 = w / 2,
//////                    Y2 = h,
//////                    Stroke = Brushes.Yellow,
//////                    StrokeThickness = 2,
//////                    StrokeDashArray = new DoubleCollection { 5, 5 }
//////                });
//////            }

//////            // 鼠标十字光标
//////            if (_isMouseOverChart)
//////            {
//////                _crossVerticalLine.X1 = _mousePoint.X; _crossVerticalLine.X2 = _mousePoint.X;
//////                _crossVerticalLine.Y1 = 0; _crossVerticalLine.Y2 = h;
//////                _crossHorizontalLine.Y1 = _mousePoint.Y; _crossHorizontalLine.Y2 = _mousePoint.Y;
//////                _crossHorizontalLine.X1 = 0; _crossHorizontalLine.X2 = w;
//////            }
//////            else
//////            {
//////                _crossVerticalLine.Visibility = Visibility.Collapsed;
//////                _crossHorizontalLine.Visibility = Visibility.Collapsed;
//////            }

//////            UpdateTimeInfo();
//////            UpdateSlider();
//////        }

//////        private PointCollection GetPoints(DataPoint[] data, double step, double h, double min, double range, Func<DataPoint, double> sel)
//////        {
//////            var pc = new PointCollection(data.Length);
//////            for (int i = 0; i < data.Length; i++)
//////            {
//////                double v = sel(data[i]);
//////                double y = h - (v - min) / range * h;
//////                pc.Add(new Point(i * step, y));
//////            }
//////            return pc;
//////        }

//////        private void DrawGrid(double w, double h, double min, double max)
//////        {
//////            double range = max - min;
//////            double interval = range > 720 ? 90 : (range > 360 ? 60 : 30);
//////            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
//////            {
//////                double y = h - (v - min) / range * h;
//////                WaveCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = Brushes.Gray, StrokeThickness = 0.5 });
//////            }
//////            for (int i = 0; i <= 10; i++)
//////            {
//////                double x = w * i / 10;
//////                WaveCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = Brushes.DarkGray, StrokeThickness = 0.5, StrokeDashArray = new DoubleCollection { 3, 3 } });
//////            }
//////        }

//////        private void DrawYAxisLabels(double min, double max)
//////        {
//////            double h = WaveCanvas.ActualHeight;
//////            double range = max - min;
//////            double interval = range > 720 ? 90 : (range > 360 ? 60 : 30);
//////            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
//////            {
//////                double y = h - (v - min) / range * h;
//////                YAxisCanvas.Children.Add(new TextBlock
//////                {
//////                    Text = $"{v:F0}°",
//////                    Foreground = Brushes.White,
//////                    FontSize = 10,
//////                    Width = 45,
//////                    TextAlignment = TextAlignment.Right
//////                });
//////                Canvas.SetTop(YAxisCanvas.Children[^1], y - 8);
//////            }
//////        }

//////        private void UpdateTimeInfo()
//////        {
//////            lock (_dataLock)
//////            {
//////                var total = TimeSpan.FromMilliseconds(_historyData.Count * SAMPLE_RATE_MS);
//////                var pos = TimeSpan.FromMilliseconds(_viewStartIndex * SAMPLE_RATE_MS);
//////                TxtTimeInfo.Text = $"{pos.Minutes:D2}:{pos.Seconds:D2} / {total.Minutes:D2}:{total.Seconds:D2}";
//////            }
//////        }

//////        private void UpdateSlider()
//////        {
//////            lock (_dataLock)
//////            {
//////                int max = Math.Max(0, _historyData.Count - DISPLAY_POINTS / _timeScale);
//////                if (!_isDraggingSlider)
//////                {
//////                    SliderHistory.Maximum = max;
//////                    SliderHistory.Value = _viewStartIndex;
//////                }
//////            }
//////        }

//////        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
//////        {
//////            _isPaused = !_isPaused;
//////            BtnPlayPause.Content = _isPaused ? "▶ 继续" : "⏸ 暂停";
//////            BtnPlayPause.Background = new SolidColorBrush(_isPaused ? Color.FromRgb(0xEA, 0x4C, 0x4C) : Color.FromRgb(0x4E, 0xC9, 0x4E));
//////            TxtStatus.Text = _isPaused ? "已暂停 - 可拖动查看历史" : "实时模式";
//////            TxtStatus.Foreground = _isPaused ? Brushes.Orange : Brushes.Gray;
//////        }

//////        private void BtnClear_Click(object sender, RoutedEventArgs e)
//////        {
//////            lock (_dataLock)
//////            {
//////                _historyData.Clear();
//////                _viewStartIndex = 0;
//////                _unwrapOffsetX = _unwrapOffsetY = _unwrapOffsetZ = 0;
//////                _firstData = true;
//////                _currentRawX = _currentRawY = _currentRawZ = 0;
//////            }
//////            DrawEmptyWaveform();
//////            UpdateRealTimeDisplay();
//////        }

//////        private void SliderHistory_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _isDraggingSlider = true;
//////        private void SliderHistory_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => _isDraggingSlider = false;
//////        private void SliderHistory_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_isDraggingSlider) _viewStartIndex = (int)e.NewValue; }

//////        private void CmbTimeScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
//////        {
//////            _timeScale = CmbTimeScale.SelectedIndex switch { 0 => 1, 1 => 2, 2 => 5, 3 => 10, _ => 1 };
//////            if (!_isPaused && !_isDraggingSlider) lock (_dataLock) _viewStartIndex = Math.Max(0, _historyData.Count - DISPLAY_POINTS / _timeScale);
//////        }

//////        private void UpdateParentInfo() => TxtParentInfo.Text = $"所属串口: {ParentPortName} | 角度 0~360° | 已优化渲染";
//////    }
//////}

////using System;
////using System.Collections.Generic;
////using System.Windows;
////using System.Windows.Controls;
////using System.Windows.Media;
////using System.Windows.Shapes;
////using System.Windows.Threading;
////using System.Text.RegularExpressions;

////namespace MySerialPortAssistant04
////{
////    public partial class WaveformSubWindow : Window, ILogReceiver
////    {
////        private class DataPoint
////        {
////            public double RawX, RawY, RawZ;
////            public double UnwrappedX, UnwrappedY, UnwrappedZ;
////        }

////        private readonly List<DataPoint> _historyData = new List<DataPoint>();
////        private readonly object _dataLock = new object();

////        private const int SAMPLE_RATE_MS = 20;
////        private const int MAX_HISTORY_POINTS = 15000;
////        private const int DISPLAY_POINTS = 1000;
////        private const double WRAP_THRESHOLD = 180;

////        private bool _isPaused = false;
////        private bool _isDraggingSlider = false;
////        private int _viewStartIndex = 0;
////        private int _timeScale = 1;

////        private double _unwrapOffsetX = 0, _unwrapOffsetY = 0, _unwrapOffsetZ = 0;
////        private double _lastRawX = 0, _lastRawY = 0, _lastRawZ = 0;
////        private bool _firstData = true;

////        private DispatcherTimer? _renderTimer;
////        private DispatcherTimer? _resizeTimer;

////        private double _currentRawX = 0, _currentRawY = 0, _currentRawZ = 0;

////        // 渲染优化：复用线条，不重建
////        private Polyline _lineX, _lineY, _lineZ;
////        private Line _crossVerticalLine, _crossHorizontalLine;

////        // Y轴缓动
////        private double _lastMin = 0, _lastMax = 360;
////        private const double Y_AXIS_FOLLOW_SPEED = 0.15;

////        // 鼠标十字光标
////        private Point _mousePoint;
////        private bool _isMouseOverChart = false;

////        public int ParentPortIndex { get; private set; }
////        public string ParentPortName => ParentPortIndex > 0 ? $"串口监控 #{ParentPortIndex}" : "未知";

////        public WaveformSubWindow(int parentPortIndex = 0)
////        {
////            ParentPortIndex = parentPortIndex;
////            InitializeComponent();
////            InitializeReusableLines();
////            InitializeCrossCursor();

////            Loaded += OnWindowLoaded;
////            Closed += OnWindowClosed;
////            SizeChanged += (s, e) => _resizeTimer?.Start();
////        }

////        private void InitializeReusableLines()
////        {
////            _lineX = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(234, 76, 76)), StrokeThickness = 1.5 };
////            _lineY = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(76, 201, 76)), StrokeThickness = 1.5 };
////            _lineZ = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(77, 158, 255)), StrokeThickness = 1.5 };
////        }

////        private void InitializeCrossCursor()
////        {
////            _crossVerticalLine = new Line { Stroke = Brushes.Cyan, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } };
////            _crossHorizontalLine = new Line { Stroke = Brushes.Cyan, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } };
////        }

////        private void OnWindowLoaded(object sender, RoutedEventArgs e)
////        {
////            UpdateParentInfo();

////            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
////            _renderTimer.Tick += OnRenderTick;
////            _renderTimer.Start();

////            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
////            _resizeTimer.Tick += (s, e) => { _resizeTimer.Stop(); DrawEmptyWaveform(); };

////            WaveCanvas.MouseMove += WaveCanvas_MouseMove;
////            WaveCanvas.MouseLeave += (s, e) => _isMouseOverChart = false;
////            WaveCanvas.MouseEnter += (s, e) => _isMouseOverChart = true;

////            ChkX.Checked += (s, e) => DrawWaveform();
////            ChkX.Unchecked += (s, e) => DrawWaveform();
////            ChkY.Checked += (s, e) => DrawWaveform();
////            ChkY.Unchecked += (s, e) => DrawWaveform();
////            ChkZ.Checked += (s, e) => DrawWaveform();
////            ChkZ.Unchecked += (s, e) => DrawWaveform();

////            DrawEmptyWaveform();
////        }

////        private void WaveCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
////        {
////            _mousePoint = e.GetPosition(WaveCanvas);
////        }

////        private void OnWindowClosed(object? sender, EventArgs e)
////        {
////            _renderTimer?.Stop();
////            _resizeTimer?.Stop();
////        }

////        private void DrawEmptyWaveform()
////        {
////            if (WaveCanvas == null) return;
////            WaveCanvas.Children.Clear();
////            YAxisCanvas.Children.Clear();

////            WaveCanvas.Children.Add(_lineX);
////            WaveCanvas.Children.Add(_lineY);
////            WaveCanvas.Children.Add(_lineZ);
////            WaveCanvas.Children.Add(_crossVerticalLine);
////            WaveCanvas.Children.Add(_crossHorizontalLine);

////            double w = WaveBorder.ActualWidth > 0 ? WaveBorder.ActualWidth : 1000;
////            double h = WaveCanvas.ActualHeight > 0 ? WaveCanvas.ActualHeight : 400;
////            DrawGrid(w, h, 0, 360);
////            DrawYAxisLabels(0, 360);
////        }

////        public void EnqueueLog(string log)
////        {
////            if (string.IsNullOrEmpty(log) || !log.Contains("Same_angle")) return;

////            try
////            {
////                double x = GetAngle(log, "X");
////                double y = GetAngle(log, "Y");
////                double z = GetAngle(log, "Z");

////                if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) return;

////                double ux, uy, uz;

////                if (_firstData)
////                {
////                    _lastRawX = x; _lastRawY = y; _lastRawZ = z;
////                    _firstData = false;
////                    ux = x; uy = y; uz = z;
////                }
////                else
////                {
////                    ux = Unwrap(x, ref _lastRawX, ref _unwrapOffsetX);
////                    uy = Unwrap(y, ref _lastRawY, ref _unwrapOffsetY);
////                    uz = Unwrap(z, ref _lastRawZ, ref _unwrapOffsetZ);
////                }

////                lock (_dataLock)
////                {
////                    _historyData.Add(new DataPoint
////                    {
////                        RawX = x,
////                        RawY = y,
////                        RawZ = z,
////                        UnwrappedX = ux,
////                        UnwrappedY = uy,
////                        UnwrappedZ = uz
////                    });

////                    _currentRawX = x;
////                    _currentRawY = y;
////                    _currentRawZ = z;

////                    if (_historyData.Count > MAX_HISTORY_POINTS)
////                        _historyData.RemoveAt(0);

////                    if (!_isPaused && !_isDraggingSlider)
////                    {
////                        int show = DISPLAY_POINTS / _timeScale;
////                        _viewStartIndex = Math.Max(0, _historyData.Count - show);
////                    }
////                }
////            }
////            catch { }
////        }

////        private readonly Regex _angleRegex = new Regex(@"([XYZ])[:=]\s*([-\d\.]+)", RegexOptions.Compiled);
////        private double GetAngle(string log, string axis)
////        {
////            var match = _angleRegex.Match(log);
////            while (match.Success)
////            {
////                if (match.Groups[1].Value == axis)
////                    return double.TryParse(match.Groups[2].Value, out double v) ? v : double.NaN;
////                match = match.NextMatch();
////            }
////            return double.NaN;
////        }

////        private static double Unwrap(double current, ref double last, ref double offset)
////        {
////            double d = current - last;
////            if (d > WRAP_THRESHOLD) offset -= 360;
////            else if (d < -WRAP_THRESHOLD) offset += 360;
////            last = current;
////            return current + offset;
////        }

////        private void OnRenderTick(object? sender, EventArgs e)
////        {
////            try { DrawWaveform(); UpdateRealTimeDisplay(); }
////            catch { }
////        }

////        private void UpdateRealTimeDisplay()
////        {
////            if (TxtRealTimeX == null) return;
////            lock (_dataLock)
////            {
////                TxtRealTimeX.Text = $"{_currentRawX:F0}°";
////                TxtRealTimeY.Text = $"{_currentRawY:F0}°";
////                TxtRealTimeZ.Text = $"{_currentRawZ:F0}°";
////            }
////        }

////        private void DrawWaveform()
////        {
////            if (WaveCanvas == null) return;
////            double w = WaveBorder.ActualWidth;
////            double h = WaveCanvas.ActualHeight;
////            if (w < 10 || h < 10) return;

////            DataPoint[] data;
////            lock (_dataLock)
////            {
////                int count = _historyData.Count;
////                if (count < 2) { DrawEmptyWaveform(); return; }
////                int show = DISPLAY_POINTS / _timeScale;
////                int end = Math.Min(_viewStartIndex + show, count);
////                int start = Math.Max(0, end - show);
////                data = _historyData.GetRange(start, end - start).ToArray();
////            }

////            // 计算范围
////            double min = double.MaxValue, max = double.MinValue;
////            foreach (var p in data)
////            {
////                if (ChkX.IsChecked == true) { min = Math.Min(min, p.UnwrappedX); max = Math.Max(max, p.UnwrappedX); }
////                if (ChkY.IsChecked == true) { min = Math.Min(min, p.UnwrappedY); max = Math.Max(max, p.UnwrappedY); }
////                if (ChkZ.IsChecked == true) { min = Math.Min(min, p.UnwrappedZ); max = Math.Max(max, p.UnwrappedZ); }
////            }

////            if (min == double.MaxValue) { min = 0; max = 360; }
////            double targetMin = Math.Floor(min / 30) * 30 - 15;
////            double targetMax = targetMin + Math.Max(360, max - min) + 30;

////            _lastMin = _lastMin + (targetMin - _lastMin) * Y_AXIS_FOLLOW_SPEED;
////            _lastMax = _lastMax + (targetMax - _lastMax) * Y_AXIS_FOLLOW_SPEED;
////            double range = _lastMax - _lastMin;

////            // 重绘网格
////            WaveCanvas.Children.Clear();
////            WaveCanvas.Children.Add(_lineX);
////            WaveCanvas.Children.Add(_lineY);
////            WaveCanvas.Children.Add(_lineZ);
////            WaveCanvas.Children.Add(_crossVerticalLine);
////            WaveCanvas.Children.Add(_crossHorizontalLine);
////            YAxisCanvas.Children.Clear();

////            DrawGrid(w, h, _lastMin, _lastMax);
////            DrawYAxisLabels(_lastMin, _lastMax);

////            double step = w / (data.Length - 1);

////            _lineX.Points = ChkX.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedX) : new PointCollection();
////            _lineY.Points = ChkY.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedY) : new PointCollection();
////            _lineZ.Points = ChkZ.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedZ) : new PointCollection();

////            // 暂停指示线
////            if (_isPaused || _isDraggingSlider)
////            {
////                WaveCanvas.Children.Add(new Line
////                {
////                    X1 = w / 2,
////                    Y1 = 0,
////                    X2 = w / 2,
////                    Y2 = h,
////                    Stroke = Brushes.Yellow,
////                    StrokeThickness = 2,
////                    StrokeDashArray = new DoubleCollection { 5, 5 }
////                });
////            }

////            // 鼠标十字光标
////            if (_isMouseOverChart)
////            {
////                _crossVerticalLine.X1 = _mousePoint.X; _crossVerticalLine.X2 = _mousePoint.X;
////                _crossVerticalLine.Y1 = 0; _crossVerticalLine.Y2 = h;
////                _crossHorizontalLine.Y1 = _mousePoint.Y; _crossHorizontalLine.Y2 = _mousePoint.Y;
////                _crossHorizontalLine.X1 = 0; _crossHorizontalLine.X2 = w;
////            }
////            else
////            {
////                _crossVerticalLine.Visibility = Visibility.Collapsed;
////                _crossHorizontalLine.Visibility = Visibility.Collapsed;
////            }

////            UpdateTimeInfo();
////            UpdateSlider();
////        }

////        private PointCollection GetPoints(DataPoint[] data, double step, double h, double min, double range, Func<DataPoint, double> sel)
////        {
////            var pc = new PointCollection(data.Length);
////            for (int i = 0; i < data.Length; i++)
////            {
////                double v = sel(data[i]);
////                double y = h - (v - min) / range * h;
////                pc.Add(new Point(i * step, y));
////            }
////            return pc;
////        }

////        // ========== 修改后的刻度绘制方法 ==========
////        private void DrawGrid(double w, double h, double min, double max)
////        {
////            double range = max - min;
////            // 刻度间隔更细：大范围用30°，中等范围用15°，小范围用10°或5°
////            double interval = range > 720 ? 30 : (range > 360 ? 15 : (range > 180 ? 10 : 5));

////            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
////            {
////                double y = h - (v - min) / range * h;
////                // 主刻度线（整90或整60的倍数）用稍粗的线
////                bool isMajorTick = (Math.Abs(v) % 90 < 0.1) || (Math.Abs(v) % 60 < 0.1);
////                WaveCanvas.Children.Add(new Line
////                {
////                    X1 = 0,
////                    Y1 = y,
////                    X2 = w,
////                    Y2 = y,
////                    Stroke = isMajorTick ? Brushes.LightGray : Brushes.Gray,
////                    StrokeThickness = isMajorTick ? 0.8 : 0.3,
////                    Opacity = isMajorTick ? 0.6 : 0.3
////                });
////            }

////            // 垂直刻度也更密集：从10格改为20格
////            for (int i = 0; i <= 20; i++)
////            {
////                double x = w * i / 20;
////                WaveCanvas.Children.Add(new Line
////                {
////                    X1 = x,
////                    Y1 = 0,
////                    X2 = x,
////                    Y2 = h,
////                    Stroke = Brushes.DarkGray,
////                    StrokeThickness = 0.3,
////                    StrokeDashArray = new DoubleCollection { 2, 4 },
////                    Opacity = 0.4
////                });
////            }
////        }

////        private void DrawYAxisLabels(double min, double max)
////        {
////            double h = WaveCanvas.ActualHeight;
////            double range = max - min;
////            // 与网格线保持一致
////            double interval = range > 720 ? 30 : (range > 360 ? 15 : (range > 180 ? 10 : 5));

////            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
////            {
////                double y = h - (v - min) / range * h;

////                // 只显示主要刻度标签，避免拥挤
////                bool showLabel = range > 360 ? (Math.Abs(v) % 30 < 0.1) : (Math.Abs(v) % 15 < 0.1);

////                if (showLabel)
////                {
////                    YAxisCanvas.Children.Add(new TextBlock
////                    {
////                        Text = $"{v:F0}°",
////                        Foreground = Brushes.White,
////                        FontSize = 9,  // 字体稍小以适应更密集的刻度
////                        Width = 45,
////                        TextAlignment = TextAlignment.Right
////                    });
////                    Canvas.SetTop(YAxisCanvas.Children[YAxisCanvas.Children.Count - 1], y - 6);
////                }

////                // 添加短刻度线标记
////                var tick = new Line
////                {
////                    X1 = 42,
////                    Y1 = y,
////                    X2 = 48,
////                    Y2 = y,
////                    Stroke = Brushes.Gray,
////                    StrokeThickness = 0.5
////                };
////                YAxisCanvas.Children.Add(tick);
////            }
////        }
////        // ========== 修改结束 ==========

////        private void UpdateTimeInfo()
////        {
////            lock (_dataLock)
////            {
////                var total = TimeSpan.FromMilliseconds(_historyData.Count * SAMPLE_RATE_MS);
////                var pos = TimeSpan.FromMilliseconds(_viewStartIndex * SAMPLE_RATE_MS);
////                TxtTimeInfo.Text = $"{pos.Minutes:D2}:{pos.Seconds:D2} / {total.Minutes:D2}:{total.Seconds:D2}";
////            }
////        }

////        private void UpdateSlider()
////        {
////            lock (_dataLock)
////            {
////                int max = Math.Max(0, _historyData.Count - DISPLAY_POINTS / _timeScale);
////                if (!_isDraggingSlider)
////                {
////                    SliderHistory.Maximum = max;
////                    SliderHistory.Value = _viewStartIndex;
////                }
////            }
////        }

////        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
////        {
////            _isPaused = !_isPaused;
////            BtnPlayPause.Content = _isPaused ? "▶ 继续" : "⏸ 暂停";
////            BtnPlayPause.Background = new SolidColorBrush(_isPaused ? Color.FromRgb(0xEA, 0x4C, 0x4C) : Color.FromRgb(0x4E, 0xC9, 0x4E));
////            TxtStatus.Text = _isPaused ? "已暂停 - 可拖动查看历史" : "实时模式";
////            TxtStatus.Foreground = _isPaused ? Brushes.Orange : Brushes.Gray;
////        }

////        private void BtnClear_Click(object sender, RoutedEventArgs e)
////        {
////            lock (_dataLock)
////            {
////                _historyData.Clear();
////                _viewStartIndex = 0;
////                _unwrapOffsetX = _unwrapOffsetY = _unwrapOffsetZ = 0;
////                _firstData = true;
////                _currentRawX = _currentRawY = _currentRawZ = 0;
////            }
////            DrawEmptyWaveform();
////            UpdateRealTimeDisplay();
////        }

////        private void SliderHistory_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _isDraggingSlider = true;
////        private void SliderHistory_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => _isDraggingSlider = false;
////        private void SliderHistory_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_isDraggingSlider) _viewStartIndex = (int)e.NewValue; }

////        private void CmbTimeScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
////        {
////            _timeScale = CmbTimeScale.SelectedIndex switch { 0 => 1, 1 => 2, 2 => 5, 3 => 10, _ => 1 };
////            if (!_isPaused && !_isDraggingSlider) lock (_dataLock) _viewStartIndex = Math.Max(0, _historyData.Count - DISPLAY_POINTS / _timeScale);
////        }

////        private void UpdateParentInfo() => TxtParentInfo.Text = $"所属串口: {ParentPortName} | 角度 0~360° | 已优化渲染";
////    }
////}

//using System;
//using System.Collections.Generic;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Media;
//using System.Windows.Shapes;
//using System.Windows.Threading;
//using System.Text.RegularExpressions;

//namespace MySerialPortAssistant04
//{
//    public partial class WaveformSubWindow : Window, ILogReceiver
//    {
//        private class DataPoint
//        {
//            public double RawX, RawY, RawZ;
//            public double UnwrappedX, UnwrappedY, UnwrappedZ;
//        }

//        private readonly List<DataPoint> _historyData = new List<DataPoint>();
//        private readonly object _dataLock = new object();

//        private const int SAMPLE_RATE_MS = 20;
//        private const int MAX_HISTORY_POINTS = 15000;
//        private const int DISPLAY_POINTS = 1000;
//        private const double WRAP_THRESHOLD = 180;

//        private bool _isPaused = false;
//        private bool _isDraggingSlider = false;
//        private int _viewStartIndex = 0;
//        private int _timeScale = 1;

//        private double _unwrapOffsetX = 0, _unwrapOffsetY = 0, _unwrapOffsetZ = 0;
//        private double _lastRawX = 0, _lastRawY = 0, _lastRawZ = 0;
//        private bool _firstData = true;

//        private DispatcherTimer? _renderTimer;
//        private DispatcherTimer? _resizeTimer;

//        private double _currentRawX = 0, _currentRawY = 0, _currentRawZ = 0;

//        private Polyline _lineX, _lineY, _lineZ;
//        private Line _crossVerticalLine, _crossHorizontalLine;

//        private double _lastMin = 0, _lastMax = 360;
//        private const double Y_AXIS_FOLLOW_SPEED = 0.15;

//        private Point _mousePoint;
//        private bool _isMouseOverChart = false;

//        public int ParentPortIndex { get; private set; }
//        public string ParentPortName => ParentPortIndex > 0 ? $"串口监控 #{ParentPortIndex}" : "未知";

//        public WaveformSubWindow(int parentPortIndex = 0)
//        {
//            ParentPortIndex = parentPortIndex;
//            InitializeComponent();
//            InitializeReusableLines();
//            InitializeCrossCursor();

//            Loaded += OnWindowLoaded;
//            Closed += OnWindowClosed;
//            SizeChanged += (s, e) => _resizeTimer?.Start();
//        }

//        private void InitializeReusableLines()
//        {
//            _lineX = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(234, 76, 76)), StrokeThickness = 1.5 };
//            _lineY = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(76, 201, 76)), StrokeThickness = 1.5 };
//            _lineZ = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(77, 158, 255)), StrokeThickness = 1.5 };
//        }

//        private void InitializeCrossCursor()
//        {
//            _crossVerticalLine = new Line { Stroke = Brushes.Cyan, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } };
//            _crossHorizontalLine = new Line { Stroke = Brushes.Cyan, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } };
//        }

//        private void OnWindowLoaded(object sender, RoutedEventArgs e)
//        {
//            UpdateParentInfo();

//            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
//            _renderTimer.Tick += OnRenderTick;
//            _renderTimer.Start();

//            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
//            _resizeTimer.Tick += (s, e) => { _resizeTimer.Stop(); DrawEmptyWaveform(); };

//            WaveCanvas.MouseMove += WaveCanvas_MouseMove;
//            WaveCanvas.MouseLeave += (s, e) => _isMouseOverChart = false;
//            WaveCanvas.MouseEnter += (s, e) => _isMouseOverChart = true;

//            ChkX.Checked += (s, e) => DrawWaveform();
//            ChkX.Unchecked += (s, e) => DrawWaveform();
//            ChkY.Checked += (s, e) => DrawWaveform();
//            ChkY.Unchecked += (s, e) => DrawWaveform();
//            ChkZ.Checked += (s, e) => DrawWaveform();
//            ChkZ.Unchecked += (s, e) => DrawWaveform();

//            DrawEmptyWaveform();
//        }

//        private void WaveCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
//        {
//            _mousePoint = e.GetPosition(WaveCanvas);
//        }

//        private void OnWindowClosed(object? sender, EventArgs e)
//        {
//            _renderTimer?.Stop();
//            _resizeTimer?.Stop();
//        }

//        private void DrawEmptyWaveform()
//        {
//            if (WaveCanvas == null) return;
//            WaveCanvas.Children.Clear();
//            YAxisCanvas.Children.Clear();
//            YAxisRightCanvas.Children.Clear();

//            WaveCanvas.Children.Add(_lineX);
//            WaveCanvas.Children.Add(_lineY);
//            WaveCanvas.Children.Add(_lineZ);
//            WaveCanvas.Children.Add(_crossVerticalLine);
//            WaveCanvas.Children.Add(_crossHorizontalLine);

//            double w = WaveBorder.ActualWidth > 0 ? WaveBorder.ActualWidth : 1000;
//            double h = WaveCanvas.ActualHeight > 0 ? WaveCanvas.ActualHeight : 400;
//            DrawGrid(w, h, 0, 360);
//            DrawYAxisLabels(0, 360);
//            DrawYAxisRightLabels(0, 360);
//        }

//        public void EnqueueLog(string log)
//        {
//            if (string.IsNullOrEmpty(log) || !log.Contains("Same_angle")) return;

//            try
//            {
//                double x = GetAngle(log, "X");
//                double y = GetAngle(log, "Y");
//                double z = GetAngle(log, "Z");

//                if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) return;

//                double ux, uy, uz;

//                if (_firstData)
//                {
//                    _lastRawX = x; _lastRawY = y; _lastRawZ = z;
//                    _firstData = false;
//                    ux = x; uy = y; uz = z;
//                }
//                else
//                {
//                    ux = Unwrap(x, ref _lastRawX, ref _unwrapOffsetX);
//                    uy = Unwrap(y, ref _lastRawY, ref _unwrapOffsetY);
//                    uz = Unwrap(z, ref _lastRawZ, ref _unwrapOffsetZ);
//                }

//                lock (_dataLock)
//                {
//                    _historyData.Add(new DataPoint
//                    {
//                        RawX = x,
//                        RawY = y,
//                        RawZ = z,
//                        UnwrappedX = ux,
//                        UnwrappedY = uy,
//                        UnwrappedZ = uz
//                    });

//                    _currentRawX = x;
//                    _currentRawY = y;
//                    _currentRawZ = z;

//                    if (_historyData.Count > MAX_HISTORY_POINTS)
//                        _historyData.RemoveAt(0);

//                    if (!_isPaused && !_isDraggingSlider)
//                    {
//                        int show = DISPLAY_POINTS / _timeScale;
//                        _viewStartIndex = Math.Max(0, _historyData.Count - show);
//                    }
//                }
//            }
//            catch { }
//        }

//        private readonly Regex _angleRegex = new Regex(@"([XYZ])[:=]\s*([-\d\.]+)", RegexOptions.Compiled);
//        private double GetAngle(string log, string axis)
//        {
//            var match = _angleRegex.Match(log);
//            while (match.Success)
//            {
//                if (match.Groups[1].Value == axis)
//                    return double.TryParse(match.Groups[2].Value, out double v) ? v : double.NaN;
//                match = match.NextMatch();
//            }
//            return double.NaN;
//        }

//        private static double Unwrap(double current, ref double last, ref double offset)
//        {
//            double d = current - last;
//            if (d > WRAP_THRESHOLD) offset -= 360;
//            else if (d < -WRAP_THRESHOLD) offset += 360;
//            last = current;
//            return current + offset;
//        }

//        private void OnRenderTick(object? sender, EventArgs e)
//        {
//            try { DrawWaveform(); UpdateRealTimeDisplay(); }
//            catch { }
//        }

//        private void UpdateRealTimeDisplay()
//        {
//            if (TxtRealTimeX == null) return;
//            lock (_dataLock)
//            {
//                TxtRealTimeX.Text = $"{_currentRawX:F0}°";
//                TxtRealTimeY.Text = $"{_currentRawY:F0}°";
//                TxtRealTimeZ.Text = $"{_currentRawZ:F0}°";
//            }
//        }

//        private void DrawWaveform()
//        {
//            if (WaveCanvas == null) return;
//            double w = WaveBorder.ActualWidth;
//            double h = WaveCanvas.ActualHeight;
//            if (w < 10 || h < 10) return;

//            DataPoint[] data;
//            lock (_dataLock)
//            {
//                int count = _historyData.Count;
//                if (count < 2) { DrawEmptyWaveform(); return; }
//                int show = DISPLAY_POINTS / _timeScale;
//                int end = Math.Min(_viewStartIndex + show, count);
//                int start = Math.Max(0, end - show);
//                data = _historyData.GetRange(start, end - start).ToArray();
//            }

//            double min = double.MaxValue, max = double.MinValue;
//            foreach (var p in data)
//            {
//                if (ChkX.IsChecked == true) { min = Math.Min(min, p.UnwrappedX); max = Math.Max(max, p.UnwrappedX); }
//                if (ChkY.IsChecked == true) { min = Math.Min(min, p.UnwrappedY); max = Math.Max(max, p.UnwrappedY); }
//                if (ChkZ.IsChecked == true) { min = Math.Min(min, p.UnwrappedZ); max = Math.Max(max, p.UnwrappedZ); }
//            }

//            if (min == double.MaxValue) { min = 0; max = 360; }
//            double targetMin = Math.Floor(min / 30) * 30 - 15;
//            double targetMax = targetMin + Math.Max(360, max - min) + 30;

//            _lastMin = _lastMin + (targetMin - _lastMin) * Y_AXIS_FOLLOW_SPEED;
//            _lastMax = _lastMax + (targetMax - _lastMax) * Y_AXIS_FOLLOW_SPEED;
//            double range = _lastMax - _lastMin;

//            WaveCanvas.Children.Clear();
//            WaveCanvas.Children.Add(_lineX);
//            WaveCanvas.Children.Add(_lineY);
//            WaveCanvas.Children.Add(_lineZ);
//            WaveCanvas.Children.Add(_crossVerticalLine);
//            WaveCanvas.Children.Add(_crossHorizontalLine);
//            YAxisCanvas.Children.Clear();
//            YAxisRightCanvas.Children.Clear();

//            DrawGrid(w, h, _lastMin, _lastMax);
//            DrawYAxisLabels(_lastMin, _lastMax);
//            DrawYAxisRightLabels(_lastMin, _lastMax);

//            double step = w / (data.Length - 1);

//            _lineX.Points = ChkX.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedX) : new PointCollection();
//            _lineY.Points = ChkY.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedY) : new PointCollection();
//            _lineZ.Points = ChkZ.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedZ) : new PointCollection();

//            if (_isPaused || _isDraggingSlider)
//            {
//                WaveCanvas.Children.Add(new Line
//                {
//                    X1 = w / 2,
//                    Y1 = 0,
//                    X2 = w / 2,
//                    Y2 = h,
//                    Stroke = Brushes.Yellow,
//                    StrokeThickness = 2,
//                    StrokeDashArray = new DoubleCollection { 5, 5 }
//                });
//            }

//            if (_isMouseOverChart)
//            {
//                _crossVerticalLine.X1 = _mousePoint.X; _crossVerticalLine.X2 = _mousePoint.X;
//                _crossVerticalLine.Y1 = 0; _crossVerticalLine.Y2 = h;
//                _crossHorizontalLine.Y1 = _mousePoint.Y; _crossHorizontalLine.Y2 = _mousePoint.Y;
//                _crossHorizontalLine.X1 = 0; _crossHorizontalLine.X2 = w;
//            }
//            else
//            {
//                _crossVerticalLine.Visibility = Visibility.Collapsed;
//                _crossHorizontalLine.Visibility = Visibility.Collapsed;
//            }

//            UpdateTimeInfo();
//            UpdateSlider();
//        }

//        private PointCollection GetPoints(DataPoint[] data, double step, double h, double min, double range, Func<DataPoint, double> sel)
//        {
//            var pc = new PointCollection(data.Length);
//            for (int i = 0; i < data.Length; i++)
//            {
//                double v = sel(data[i]);
//                double y = h - (v - min) / range * h;
//                pc.Add(new Point(i * step, y));
//            }
//            return pc;
//        }

//        private void DrawGrid(double w, double h, double min, double max)
//        {
//            double range = max - min;
//            double interval = range > 720 ? 30 : (range > 360 ? 15 : (range > 180 ? 10 : 5));

//            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
//            {
//                double y = h - (v - min) / range * h;
//                bool isMajorTick = (Math.Abs(v) % 90 < 0.1) || (Math.Abs(v) % 60 < 0.1);
//                WaveCanvas.Children.Add(new Line
//                {
//                    X1 = 0,
//                    Y1 = y,
//                    X2 = w,
//                    Y2 = y,
//                    Stroke = isMajorTick ? Brushes.LightGray : Brushes.Gray,
//                    StrokeThickness = isMajorTick ? 0.8 : 0.3,
//                    Opacity = isMajorTick ? 0.6 : 0.3
//                });
//            }

//            for (int i = 0; i <= 20; i++)
//            {
//                double x = w * i / 20;
//                WaveCanvas.Children.Add(new Line
//                {
//                    X1 = x,
//                    Y1 = 0,
//                    X2 = x,
//                    Y2 = h,
//                    Stroke = Brushes.DarkGray,
//                    StrokeThickness = 0.3,
//                    StrokeDashArray = new DoubleCollection { 2, 4 },
//                    Opacity = 0.4
//                });
//            }
//        }

//        private void DrawYAxisLabels(double min, double max)
//        {
//            double h = WaveCanvas.ActualHeight;
//            double range = max - min;
//            double interval = range > 720 ? 30 : (range > 360 ? 15 : (range > 180 ? 10 : 5));

//            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
//            {
//                double y = h - (v - min) / range * h;
//                bool showLabel = range > 360 ? (Math.Abs(v) % 30 < 0.1) : (Math.Abs(v) % 15 < 0.1);

//                if (showLabel)
//                {
//                    YAxisCanvas.Children.Add(new TextBlock
//                    {
//                        Text = $"{v:F0}°",
//                        Foreground = Brushes.White,
//                        FontSize = 9,
//                        Width = 45,
//                        TextAlignment = TextAlignment.Right
//                    });
//                    Canvas.SetTop(YAxisCanvas.Children[YAxisCanvas.Children.Count - 1], y - 6);
//                }

//                var tick = new Line
//                {
//                    X1 = 42,
//                    Y1 = y,
//                    X2 = 48,
//                    Y2 = y,
//                    Stroke = Brushes.Gray,
//                    StrokeThickness = 0.5
//                };
//                YAxisCanvas.Children.Add(tick);
//            }
//        }

//        private void DrawYAxisRightLabels(double min, double max)
//        {
//            double h = WaveCanvas.ActualHeight;
//            double range = max - min;
//            double interval = range > 720 ? 30 : (range > 360 ? 15 : (range > 180 ? 10 : 5));

//            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
//            {
//                double y = h - (v - min) / range * h;
//                bool showLabel = range > 360 ? (Math.Abs(v) % 30 < 0.1) : (Math.Abs(v) % 15 < 0.1);

//                if (showLabel)
//                {
//                    YAxisRightCanvas.Children.Add(new TextBlock
//                    {
//                        Text = $"{v:F0}°",
//                        Foreground = Brushes.White,
//                        FontSize = 9,
//                        Width = 45,
//                        TextAlignment = TextAlignment.Left
//                    });
//                    Canvas.SetTop(YAxisRightCanvas.Children[YAxisRightCanvas.Children.Count - 1], y - 6);
//                    Canvas.SetLeft(YAxisRightCanvas.Children[YAxisRightCanvas.Children.Count - 1], 0);
//                }

//                var tick = new Line
//                {
//                    X1 = 0,
//                    Y1 = y,
//                    X2 = 6,
//                    Y2 = y,
//                    Stroke = Brushes.Gray,
//                    StrokeThickness = 0.5
//                };
//                YAxisRightCanvas.Children.Add(tick);
//            }
//        }

//        private void UpdateTimeInfo()
//        {
//            lock (_dataLock)
//            {
//                var total = TimeSpan.FromMilliseconds(_historyData.Count * SAMPLE_RATE_MS);
//                var pos = TimeSpan.FromMilliseconds(_viewStartIndex * SAMPLE_RATE_MS);
//                TxtTimeInfo.Text = $"{pos.Minutes:D2}:{pos.Seconds:D2} / {total.Minutes:D2}:{total.Seconds:D2}";
//            }
//        }

//        private void UpdateSlider()
//        {
//            lock (_dataLock)
//            {
//                int max = Math.Max(0, _historyData.Count - DISPLAY_POINTS / _timeScale);
//                if (!_isDraggingSlider)
//                {
//                    SliderHistory.Maximum = max;
//                    SliderHistory.Value = _viewStartIndex;
//                }
//            }
//        }

//        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
//        {
//            _isPaused = !_isPaused;
//            BtnPlayPause.Content = _isPaused ? "▶ 继续" : "⏸ 暂停";
//            BtnPlayPause.Background = new SolidColorBrush(_isPaused ? Color.FromRgb(0xEA, 0x4C, 0x4C) : Color.FromRgb(0x4E, 0xC9, 0x4E));
//            TxtStatus.Text = _isPaused ? "已暂停 - 可拖动查看历史" : "实时模式";
//            TxtStatus.Foreground = _isPaused ? Brushes.Orange : Brushes.Gray;
//        }

//        private void BtnClear_Click(object sender, RoutedEventArgs e)
//        {
//            lock (_dataLock)
//            {
//                _historyData.Clear();
//                _viewStartIndex = 0;
//                _unwrapOffsetX = _unwrapOffsetY = _unwrapOffsetZ = 0;
//                _firstData = true;
//                _currentRawX = _currentRawY = _currentRawZ = 0;
//            }
//            DrawEmptyWaveform();
//            UpdateRealTimeDisplay();
//        }

//        private void SliderHistory_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _isDraggingSlider = true;
//        private void SliderHistory_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => _isDraggingSlider = false;
//        private void SliderHistory_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_isDraggingSlider) _viewStartIndex = (int)e.NewValue; }

//        private void CmbTimeScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
//        {
//            _timeScale = CmbTimeScale.SelectedIndex switch { 0 => 1, 1 => 2, 2 => 5, 3 => 10, _ => 1 };
//            if (!_isPaused && !_isDraggingSlider) lock (_dataLock) _viewStartIndex = Math.Max(0, _historyData.Count - DISPLAY_POINTS / _timeScale);
//        }

//        private void UpdateParentInfo() => TxtParentInfo.Text = $"所属串口: {ParentPortName} | 角度 0~360° | 已优化渲染";
//    }
//}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.RegularExpressions;

namespace MySerialPortAssistant04
{
    public enum WaveformMode
    {
        SameAngle,   // 原始模式：Same_angle X:125 Y:9 Z:97
        FifoXyz      // 新增模式：FIFO xyz = 1010 14 171
    }

    public partial class WaveformSubWindow : Window, ILogReceiver
    {
        private class DataPoint
        {
            public double RawX, RawY, RawZ;
            public double UnwrappedX, UnwrappedY, UnwrappedZ;
        }

        private readonly List<DataPoint> _historyData = new List<DataPoint>();
        private readonly object _dataLock = new object();

        private const int SAMPLE_RATE_MS = 20;
        private const int MAX_HISTORY_POINTS = 15000;
        private const int DISPLAY_POINTS = 1000;
        private const double WRAP_THRESHOLD = 180;

        private bool _isPaused = false;
        private bool _isDraggingSlider = false;
        private int _viewStartIndex = 0;
        private int _timeScale = 1;

        private double _unwrapOffsetX = 0, _unwrapOffsetY = 0, _unwrapOffsetZ = 0;
        private double _lastRawX = 0, _lastRawY = 0, _lastRawZ = 0;
        private bool _firstData = true;

        private DispatcherTimer? _renderTimer;
        private DispatcherTimer? _resizeTimer;

        private double _currentRawX = 0, _currentRawY = 0, _currentRawZ = 0;

        private Polyline _lineX, _lineY, _lineZ;
        private Line _crossVerticalLine, _crossHorizontalLine;

        private double _lastMin = 0, _lastMax = 360;
        private const double Y_AXIS_FOLLOW_SPEED = 0.15;

        private Point _mousePoint;
        private bool _isMouseOverChart = false;

        // ===== 新增：模式相关 =====
        private WaveformMode _currentMode = WaveformMode.SameAngle;

        // Same_angle 正则
        private readonly Regex _angleRegex = new Regex(@"([XYZ])[:=]\s*([-\d\.]+)", RegexOptions.Compiled);
        // FIFO xyz 正则：匹配 "xyz = 1010 14 171" 或 "xyz = 1010, 14, 171" 等变体
        private readonly Regex _fifoRegex = new Regex(
            @"FIFO\s+xyz\s*=\s*([-\d\.]+)[\s,]+([-\d\.]+)[\s,]+([-\d\.]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public int ParentPortIndex { get; private set; }
        public string ParentPortName => ParentPortIndex > 0 ? $"串口监控 #{ParentPortIndex}" : "未知";

        public WaveformSubWindow(int parentPortIndex = 0)
        {
            ParentPortIndex = parentPortIndex;
            InitializeComponent();
            InitializeReusableLines();
            InitializeCrossCursor();

            Loaded += OnWindowLoaded;
            Closed += OnWindowClosed;
            SizeChanged += (s, e) => _resizeTimer?.Start();
        }

        private void InitializeReusableLines()
        {
            _lineX = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(234, 76, 76)), StrokeThickness = 1.5 };
            _lineY = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(76, 201, 76)), StrokeThickness = 1.5 };
            _lineZ = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(77, 158, 255)), StrokeThickness = 1.5 };
        }

        private void InitializeCrossCursor()
        {
            _crossVerticalLine = new Line { Stroke = Brushes.Cyan, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } };
            _crossHorizontalLine = new Line { Stroke = Brushes.Cyan, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } };
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            UpdateParentInfo();

            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _renderTimer.Tick += OnRenderTick;
            _renderTimer.Start();

            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _resizeTimer.Tick += (s, e) => { _resizeTimer.Stop(); DrawEmptyWaveform(); };

            WaveCanvas.MouseMove += WaveCanvas_MouseMove;
            WaveCanvas.MouseLeave += (s, e) => _isMouseOverChart = false;
            WaveCanvas.MouseEnter += (s, e) => _isMouseOverChart = true;

            ChkX.Checked += (s, e) => DrawWaveform();
            ChkX.Unchecked += (s, e) => DrawWaveform();
            ChkY.Checked += (s, e) => DrawWaveform();
            ChkY.Unchecked += (s, e) => DrawWaveform();
            ChkZ.Checked += (s, e) => DrawWaveform();
            ChkZ.Unchecked += (s, e) => DrawWaveform();

            // 初始化模式UI
            UpdateModeUI();

            DrawEmptyWaveform();
        }

        private void WaveCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _mousePoint = e.GetPosition(WaveCanvas);
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _renderTimer?.Stop();
            _resizeTimer?.Stop();
        }

        private void DrawEmptyWaveform()
        {
            if (WaveCanvas == null) return;
            WaveCanvas.Children.Clear();
            YAxisCanvas.Children.Clear();
            YAxisRightCanvas.Children.Clear();

            WaveCanvas.Children.Add(_lineX);
            WaveCanvas.Children.Add(_lineY);
            WaveCanvas.Children.Add(_lineZ);
            WaveCanvas.Children.Add(_crossVerticalLine);
            WaveCanvas.Children.Add(_crossHorizontalLine);

            double w = WaveBorder.ActualWidth > 0 ? WaveBorder.ActualWidth : 1000;
            double h = WaveCanvas.ActualHeight > 0 ? WaveCanvas.ActualHeight : 400;

            // 根据模式决定空波形范围
            double emptyMin = _currentMode == WaveformMode.SameAngle ? 0 : -32768;
            double emptyMax = _currentMode == WaveformMode.SameAngle ? 360 : 32767;

            DrawGrid(w, h, emptyMin, emptyMax);
            DrawYAxisLabels(emptyMin, emptyMax);
            DrawYAxisRightLabels(emptyMin, emptyMax);
        }

        // ===== 新增：模式切换处理 =====
        private void CmbMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentMode = CmbMode.SelectedIndex == 0 ? WaveformMode.SameAngle : WaveformMode.FifoXyz;

            // 清空历史数据，切换模式后重新开始
            lock (_dataLock)
            {
                _historyData.Clear();
                _viewStartIndex = 0;
                _unwrapOffsetX = _unwrapOffsetY = _unwrapOffsetZ = 0;
                _firstData = true;
                _currentRawX = _currentRawY = _currentRawZ = 0;
            }

            UpdateModeUI();
            DrawEmptyWaveform();
            UpdateRealTimeDisplay();
            UpdateParentInfo();
        }

        private void UpdateModeUI()
        {
            if (TxtLabelX == null) return;

            if (_currentMode == WaveformMode.SameAngle)
            {
                TxtLabelX.Text = "X 轴 (红色)";
                TxtLabelY.Text = "Y 轴 (绿色)";
                TxtLabelZ.Text = "Z 轴 (蓝色)";
                TxtHint.Text = "显示原始角度值\n(0-360°范围)";
            }
            else
            {
                TxtLabelX.Text = "X (红色)";
                TxtLabelY.Text = "Y (绿色)";
                TxtLabelZ.Text = "Z (蓝色)";
                TxtHint.Text = "显示 FIFO xyz 原始值\n(IMU 传感器数据)";
            }
        }

        public void EnqueueLog(string log)
        {
            if (string.IsNullOrEmpty(log)) return;

            try
            {
                double x, y, z;
                bool parsed = false;

                if (_currentMode == WaveformMode.SameAngle)
                {
                    // Same_angle 模式
                    if (!log.Contains("Same_angle")) return;

                    x = GetAngleFromSameAngle(log, "X");
                    y = GetAngleFromSameAngle(log, "Y");
                    z = GetAngleFromSameAngle(log, "Z");

                    if (!double.IsNaN(x) && !double.IsNaN(y) && !double.IsNaN(z))
                        parsed = true;
                }
                else
                {
                    // FIFO xyz 模式
                    if (!log.Contains("qma_imu") || !log.Contains("FIFO")) return;

                    var match = _fifoRegex.Match(log);
                    if (match.Success)
                    {
                        x = double.Parse(match.Groups[1].Value);
                        y = double.Parse(match.Groups[2].Value);
                        z = double.Parse(match.Groups[3].Value);
                        parsed = true;
                    }
                    else
                    {
                        return;
                    }
                }

                if (!parsed) return;

                double ux, uy, uz;

                if (_firstData)
                {
                    _lastRawX = x; _lastRawY = y; _lastRawZ = z;
                    _firstData = false;
                    ux = x; uy = y; uz = z;
                }
                else
                {
                    ux = Unwrap(x, ref _lastRawX, ref _unwrapOffsetX);
                    uy = Unwrap(y, ref _lastRawY, ref _unwrapOffsetY);
                    uz = Unwrap(z, ref _lastRawZ, ref _unwrapOffsetZ);
                }

                lock (_dataLock)
                {
                    _historyData.Add(new DataPoint
                    {
                        RawX = x,
                        RawY = y,
                        RawZ = z,
                        UnwrappedX = ux,
                        UnwrappedY = uy,
                        UnwrappedZ = uz
                    });

                    _currentRawX = x;
                    _currentRawY = y;
                    _currentRawZ = z;

                    if (_historyData.Count > MAX_HISTORY_POINTS)
                        _historyData.RemoveAt(0);

                    if (!_isPaused && !_isDraggingSlider)
                    {
                        int show = DISPLAY_POINTS / _timeScale;
                        _viewStartIndex = Math.Max(0, _historyData.Count - show);
                    }
                }
            }
            catch { }
        }

        private double GetAngleFromSameAngle(string log, string axis)
        {
            var match = _angleRegex.Match(log);
            while (match.Success)
            {
                if (match.Groups[1].Value == axis)
                    return double.TryParse(match.Groups[2].Value, out double v) ? v : double.NaN;
                match = match.NextMatch();
            }
            return double.NaN;
        }

        private static double Unwrap(double current, ref double last, ref double offset)
        {
            double d = current - last;
            if (d > WRAP_THRESHOLD) offset -= 360;
            else if (d < -WRAP_THRESHOLD) offset += 360;
            last = current;
            return current + offset;
        }

        private void OnRenderTick(object? sender, EventArgs e)
        {
            try { DrawWaveform(); UpdateRealTimeDisplay(); }
            catch { }
        }

        private void UpdateRealTimeDisplay()
        {
            if (TxtRealTimeX == null) return;
            lock (_dataLock)
            {
                if (_currentMode == WaveformMode.SameAngle)
                {
                    TxtRealTimeX.Text = $"{_currentRawX:F0}°";
                    TxtRealTimeY.Text = $"{_currentRawY:F0}°";
                    TxtRealTimeZ.Text = $"{_currentRawZ:F0}°";
                }
                else
                {
                    TxtRealTimeX.Text = $"{_currentRawX:F0}";
                    TxtRealTimeY.Text = $"{_currentRawY:F0}";
                    TxtRealTimeZ.Text = $"{_currentRawZ:F0}";
                }
            }
        }

        private void DrawWaveform()
        {
            if (WaveCanvas == null) return;
            double w = WaveBorder.ActualWidth;
            double h = WaveCanvas.ActualHeight;
            if (w < 10 || h < 10) return;

            DataPoint[] data;
            lock (_dataLock)
            {
                int count = _historyData.Count;
                if (count < 2) { DrawEmptyWaveform(); return; }
                int show = DISPLAY_POINTS / _timeScale;
                int end = Math.Min(_viewStartIndex + show, count);
                int start = Math.Max(0, end - show);
                data = _historyData.GetRange(start, end - start).ToArray();
            }

            double min = double.MaxValue, max = double.MinValue;
            foreach (var p in data)
            {
                if (ChkX.IsChecked == true) { min = Math.Min(min, p.UnwrappedX); max = Math.Max(max, p.UnwrappedX); }
                if (ChkY.IsChecked == true) { min = Math.Min(min, p.UnwrappedY); max = Math.Max(max, p.UnwrappedY); }
                if (ChkZ.IsChecked == true) { min = Math.Min(min, p.UnwrappedZ); max = Math.Max(max, p.UnwrappedZ); }
            }

            if (min == double.MaxValue)
            {
                min = _currentMode == WaveformMode.SameAngle ? 0 : -1000;
                max = _currentMode == WaveformMode.SameAngle ? 360 : 1000;
            }

            double targetMin, targetMax;
            if (_currentMode == WaveformMode.SameAngle)
            {
                targetMin = Math.Floor(min / 30) * 30 - 15;
                targetMax = targetMin + Math.Max(360, max - min) + 30;
            }
            else
            {
                // FIFO xyz 模式：动态范围，使用更合适的刻度
                double dataRange = max - min;
                double padding = dataRange * 0.1;
                targetMin = min - padding;
                targetMax = max + padding;
                if (dataRange < 100) { targetMin -= 50; targetMax += 50; }
            }

            _lastMin = _lastMin + (targetMin - _lastMin) * Y_AXIS_FOLLOW_SPEED;
            _lastMax = _lastMax + (targetMax - _lastMax) * Y_AXIS_FOLLOW_SPEED;
            double range = _lastMax - _lastMin;

            WaveCanvas.Children.Clear();
            WaveCanvas.Children.Add(_lineX);
            WaveCanvas.Children.Add(_lineY);
            WaveCanvas.Children.Add(_lineZ);
            WaveCanvas.Children.Add(_crossVerticalLine);
            WaveCanvas.Children.Add(_crossHorizontalLine);
            YAxisCanvas.Children.Clear();
            YAxisRightCanvas.Children.Clear();

            DrawGrid(w, h, _lastMin, _lastMax);
            DrawYAxisLabels(_lastMin, _lastMax);
            DrawYAxisRightLabels(_lastMin, _lastMax);

            double step = w / (data.Length - 1);

            _lineX.Points = ChkX.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedX) : new PointCollection();
            _lineY.Points = ChkY.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedY) : new PointCollection();
            _lineZ.Points = ChkZ.IsChecked == true ? GetPoints(data, step, h, _lastMin, range, p => p.UnwrappedZ) : new PointCollection();

            if (_isPaused || _isDraggingSlider)
            {
                WaveCanvas.Children.Add(new Line
                {
                    X1 = w / 2,
                    Y1 = 0,
                    X2 = w / 2,
                    Y2 = h,
                    Stroke = Brushes.Yellow,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 5, 5 }
                });
            }

            if (_isMouseOverChart)
            {
                _crossVerticalLine.X1 = _mousePoint.X; _crossVerticalLine.X2 = _mousePoint.X;
                _crossVerticalLine.Y1 = 0; _crossVerticalLine.Y2 = h;
                _crossHorizontalLine.Y1 = _mousePoint.Y; _crossHorizontalLine.Y2 = _mousePoint.Y;
                _crossHorizontalLine.X1 = 0; _crossHorizontalLine.X2 = w;
            }
            else
            {
                _crossVerticalLine.Visibility = Visibility.Collapsed;
                _crossHorizontalLine.Visibility = Visibility.Collapsed;
            }

            UpdateTimeInfo();
            UpdateSlider();
        }

        private PointCollection GetPoints(DataPoint[] data, double step, double h, double min, double range, Func<DataPoint, double> sel)
        {
            var pc = new PointCollection(data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                double v = sel(data[i]);
                double y = h - (v - min) / range * h;
                pc.Add(new Point(i * step, y));
            }
            return pc;
        }

        private void DrawGrid(double w, double h, double min, double max)
        {
            double range = max - min;
            double interval;

            if (_currentMode == WaveformMode.SameAngle)
            {
                interval = range > 720 ? 30 : (range > 360 ? 15 : (range > 180 ? 10 : 5));
            }
            else
            {
                // FIFO xyz 模式：自适应刻度间隔
                if (range > 10000) interval = 2000;
                else if (range > 5000) interval = 1000;
                else if (range > 2000) interval = 500;
                else if (range > 1000) interval = 200;
                else if (range > 500) interval = 100;
                else if (range > 200) interval = 50;
                else if (range > 100) interval = 20;
                else interval = 10;
            }

            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
            {
                double y = h - (v - min) / range * h;
                bool isMajorTick = (Math.Abs(v) % (interval * 2) < 0.1);
                WaveCanvas.Children.Add(new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = w,
                    Y2 = y,
                    Stroke = isMajorTick ? Brushes.LightGray : Brushes.Gray,
                    StrokeThickness = isMajorTick ? 0.8 : 0.3,
                    Opacity = isMajorTick ? 0.6 : 0.3
                });
            }

            for (int i = 0; i <= 20; i++)
            {
                double x = w * i / 20;
                WaveCanvas.Children.Add(new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = h,
                    Stroke = Brushes.DarkGray,
                    StrokeThickness = 0.3,
                    StrokeDashArray = new DoubleCollection { 2, 4 },
                    Opacity = 0.4
                });
            }
        }

        private void DrawYAxisLabels(double min, double max)
        {
            double h = WaveCanvas.ActualHeight;
            double range = max - min;
            double interval;

            if (_currentMode == WaveformMode.SameAngle)
            {
                interval = range > 720 ? 30 : (range > 360 ? 15 : (range > 180 ? 10 : 5));
            }
            else
            {
                if (range > 10000) interval = 2000;
                else if (range > 5000) interval = 1000;
                else if (range > 2000) interval = 500;
                else if (range > 1000) interval = 200;
                else if (range > 500) interval = 100;
                else if (range > 200) interval = 50;
                else if (range > 100) interval = 20;
                else interval = 10;
            }

            double labelInterval = interval * 2; // 标签间隔是刻度间隔的2倍，避免拥挤

            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
            {
                double y = h - (v - min) / range * h;

                bool showLabel = Math.Abs(v - Math.Round(v / labelInterval) * labelInterval) < 0.1;

                if (showLabel)
                {
                    YAxisCanvas.Children.Add(new TextBlock
                    {
                        Text = _currentMode == WaveformMode.SameAngle ? $"{v:F0}°" : $"{v:F0}",
                        Foreground = Brushes.White,
                        FontSize = 9,
                        Width = 45,
                        TextAlignment = TextAlignment.Right
                    });
                    Canvas.SetTop(YAxisCanvas.Children[YAxisCanvas.Children.Count - 1], y - 6);
                }

                var tick = new Line
                {
                    X1 = 42,
                    Y1 = y,
                    X2 = 48,
                    Y2 = y,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5
                };
                YAxisCanvas.Children.Add(tick);
            }
        }

        private void DrawYAxisRightLabels(double min, double max)
        {
            double h = WaveCanvas.ActualHeight;
            double range = max - min;
            double interval;

            if (_currentMode == WaveformMode.SameAngle)
            {
                interval = range > 720 ? 30 : (range > 360 ? 15 : (range > 180 ? 10 : 5));
            }
            else
            {
                if (range > 10000) interval = 2000;
                else if (range > 5000) interval = 1000;
                else if (range > 2000) interval = 500;
                else if (range > 1000) interval = 200;
                else if (range > 500) interval = 100;
                else if (range > 200) interval = 50;
                else if (range > 100) interval = 20;
                else interval = 10;
            }

            double labelInterval = interval * 2;

            for (double v = Math.Ceiling(min / interval) * interval; v <= max; v += interval)
            {
                double y = h - (v - min) / range * h;
                bool showLabel = Math.Abs(v - Math.Round(v / labelInterval) * labelInterval) < 0.1;

                if (showLabel)
                {
                    YAxisRightCanvas.Children.Add(new TextBlock
                    {
                        Text = _currentMode == WaveformMode.SameAngle ? $"{v:F0}°" : $"{v:F0}",
                        Foreground = Brushes.White,
                        FontSize = 9,
                        Width = 45,
                        TextAlignment = TextAlignment.Left
                    });
                    Canvas.SetTop(YAxisRightCanvas.Children[YAxisRightCanvas.Children.Count - 1], y - 6);
                    Canvas.SetLeft(YAxisRightCanvas.Children[YAxisRightCanvas.Children.Count - 1], 0);
                }

                var tick = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = 6,
                    Y2 = y,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5
                };
                YAxisRightCanvas.Children.Add(tick);
            }
        }

        private void UpdateTimeInfo()
        {
            lock (_dataLock)
            {
                var total = TimeSpan.FromMilliseconds(_historyData.Count * SAMPLE_RATE_MS);
                var pos = TimeSpan.FromMilliseconds(_viewStartIndex * SAMPLE_RATE_MS);
                TxtTimeInfo.Text = $"{pos.Minutes:D2}:{pos.Seconds:D2} / {total.Minutes:D2}:{total.Seconds:D2}";
            }
        }

        private void UpdateSlider()
        {
            lock (_dataLock)
            {
                int max = Math.Max(0, _historyData.Count - DISPLAY_POINTS / _timeScale);
                if (!_isDraggingSlider)
                {
                    SliderHistory.Maximum = max;
                    SliderHistory.Value = _viewStartIndex;
                }
            }
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            BtnPlayPause.Content = _isPaused ? "▶ 继续" : "⏸ 暂停";
            BtnPlayPause.Background = new SolidColorBrush(_isPaused ? Color.FromRgb(0xEA, 0x4C, 0x4C) : Color.FromRgb(0x4E, 0xC9, 0x4E));
            TxtStatus.Text = _isPaused ? "已暂停 - 可拖动查看历史" : "实时模式";
            TxtStatus.Foreground = _isPaused ? Brushes.Orange : Brushes.Gray;
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            lock (_dataLock)
            {
                _historyData.Clear();
                _viewStartIndex = 0;
                _unwrapOffsetX = _unwrapOffsetY = _unwrapOffsetZ = 0;
                _firstData = true;
                _currentRawX = _currentRawY = _currentRawZ = 0;
            }
            DrawEmptyWaveform();
            UpdateRealTimeDisplay();
        }

        private void SliderHistory_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _isDraggingSlider = true;
        private void SliderHistory_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => _isDraggingSlider = false;
        private void SliderHistory_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_isDraggingSlider) _viewStartIndex = (int)e.NewValue; }

        private void CmbTimeScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _timeScale = CmbTimeScale.SelectedIndex switch { 0 => 1, 1 => 2, 2 => 5, 3 => 10, _ => 1 };
            if (!_isPaused && !_isDraggingSlider) lock (_dataLock) _viewStartIndex = Math.Max(0, _historyData.Count - DISPLAY_POINTS / _timeScale);
        }

        private void UpdateParentInfo()
        {
            string modeStr = _currentMode == WaveformMode.SameAngle ? "角度模式" : "FIFO xyz";
            TxtParentInfo.Text = $"所属串口: {ParentPortName} | {modeStr} | 已优化渲染";
        }
    }
}