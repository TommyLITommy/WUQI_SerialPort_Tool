using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MySerialPortAssistant04
{
    public partial class MagnetometerSubWindow : Window, ILogReceiver
    {
        #region 核心变量
        private double northReference = 0.0;
        private const double AngleSpeed = 0.5;
        private DispatcherTimer timer;

        // FPS 计算
        private DateTime lastTime = DateTime.Now;
        private int frameCount = 0;
        private double fps = 0.0;

        // 串口/数据队列
        private readonly ConcurrentQueue<double> _angleQueue = new ConcurrentQueue<double>();
        private int _testAngle = 0;

        // 演示模式标志
        private bool _isDemoMode = false;

        // 显示模式：false=方案A(指针固定，刻度盘旋转)，true=方案B(刻度盘固定，指针旋转)
        private bool _isModeB = false;
        #endregion

        // 父窗口信息
        public int ParentPortIndex { get; private set; } = 0;
        public string ParentPortName => ParentPortIndex > 0 ? $"串口监控 #{ParentPortIndex}" : "未知";

        public MagnetometerSubWindow(int parentPortIndex = 0)
        {
            InitializeComponent();

            // 设置父窗口信息
            ParentPortIndex = parentPortIndex;
            UpdateParentInfo();

            this.Focus();

            // 初始化绘制
            DrawCompass(northReference);

            // 50Hz 刷新定时器
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(20)
            };
            timer.Tick += Timer_Tick;
            timer.Start();

            // 初始化按钮状态
            UpdateDemoButtonState();
        }

        /// <summary>
        /// 更新父窗口信息显示
        /// </summary>
        private void UpdateParentInfo()
        {
            if (TxtParentInfo != null)
            {
                TxtParentInfo.Text = $"所属串口: #{ParentPortIndex}";
            }
            // 同时更新窗口标题
            this.Title = $"磁力计指南针 - {ParentPortName}";
        }

        /// <summary>
        /// 定时器主循环
        /// </summary>
        private void Timer_Tick(object sender, EventArgs e)
        {
            // 从队列获取最新角度
            bool hasNewData = false;
            while (_angleQueue.TryDequeue(out var a))
            {
                northReference = a % 360;
                hasNewData = true;
            }

            // 演示模式下自动旋转
            if (_isDemoMode)
            {
                northReference = (northReference + AngleSpeed) % 360;
            }

            DrawCompass(northReference);
            CalculateFPS();
        }

        /// <summary>
        /// 绘制指南针
        /// 方案A：指针固定向上，刻度盘旋转
        /// 方案B：刻度盘固定，指针旋转
        /// </summary>
        private void DrawCompass(double angle)
        {
            CompassCanvas.Children.Clear();
            double centerX = CompassCanvas.Width / 2;
            double centerY = CompassCanvas.Height / 2;
            double radius = 140;

            if (_isModeB)
            {
                // 方案B：刻度盘固定，指针旋转
                DrawFixedScale(centerX, centerY, radius);
                DrawRotatingNeedle(centerX, centerY, radius, angle);
            }
            else
            {
                // 方案A：指针固定向上，刻度盘旋转
                DrawFixedNeedle(centerX, centerY, radius);
                DrawRotatingScale(centerX, centerY, radius, angle);
            }
        }

        /// <summary>
        /// 方案A：绘制固定指针（指向正上方）
        /// </summary>
        private void DrawFixedNeedle(double centerX, double centerY, double radius)
        {
            // 固定红色指针（指向正上方/北）
            var needle = new Line
            {
                X1 = centerX,
                Y1 = centerY + 15,
                X2 = centerX,
                Y2 = centerY - 133,
                Stroke = Brushes.Red,
                StrokeThickness = 3
            };
            CompassCanvas.Children.Add(needle);

            // 指针中心圆点
            var centerDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.Red
            };
            Canvas.SetLeft(centerDot, centerX - 5);
            Canvas.SetTop(centerDot, centerY - 5);
            CompassCanvas.Children.Add(centerDot);
        }

        /// <summary>
        /// 方案A：绘制旋转刻度盘 (最终修复版)
        /// 指针固定向上，刻度盘顺时针旋转，让当前角度对准指针
        /// </summary>
        private void DrawRotatingScale(double centerX, double centerY, double radius, double rotationAngle)
        {
            // 主刻度：30° 间隔
            for (int deg = 0; deg < 360; deg += 30)
            {
                // ✅ 核心修复：+ rotationAngle → 刻度盘顺时针旋转
                double wpfAngle = 90 - deg + rotationAngle;
                double rad = wpfAngle * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);

                // 刻度线
                var line = new Line
                {
                    X1 = centerX + 0.85 * radius * cos,
                    Y1 = centerY - 0.85 * radius * sin,
                    X2 = centerX + radius * cos,
                    Y2 = centerY - radius * sin,
                    Stroke = Brushes.Black,
                    StrokeThickness = 3
                };
                CompassCanvas.Children.Add(line);

                // 刻度数字
                var text = new TextBlock
                {
                    Text = deg.ToString(),
                    Foreground = Brushes.Black,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Canvas.SetLeft(text, centerX + 1.12 * radius * cos - 10);
                Canvas.SetTop(text, centerY - 1.12 * radius * sin - 8);
                CompassCanvas.Children.Add(text);
            }

            // 次刻度：10° 间隔
            for (int deg = 10; deg < 360; deg += 10)
            {
                if (deg % 30 == 0) continue;

                // ✅ 同步修改：+ rotationAngle
                double wpfAngle = 90 - deg + rotationAngle;
                double rad = wpfAngle * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);

                var line = new Line
                {
                    X1 = centerX + 0.92 * radius * cos,
                    Y1 = centerY - 0.92 * radius * sin,
                    X2 = centerX + radius * cos,
                    Y2 = centerY - radius * sin,
                    Stroke = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    StrokeThickness = 1
                };
                CompassCanvas.Children.Add(line);
            }
        }

        /// <summary>
        /// 方案B：绘制固定刻度盘
        /// </summary>
        private void DrawFixedScale(double centerX, double centerY, double radius)
        {
            // 主刻度：30° 间隔
            for (int deg = 0; deg < 360; deg += 30)
            {
                // 刻度在WPF坐标系中的角度：指南针0°=上方=WPF 90°
                double wpfAngle = 90 - deg;
                double rad = wpfAngle * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);

                // 刻度线
                var line = new Line
                {
                    X1 = centerX + 0.85 * radius * cos,
                    Y1 = centerY - 0.85 * radius * sin,
                    X2 = centerX + radius * cos,
                    Y2 = centerY - radius * sin,
                    Stroke = Brushes.Black,
                    StrokeThickness = 3
                };
                CompassCanvas.Children.Add(line);

                // 刻度数字
                var text = new TextBlock
                {
                    Text = deg.ToString(),
                    Foreground = Brushes.Black,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Canvas.SetLeft(text, centerX + 1.12 * radius * cos - 10);
                Canvas.SetTop(text, centerY - 1.12 * radius * sin - 8);
                CompassCanvas.Children.Add(text);
            }

            // 次刻度：10° 间隔
            for (int deg = 10; deg < 360; deg += 10)
            {
                if (deg % 30 == 0) continue;

                double wpfAngle = 90 - deg;
                double rad = wpfAngle * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);

                var line = new Line
                {
                    X1 = centerX + 0.92 * radius * cos,
                    Y1 = centerY - 0.92 * radius * sin,
                    X2 = centerX + radius * cos,
                    Y2 = centerY - radius * sin,
                    Stroke = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    StrokeThickness = 1
                };
                CompassCanvas.Children.Add(line);
            }
        }

        /// <summary>
        /// 方案B：绘制旋转指针
        /// </summary>
        private void DrawRotatingNeedle(double centerX, double centerY, double radius, double angle)
        {
            double wpfAngle = 90 - angle;
            double rad = wpfAngle * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            // 旋转的红色指针，从中心向外指
            var needle = new Line
            {
                X1 = centerX,
                Y1 = centerY,
                X2 = centerX + 133 * cos,
                Y2 = centerY - 133 * sin,
                Stroke = Brushes.Red,
                StrokeThickness = 3
            };
            CompassCanvas.Children.Add(needle);

            // 指针尾部（短反向线）
            var tail = new Line
            {
                X1 = centerX,
                Y1 = centerY,
                X2 = centerX - 15 * cos,
                Y2 = centerY + 15 * sin,
                Stroke = Brushes.Red,
                StrokeThickness = 2
            };
            CompassCanvas.Children.Add(tail);

            // 指针中心圆点
            var centerDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.Red
            };
            Canvas.SetLeft(centerDot, centerX - 5);
            Canvas.SetTop(centerDot, centerY - 5);
            CompassCanvas.Children.Add(centerDot);
        }

        #region 功能按钮
        /// <summary>
        /// 暂停 / 继续
        /// </summary>
        private void ToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (timer.IsEnabled)
            {
                timer.Stop();
                ToggleBtn.Content = "继续";
            }
            else
            {
                timer.Start();
                ToggleBtn.Content = "暂停";
            }
        }

        /// <summary>
        /// 重置角度为 0
        /// </summary>
        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            northReference = 0.0;
            DrawCompass(northReference);
            StatusLabel.Text = $"角度: 0.0° | FPS: -- | 模式: {GetModeText()}";
        }

        /// <summary>
        /// 演示模式切换
        /// </summary>
        private void DemoBtn_Click(object sender, RoutedEventArgs e)
        {
            _isDemoMode = !_isDemoMode;
            UpdateDemoButtonState();
        }

        /// <summary>
        /// 显示模式切换
        /// </summary>
        private void ModeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _isModeB = ModeCheckBox.IsChecked == true;
            DrawCompass(northReference);
            UpdateStatusLabel();
        }

        /// <summary>
        /// 更新演示按钮状态显示
        /// </summary>
        private void UpdateDemoButtonState()
        {
            if (DemoBtn != null)
            {
                DemoBtn.Content = _isDemoMode ? "演示: 开" : "演示: 关";
                DemoBtn.Background = _isDemoMode
                    ? new SolidColorBrush(Color.FromRgb(76, 175, 80))  // 绿色
                    : new SolidColorBrush(Color.FromRgb(158, 158, 158)); // 灰色
            }
        }

        /// <summary>
        /// 获取当前模式文本
        /// </summary>
        private string GetModeText()
        {
            return _isModeB ? "方案B" : "方案A";
        }

        /// <summary>
        /// 更新状态标签
        /// </summary>
        private void UpdateStatusLabel()
        {
            if (StatusLabel != null)
            {
                string modeText = _isDemoMode ? " [演示]" : " [实时]";
                StatusLabel.Text = $"角度: {northReference:F1}° | FPS: {fps:F1}{modeText} | 模式: {GetModeText()}";
            }
        }
        #endregion

        #region 键盘控制（左右方向键）
        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            double step = 5.0;
            if (e.Key == System.Windows.Input.Key.Left)
            {
                northReference = (northReference - step + 360) % 360;
                DrawCompass(northReference);
            }
            else if (e.Key == System.Windows.Input.Key.Right)
            {
                northReference = (northReference + step) % 360;
                DrawCompass(northReference);
            }
        }
        #endregion

        #region FPS 计算与状态显示
        private void CalculateFPS()
        {
            frameCount++;
            var now = DateTime.Now;
            if ((now - lastTime).TotalSeconds >= 1.0)
            {
                fps = frameCount / (now - lastTime).TotalSeconds;
                frameCount = 0;
                lastTime = now;
                UpdateStatusLabel();
            }
        }
        #endregion

        #region 日志接收
        public void EnqueueLog(string log)
        {
            // 解析 "app_spatial] spatial_handle_euler_angle Same_angle X:125 Y:9 Z:97" 格式的日志
            var m = System.Text.RegularExpressions.Regex.Match(log, @"Same_angle.*X[:\s]*(-?\d+\.?\d*)");
            if (m.Success && double.TryParse(m.Groups[1].Value, out double a))
            {
                _angleQueue.Enqueue(a);
            }
        }
        #endregion

        protected override void OnClosed(EventArgs e)
        {
            timer.Stop();
            base.OnClosed(e);
        }
    }
}