//// Attitude3DWindow.xaml.cs - 轴向修正完整版
//using System;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Input;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;
//using System.Windows.Threading;
//using System.Text.RegularExpressions;

//namespace MySerialPortAssistant04
//{
//    public partial class Attitude3DWindow : Window, ILogReceiver
//    {
//        private struct V3
//        {
//            public float X, Y, Z;
//            public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
//            public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
//            public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
//            public static V3 operator *(V3 v, float s) => new V3(v.X * s, v.Y * s, v.Z * s);
//            public float Len => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
//            public V3 Norm()
//            {
//                float len = Len;
//                return len < 0.0001f ? new V3(0, 0, 0) : new V3(X / len, Y / len, Z / len);
//            }
//        }

//        private WriteableBitmap? _bmp;
//        private int _w, _h;
//        private byte[] _pixels = new byte[0];
//        private float[] _zbuf = new float[0];
//        private DispatcherTimer? _timer;

//        private float _dist = 10f;
//        private float _rotY = 0.785f;
//        private float _rotX = 0.6f;
//        private bool _autoRot = false;

//        // 内部变量含义（按航空标准）：
//        // _yaw   = 日志 X
//        // _roll  = 日志 Y  
//        // _pitch = 日志 Z
//        private float _yaw, _roll, _pitch;
//        private float _tYaw, _tRoll, _tPitch;
//        private int _dataCnt;
//        private DateTime _lastDt = DateTime.MinValue;

//        private bool _drag = false;
//        private Point _lastMp;
//        private bool _initDone = false;

//        private bool _showGrid = true;
//        private bool _showAxes = true;

//        private static readonly Regex _re = new Regex(
//            @"Same_angle\s+x:(?<x>-?\d+)\s+y:(?<y>-?\d+)\s+z:(?<z>-?\d+)",
//            RegexOptions.Compiled | RegexOptions.IgnoreCase);

//        public int ParentPortIndex { get; private set; }
//        public string ParentPortName => ParentPortIndex > 0 ? $"串口监控 #{ParentPortIndex}" : "未知";

//        public Attitude3DWindow(int parentPortIndex = 0)
//        {
//            ParentPortIndex = parentPortIndex;
//            InitializeComponent();
//            Loaded += OnLoaded;
//            Closed += OnClosed;
//        }

//        private void OnLoaded(object sender, RoutedEventArgs e)
//        {
//            UpdateParentInfo();
//            Dispatcher.BeginInvoke(new Action(() =>
//            {
//                InitBitmap();
//                _initDone = true;
//                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
//                _timer.Tick += OnTick;
//                _timer.Start();
//            }), DispatcherPriority.Render);
//        }

//        private void OnClosed(object? sender, EventArgs e) => _timer?.Stop();

//        private void InitBitmap()
//        {
//            _w = (int)(AttitudeImage.ActualWidth > 0 ? AttitudeImage.ActualWidth : 800);
//            _h = (int)(AttitudeImage.ActualHeight > 0 ? AttitudeImage.ActualHeight : 500);

//            int size = _w * _h;
//            _pixels = new byte[size * 4];
//            _zbuf = new float[size];

//            _bmp = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Bgra32, null);
//            AttitudeImage.Source = _bmp;

//            UpdateCrosshair();
//        }

//        private void UpdateCrosshair()
//        {
//            if (OverlayCanvas.ActualWidth < 1) return;
//            double cx = OverlayCanvas.ActualWidth / 2;
//            double cy = OverlayCanvas.ActualHeight / 2;
//            CrossH.X1 = 0; CrossH.Y1 = cy; CrossH.X2 = OverlayCanvas.ActualWidth; CrossH.Y2 = cy;
//            CrossV.X1 = cx; CrossV.Y1 = 0; CrossV.X2 = cx; CrossV.Y2 = OverlayCanvas.ActualHeight;
//        }

//        public void EnqueueLog(string log)
//        {
//            if (string.IsNullOrWhiteSpace(log)) return;
//            var m = _re.Match(log);
//            if (!m.Success) return;
//            if (float.TryParse(m.Groups["x"].Value, out float x) &&
//                float.TryParse(m.Groups["y"].Value, out float y) &&
//                float.TryParse(m.Groups["z"].Value, out float z))
//            {
//                // 日志 X→Yaw, Y→Roll, Z→Pitch
//                SetTarget(x, y, z);
//            }
//        }

//        public void SetTarget(float yaw, float roll, float pitch)
//        {
//            _tYaw = yaw; _tRoll = roll; _tPitch = pitch;
//            _dataCnt++;
//            _lastDt = DateTime.Now;
//        }

//        private void OnTick(object? sender, EventArgs e)
//        {
//            if (!_initDone) return;

//            _yaw += (_tYaw - _yaw) * 0.15f;
//            _roll += (_tRoll - _roll) * 0.15f;
//            _pitch += (_tPitch - _pitch) * 0.15f;

//            if (_autoRot) _rotY += 0.005f;

//            Render();
//            UpdateUI();
//        }

//        private void Render()
//        {
//            if (_bmp == null) return;

//            int size = _w * _h;
//            for (int i = 0; i < size; i++)
//            {
//                int o = i * 4;
//                _pixels[o] = 12;
//                _pixels[o + 1] = 10;
//                _pixels[o + 2] = 8;
//                _pixels[o + 3] = 255;
//            }
//            Array.Fill(_zbuf, float.MaxValue);

//            // 相机
//            V3 cam = new V3(
//                _dist * (float)(Math.Cos(_rotX) * Math.Sin(_rotY)),
//                _dist * (float)Math.Sin(_rotX),
//                _dist * (float)(Math.Cos(_rotX) * Math.Cos(_rotY)));

//            V3 target = new V3(0, 0, 0);
//            V3 forward = (target - cam).Norm();

//            V3 worldUp = new V3(0, 1, 0);
//            V3 right = Cross(forward, worldUp);
//            if (right.Len < 0.001f) right = new V3(1, 0, 0);
//            else right = right.Norm();
//            V3 up = Cross(right, forward).Norm();

//            float fov = 1.0f;
//            float aspect = (float)_w / _h;
//            float near = 0.1f;

//            // ========== 轴向修正：日志 X→Yaw, Y→Roll, Z→Pitch ==========
//            float cy = (float)Math.Cos(_yaw * Math.PI / 180);   // Yaw = 日志X
//            float sy = (float)Math.Sin(_yaw * Math.PI / 180);
//            float cr = (float)Math.Cos(_roll * Math.PI / 180);  // Roll = 日志Y
//            float sr = (float)Math.Sin(_roll * Math.PI / 180);
//            float cp = (float)Math.Cos(_pitch * Math.PI / 180); // Pitch = 日志Z
//            float sp = (float)Math.Sin(_pitch * Math.PI / 180);

//            // ZYX 欧拉角旋转：先 Yaw(Z轴) → Pitch(Y轴) → Roll(X轴)
//            V3 Rot(V3 v)
//            {
//                // Rx(Roll) - 绕X轴旋转（日志Y）
//                float y1 = cr * v.Y - sr * v.Z;
//                float z1 = sr * v.Y + cr * v.Z;
//                // Ry(Pitch) - 绕Y轴旋转（日志Z）
//                float x2 = cp * v.X + sp * z1;
//                float z2 = -sp * v.X + cp * z1;
//                // Rz(Yaw) - 绕Z轴旋转（日志X）
//                float x3 = cy * x2 - sy * y1;
//                float y3 = sy * x2 + cy * y1;
//                return new V3(x3, y3, z2);
//            }

//            // 世界空间投影（背景用，不旋转）
//            (int x, int y, float z)? ProjWorld(V3 v)
//            {
//                V3 view = v - cam;
//                float vx = Dot(view, right);
//                float vy = Dot(view, up);
//                float vz = Dot(view, forward);

//                if (vz < near) return null;

//                float px = vx / (fov * vz * aspect);
//                float py = vy / (fov * vz);

//                int sx = (int)((px + 1) * 0.5f * _w);
//                int sy = (int)((1 - py) * 0.5f * _h);

//                if (sx < -100 || sx >= _w + 100 || sy < -100 || sy >= _h + 100) return null;
//                return (sx, sy, vz);
//            }

//            // 飞机投影（先旋转再投影）
//            (int x, int y, float z)? ProjPlane(V3 v)
//            {
//                return ProjWorld(Rot(v));
//            }

//            // 绘制背景（不旋转）
//            if (_showGrid) DrawGrid(ProjWorld);
//            if (_showAxes) DrawAxes(ProjWorld);

//            // 绘制飞机（旋转）
//            DrawPaperPlane(ProjPlane);

//            _bmp.WritePixels(new Int32Rect(0, 0, _w, _h), _pixels, _w * 4, 0);
//        }

//        private void DrawPaperPlane(Func<V3, (int x, int y, float z)?> proj)
//        {
//            // 美化配色：金属银、深空灰、亮白、科技蓝、警示红
//            Color bodyMain = Color.FromRgb(240, 245, 250);   // 主体亮白
//            Color bodyDark = Color.FromRgb(190, 200, 215);   // 阴影灰
//            Color bodyWing = Color.FromRgb(220, 230, 240);   // 机翼浅灰
//            Color bodyEdge = Color.FromRgb(160, 175, 195);   // 轮廓深灰
//            Color cockpit = Color.FromRgb(80, 160, 255);   // 座舱科技蓝
//            Color arrowRed = Color.FromRgb(255, 60, 90);    // 箭头亮红
//            Color upYellow = Color.FromRgb(255, 220, 80);    // 上方向金黄

//            // 【美化版】流线型飞机顶点（更长、更协调、更真实）
//            V3 nose = new V3(0, 0, 3.8f);
//            V3 bodyBack = new V3(0, 0, -3.2f);
//            V3 wingL1 = new V3(-2.2f, 0, 1.2f);
//            V3 wingR1 = new V3(2.2f, 0, 1.2f);
//            V3 wingL2 = new V3(-4.0f, 0, -1.8f);
//            V3 wingR2 = new V3(4.0f, 0, -1.8f);
//            V3 tailL = new V3(-1.0f, 0, -3.0f);
//            V3 tailR = new V3(1.0f, 0, -3.0f);
//            V3 tailTop = new V3(0, 1.6f, -2.4f);
//            V3 belly = new V3(0, -1.0f, 0);
//            V3 cockpitPos = new V3(0, 0.4f, 0.5f);

//            // ========== 机身主体 ==========
//            FillTri(nose, wingL1, wingR1, bodyMain, proj);
//            FillTri(wingL1, wingL2, bodyBack, bodyWing, proj);
//            FillTri(wingR1, wingR2, bodyBack, bodyWing, proj);
//            FillTri(wingL1, bodyBack, wingR1, bodyMain, proj);

//            // ========== 机翼侧面 ==========
//            FillTri(wingL1, belly, wingL2, bodyDark, proj);
//            FillTri(wingR1, belly, wingR2, bodyDark, proj);

//            // ========== 尾翼 ==========
//            FillTri(bodyBack, tailTop, tailL, bodyWing, proj);
//            FillTri(bodyBack, tailTop, tailR, bodyWing, proj);
//            FillTri(tailL, tailTop, tailR, bodyMain, proj);

//            // ========== 座舱（蓝色亮点） ==========
//            FillTri(cockpitPos, wingL1, wingR1, cockpit, proj);

//            // ========== 轮廓线（强化立体感） ==========
//            Line3(nose, wingL2, bodyEdge, 2, proj);
//            Line3(nose, wingR2, bodyEdge, 2, proj);
//            Line3(wingL2, bodyBack, bodyEdge, 2, proj);
//            Line3(wingR2, bodyBack, bodyEdge, 2, proj);
//            Line3(wingL1, wingL2, bodyEdge, 2, proj);
//            Line3(wingR1, wingR2, bodyEdge, 2, proj);
//            Line3(bodyBack, tailTop, bodyEdge, 2, proj);
//            Line3(tailTop, wingL2, bodyEdge, 1, proj);
//            Line3(tailTop, wingR2, bodyEdge, 1, proj);

//            // 机身中线
//            Line3(nose, bodyBack, bodyMain, 2, proj);
//            Line3(nose, belly, bodyDark, 2, proj);
//            Line3(bodyBack, belly, bodyDark, 2, proj);

//            // ========== 机头方向箭头（高亮红） ==========
//            V3 arrTip = new V3(0, 0, 4.8f);
//            V3 arrL = new V3(-0.45f, 0, 4.0f);
//            V3 arrR = new V3(0.45f, 0, 4.0f);
//            Line3(nose, arrTip, arrowRed, 4, proj);
//            Line3(arrTip, arrL, arrowRed, 3, proj);
//            Line3(arrTip, arrR, arrowRed, 3, proj);

//            // ========== 上方向指示（亮黄） ==========
//            V3 upTip = new V3(0, 3.0f, 0);
//            Line3(new V3(0, 0, 0), upTip, upYellow, 3, proj);
//            for (int i = 0; i < 12; i++)
//            {
//                float a1 = (float)(2 * Math.PI * i / 12);
//                float a2 = (float)(2 * Math.PI * (i + 1) / 12);
//                V3 c1 = new V3(0.22f * (float)Math.Cos(a1), 3.0f, 0.22f * (float)Math.Sin(a1));
//                V3 c2 = new V3(0.22f * (float)Math.Cos(a2), 3.0f, 0.22f * (float)Math.Sin(a2));
//                Line3(c1, c2, upYellow, 2, proj);
//            }
//        }

//        //private void DrawPaperPlane(Func<V3, (int x, int y, float z)?> proj)
//        //{
//        //    Color paper = Color.FromRgb(230, 230, 240);
//        //    Color fold = Color.FromRgb(200, 200, 220);
//        //    Color edge = Color.FromRgb(180, 180, 200);

//        //    // 纸飞机顶点（Z轴向前为机头）
//        //    V3 nose = new V3(0, 0, 3.0f);
//        //    V3 wingFrontL = new V3(-2.0f, 0, 1.0f);
//        //    V3 wingFrontR = new V3(2.0f, 0, 1.0f);
//        //    V3 wingBackL = new V3(-3.5f, 0, -1.5f);
//        //    V3 wingBackR = new V3(3.5f, 0, -1.5f);
//        //    V3 tail = new V3(0, 0, -2.5f);
//        //    V3 tailTop = new V3(0, 1.2f, -2.0f);
//        //    V3 belly = new V3(0, -0.8f, 0);

//        //    // 机翼
//        //    FillTri(nose, wingFrontL, wingBackL, paper, proj);
//        //    FillTri(nose, wingFrontR, wingBackR, paper, proj);
//        //    FillTri(wingBackL, tail, wingFrontL, paper, proj);
//        //    FillTri(wingBackR, tail, wingFrontR, paper, proj);

//        //    // 折痕
//        //    Line3(nose, belly, fold, 2, proj);
//        //    Line3(wingFrontL, belly, fold, 2, proj);
//        //    Line3(wingBackL, belly, fold, 2, proj);
//        //    Line3(wingFrontR, belly, fold, 2, proj);
//        //    Line3(wingBackR, belly, fold, 2, proj);
//        //    Line3(tail, belly, fold, 2, proj);

//        //    // 尾翼
//        //    FillTri(tail, tailTop, wingBackL, fold, proj);
//        //    FillTri(tail, tailTop, wingBackR, fold, proj);

//        //    // 轮廓
//        //    Line3(nose, wingBackL, edge, 2, proj);
//        //    Line3(nose, wingBackR, edge, 2, proj);
//        //    Line3(wingBackL, tail, edge, 2, proj);
//        //    Line3(wingBackR, tail, edge, 2, proj);
//        //    Line3(wingBackL, wingBackR, edge, 1, proj);
//        //    Line3(nose, tail, edge, 2, proj);
//        //    Line3(tail, tailTop, edge, 2, proj);
//        //    Line3(tailTop, wingBackL, edge, 1, proj);
//        //    Line3(tailTop, wingBackR, edge, 1, proj);

//        //    // 机头方向箭头（红色）
//        //    V3 arrowTip = new V3(0, 0, 4.0f);
//        //    V3 arrowL = new V3(-0.3f, 0, 3.4f);
//        //    V3 arrowR = new V3(0.3f, 0, 3.4f);
//        //    Line3(nose, arrowTip, Color.FromRgb(255, 80, 80), 3, proj);
//        //    Line3(arrowTip, arrowL, Color.FromRgb(255, 80, 80), 2, proj);
//        //    Line3(arrowTip, arrowR, Color.FromRgb(255, 80, 80), 2, proj);

//        //    // 上方向指示（黄色）
//        //    V3 upTip = new V3(0, 2.5f, 0);
//        //    Line3(new V3(0, 0, 0), upTip, Color.FromRgb(255, 200, 0), 2, proj);
//        //    for (int i = 0; i < 12; i++)
//        //    {
//        //        float a1 = (float)(2 * Math.PI * i / 12);
//        //        float a2 = (float)(2 * Math.PI * (i + 1) / 12);
//        //        V3 c1 = new V3(0.15f * (float)Math.Cos(a1), 2.5f, 0.15f * (float)Math.Sin(a1));
//        //        V3 c2 = new V3(0.15f * (float)Math.Cos(a2), 2.5f, 0.15f * (float)Math.Sin(a2));
//        //        Line3(c1, c2, Color.FromRgb(255, 200, 0), 2, proj);
//        //    }
//        //}

//        private void FillTri(V3 p1, V3 p2, V3 p3, Color c, Func<V3, (int x, int y, float z)?> proj)
//        {
//            var r1 = proj(p1);
//            var r2 = proj(p2);
//            var r3 = proj(p3);
//            if (!r1.HasValue || !r2.HasValue || !r3.HasValue) return;
//            Line3(p1, p2, c, 1, proj);
//            Line3(p2, p3, c, 1, proj);
//            Line3(p3, p1, c, 1, proj);
//        }

//        private void DrawGrid(Func<V3, (int x, int y, float z)?> proj)
//        {
//            Color gc = Color.FromRgb(35, 45, 55);
//            for (int i = -8; i <= 8; i++)
//            {
//                Line3(new V3(i, 0, -8), new V3(i, 0, 8), gc, 1, proj);
//                Line3(new V3(-8, 0, i), new V3(8, 0, i), gc, 1, proj);
//            }
//        }

//        private void DrawAxes(Func<V3, (int x, int y, float z)?> proj)
//        {
//            Line3(new V3(0, 0, 0), new V3(4, 0, 0), Color.FromRgb(255, 60, 60), 3, proj);
//            Line3(new V3(0, 0, 0), new V3(0, 4, 0), Color.FromRgb(60, 255, 60), 3, proj);
//            Line3(new V3(0, 0, 0), new V3(0, 0, 4), Color.FromRgb(60, 120, 255), 3, proj);
//        }

//        private void Line3(V3 p1, V3 p2, Color c, int thick, Func<V3, (int x, int y, float z)?> proj)
//        {
//            var r1 = proj(p1);
//            var r2 = proj(p2);
//            if (!r1.HasValue || !r2.HasValue) return;

//            int x0 = r1.Value.x, y0 = r1.Value.y;
//            int x1 = r2.Value.x, y1 = r2.Value.y;
//            float z0 = r1.Value.z, z1 = r2.Value.z;

//            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
//            int dy = Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
//            int err = dx - dy;
//            int total = dx + dy;
//            if (total == 0) total = 1;

//            while (true)
//            {
//                float t = (float)(Math.Abs(x1 - x0) + Math.Abs(y1 - y0)) / total;
//                float z = z0 + (z1 - z0) * (1 - t);

//                int half = thick / 2;
//                for (int oy = -half; oy <= half; oy++)
//                {
//                    for (int ox = -half; ox <= half; ox++)
//                    {
//                        Pixel(x0 + ox, y0 + oy, z, c);
//                    }
//                }

//                if (x0 == x1 && y0 == y1) break;
//                int e2 = 2 * err;
//                if (e2 > -dy) { err -= dy; x0 += sx; }
//                if (e2 < dx) { err += dx; y0 += sy; }
//            }
//        }

//        private void Pixel(int x, int y, float z, Color c)
//        {
//            if (x < 0 || x >= _w || y < 0 || y >= _h) return;
//            int idx = y * _w + x;
//            if (z >= _zbuf[idx]) return;
//            _zbuf[idx] = z;
//            int o = idx * 4;
//            _pixels[o] = c.B;
//            _pixels[o + 1] = c.G;
//            _pixels[o + 2] = c.R;
//            _pixels[o + 3] = 255;
//        }

//        private static V3 Cross(V3 a, V3 b) => new V3(
//            a.Y * b.Z - a.Z * b.Y,
//            a.Z * b.X - a.X * b.Z,
//            a.X * b.Y - a.Y * b.X);

//        private static float Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

//        // ========== UI更新：标签对应修正后的轴向 ==========
//        private void UpdateUI()
//        {
//            // X→Yaw(偏航), Y→Roll(横滚), Z→Pitch(俯仰)
//            TxtRoll.Text = $"{_yaw:F1}°";     // 日志X = Yaw
//            TxtPitch.Text = $"{_roll:F1}°";   // 日志Y = Roll
//            TxtYaw.Text = $"{_pitch:F1}°";    // 日志Z = Pitch

//            BarRoll.Value = Math.Max(0, Math.Min(360, _yaw + 180));
//            BarPitch.Value = Math.Max(0, Math.Min(360, _roll + 180));
//            BarYaw.Value = Math.Max(0, Math.Min(360, _pitch + 180));

//            TxtDataInfo.Text = $"数据计数: {_dataCnt} | 最后更新: {(_lastDt == DateTime.MinValue ? "--" : _lastDt.ToString("HH:mm:ss.fff"))}";

//            float dy = _rotX * 180 / (float)Math.PI;
//            float dh = _rotY * 180 / (float)Math.PI;
//            TxtCameraInfo.Text = $"距离: {_dist:F1}\n俯仰: {dy:F0}°\n水平: {dh:F0}°";
//        }

//        private void UpdateParentInfo()
//        {
//            TxtParentInfo.Text = $"所属串口: {ParentPortName} | 3D 姿态可视化";
//        }

//        private void AttitudeImage_MouseDown(object sender, MouseButtonEventArgs e)
//        {
//            _lastMp = e.GetPosition(AttitudeImage);
//            if (e.LeftButton == MouseButtonState.Pressed)
//            {
//                _drag = true;
//                AttitudeImage.CaptureMouse();
//            }
//        }

//        private void AttitudeImage_MouseMove(object sender, MouseEventArgs e)
//        {
//            if (!_drag) return;
//            Point p = e.GetPosition(AttitudeImage);
//            _rotY -= (float)(p.X - _lastMp.X) * 0.008f;
//            _rotX += (float)(p.Y - _lastMp.Y) * 0.008f;
//            _rotX = Math.Max(0.1f, Math.Min((float)Math.PI - 0.1f, _rotX));
//            _lastMp = p;
//        }

//        private void AttitudeImage_MouseUp(object sender, MouseButtonEventArgs e)
//        {
//            _drag = false;
//            AttitudeImage.ReleaseMouseCapture();
//        }

//        private void AttitudeImage_MouseWheel(object sender, MouseWheelEventArgs e)
//        {
//            _dist -= e.Delta * 0.005f;
//            _dist = Math.Max(3f, Math.Min(15f, _dist));
//        }

//        private void BtnResetView_Click(object sender, RoutedEventArgs e)
//        {
//            _dist = 10f;
//            _rotY = 0.785f;
//            _rotX = 0.6f;
//        }

//        private void BtnResetAttitude_Click(object sender, RoutedEventArgs e)
//        {
//            _tYaw = _tRoll = _tPitch = 0;
//        }

//        private void ChkShowGrid_Checked(object sender, RoutedEventArgs e) => _showGrid = true;
//        private void ChkShowGrid_Unchecked(object sender, RoutedEventArgs e) => _showGrid = false;
//        private void ChkShowAxes_Checked(object sender, RoutedEventArgs e) => _showAxes = true;
//        private void ChkShowAxes_Unchecked(object sender, RoutedEventArgs e) => _showAxes = false;
//        private void ChkAutoRotate_Checked(object sender, RoutedEventArgs e) => _autoRot = true;
//        private void ChkAutoRotate_Unchecked(object sender, RoutedEventArgs e) => _autoRot = false;

//        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
//        {
//            e.Cancel = true;
//            Hide();
//        }

//        public void SetAttitude(float yaw, float roll, float pitch)
//        {
//            SetTarget(yaw, roll, pitch);
//        }
//    }
//}

// Attitude3DWindow.xaml.cs - 资源安全完整版
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.RegularExpressions;

namespace MySerialPortAssistant04
{
    public interface IDisposableWindow
    {
        void ForceClose();
    }

    public partial class Attitude3DWindow : Window, ILogReceiver, IDisposableWindow, IDisposable
    {
        private struct V3
        {
            public float X, Y, Z;
            public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
            public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static V3 operator *(V3 v, float s) => new V3(v.X * s, v.Y * s, v.Z * s);
            public float Len => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
            public V3 Norm()
            {
                float len = Len;
                return len < 0.0001f ? new V3(0, 0, 0) : new V3(X / len, Y / len, Z / len);
            }
        }

        // ========== 可释放资源 ==========
        private WriteableBitmap? _bmp;
        private int _w, _h;
        private byte[] _pixels = new byte[0];
        private float[] _zbuf = new float[0];
        private DispatcherTimer? _timer;

        // ========== 状态变量 ==========
        private float _dist = 10f;
        private float _rotY = 0.785f;
        private float _rotX = 0.6f;
        private bool _autoRot = false;

        // 内部变量含义（按航空标准）：
        // _yaw   = 日志 X
        // _roll  = 日志 Y  
        // _pitch = 日志 Z
        private float _yaw, _roll, _pitch;
        private float _tYaw, _tRoll, _tPitch;
        private int _dataCnt;
        private DateTime _lastDt = DateTime.MinValue;

        private bool _drag = false;
        private Point _lastMp;
        private bool _initDone = false;

        private bool _showGrid = true;
        private bool _showAxes = true;

        // ========== 生命周期控制 ==========
        private bool _isDisposed = false;
        private bool _forceClose = false;

        private static readonly Regex _re = new Regex(
            @"Same_angle\s+x:(?<x>-?\d+)\s+y:(?<y>-?\d+)\s+z:(?<z>-?\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public int ParentPortIndex { get; private set; }
        public string ParentPortName => ParentPortIndex > 0 ? $"串口监控 #{ParentPortIndex}" : "未知";

        public Attitude3DWindow(int parentPortIndex = 0)
        {
            ParentPortIndex = parentPortIndex;
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateParentInfo();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                InitBitmap();
                _initDone = true;
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _timer.Tick += OnTick;
                _timer.Start();
            }), DispatcherPriority.Render);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            DisposeResources();
        }

        // ========== IDisposable 实现 ==========
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            DisposeResources();

            // 如果窗口处于隐藏状态，需要调度到UI线程真正关闭
            if (!IsVisible && !_forceClose && Dispatcher.CheckAccess())
            {
                _forceClose = true;
                Close();
            }
            else if (!IsVisible && !_forceClose)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _forceClose = true;
                    Close();
                }), DispatcherPriority.Normal);
            }
        }

        // ========== IDisposableWindow 实现：强制真正关闭 ==========
        public void ForceClose()
        {
            if (_isDisposed) return;
            _forceClose = true;
            Close();  // 这次会真正关闭，不触发 Hide()
        }

        // ========== 统一资源释放 ==========
        private void DisposeResources()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTick;
                _timer = null;
            }

            if (_drag)
            {
                AttitudeImage?.ReleaseMouseCapture();
                _drag = false;
            }

            // 释放 WriteableBitmap（关键：解除对底层缓冲区的锁定）
            if (_bmp != null)
            {
                try
                {
                    _bmp.Lock();
                    _bmp.AddDirtyRect(new Int32Rect(0, 0, 0, 0)); // 空更新，确保无挂起操作
                    _bmp.Unlock();
                }
                catch { }
                _bmp = null;
            }

            // 释放大数组
            _pixels = new byte[0];
            _zbuf = new float[0];

            // 移除所有事件订阅
            Loaded -= OnLoaded;
            Closed -= OnClosed;
            if (AttitudeImage != null)
            {
                AttitudeImage.MouseDown -= AttitudeImage_MouseDown;
                AttitudeImage.MouseMove -= AttitudeImage_MouseMove;
                AttitudeImage.MouseUp -= AttitudeImage_MouseUp;
                AttitudeImage.MouseWheel -= AttitudeImage_MouseWheel;
            }
            if (BtnResetView != null) BtnResetView.Click -= BtnResetView_Click;
            if (BtnResetAttitude != null) BtnResetAttitude.Click -= BtnResetAttitude_Click;
            if (ChkShowGrid != null)
            {
                ChkShowGrid.Checked -= ChkShowGrid_Checked;
                ChkShowGrid.Unchecked -= ChkShowGrid_Unchecked;
            }
            if (ChkShowAxes != null)
            {
                ChkShowAxes.Checked -= ChkShowAxes_Checked;
                ChkShowAxes.Unchecked -= ChkShowAxes_Unchecked;
            }
            if (ChkAutoRotate != null)
            {
                ChkAutoRotate.Checked -= ChkAutoRotate_Checked;
                ChkAutoRotate.Unchecked -= ChkAutoRotate_Unchecked;
            }

            _autoRot = false;
            _initDone = false;
        }

        // ========== 窗口关闭逻辑：用户点X时隐藏，程序退出时真正关闭 ==========
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_forceClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            // 强制关闭路径：DisposeResources 已在 OnClosed 中调用
            base.OnClosing(e);
        }

        private void InitBitmap()
        {
            // 先释放旧的
            if (_bmp != null)
            {
                _bmp = null;
                _pixels = new byte[0];
                _zbuf = new float[0];
            }

            _w = (int)(AttitudeImage.ActualWidth > 0 ? AttitudeImage.ActualWidth : 800);
            _h = (int)(AttitudeImage.ActualHeight > 0 ? AttitudeImage.ActualHeight : 500);

            int size = _w * _h;
            _pixels = new byte[size * 4];
            _zbuf = new float[size];

            _bmp = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Bgra32, null);
            AttitudeImage.Source = _bmp;

            UpdateCrosshair();
        }

        private void UpdateCrosshair()
        {
            if (OverlayCanvas.ActualWidth < 1) return;
            double cx = OverlayCanvas.ActualWidth / 2;
            double cy = OverlayCanvas.ActualHeight / 2;
            CrossH.X1 = 0; CrossH.Y1 = cy; CrossH.X2 = OverlayCanvas.ActualWidth; CrossH.Y2 = cy;
            CrossV.X1 = cx; CrossV.Y1 = 0; CrossV.X2 = cx; CrossV.Y2 = OverlayCanvas.ActualHeight;
        }

        public void EnqueueLog(string log)
        {
            if (string.IsNullOrWhiteSpace(log)) return;
            var m = _re.Match(log);
            if (!m.Success) return;
            if (float.TryParse(m.Groups["x"].Value, out float x) &&
                float.TryParse(m.Groups["y"].Value, out float y) &&
                float.TryParse(m.Groups["z"].Value, out float z))
            {
                SetTarget(x, y, z);
            }
        }

        public void SetTarget(float yaw, float roll, float pitch)
        {
            _tYaw = yaw; _tRoll = roll; _tPitch = pitch;
            _dataCnt++;
            _lastDt = DateTime.Now;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (!_initDone || _isDisposed) return;

            _yaw += (_tYaw - _yaw) * 0.15f;
            _roll += (_tRoll - _roll) * 0.15f;
            _pitch += (_tPitch - _pitch) * 0.15f;

            if (_autoRot) _rotY += 0.005f;

            Render();
            UpdateUI();
        }

        private void Render()
        {
            if (_bmp == null || _isDisposed) return;

            int size = _w * _h;
            for (int i = 0; i < size; i++)
            {
                int o = i * 4;
                _pixels[o] = 12;
                _pixels[o + 1] = 10;
                _pixels[o + 2] = 8;
                _pixels[o + 3] = 255;
            }
            Array.Fill(_zbuf, float.MaxValue);

            // 相机
            V3 cam = new V3(
                _dist * (float)(Math.Cos(_rotX) * Math.Sin(_rotY)),
                _dist * (float)Math.Sin(_rotX),
                _dist * (float)(Math.Cos(_rotX) * Math.Cos(_rotY)));

            V3 target = new V3(0, 0, 0);
            V3 forward = (target - cam).Norm();

            V3 worldUp = new V3(0, 1, 0);
            V3 right = Cross(forward, worldUp);
            if (right.Len < 0.001f) right = new V3(1, 0, 0);
            else right = right.Norm();
            V3 up = Cross(right, forward).Norm();

            float fov = 1.0f;
            float aspect = (float)_w / _h;
            float near = 0.1f;

            // ========== 轴向修正：日志 X→Yaw, Y→Roll, Z→Pitch ==========
            float cy = (float)Math.Cos(_yaw * Math.PI / 180);   // Yaw = 日志X
            float sy = (float)Math.Sin(_yaw * Math.PI / 180);
            float cr = (float)Math.Cos(_roll * Math.PI / 180);  // Roll = 日志Y
            float sr = (float)Math.Sin(_roll * Math.PI / 180);
            float cp = (float)Math.Cos(_pitch * Math.PI / 180); // Pitch = 日志Z
            float sp = (float)Math.Sin(_pitch * Math.PI / 180);

            // ZYX 欧拉角旋转：先 Yaw(Z轴) → Pitch(Y轴) → Roll(X轴)
            V3 Rot(V3 v)
            {
                // Rx(Roll) - 绕X轴旋转（日志Y）
                float y1 = cr * v.Y - sr * v.Z;
                float z1 = sr * v.Y + cr * v.Z;
                // Ry(Pitch) - 绕Y轴旋转（日志Z）
                float x2 = cp * v.X + sp * z1;
                float z2 = -sp * v.X + cp * z1;
                // Rz(Yaw) - 绕Z轴旋转（日志X）
                float x3 = cy * x2 - sy * y1;
                float y3 = sy * x2 + cy * y1;
                return new V3(x3, y3, z2);
            }

            // 世界空间投影（背景用，不旋转）
            (int x, int y, float z)? ProjWorld(V3 v)
            {
                V3 view = v - cam;
                float vx = Dot(view, right);
                float vy = Dot(view, up);
                float vz = Dot(view, forward);

                if (vz < near) return null;

                float px = vx / (fov * vz * aspect);
                float py = vy / (fov * vz);

                int sx = (int)((px + 1) * 0.5f * _w);
                int sy = (int)((1 - py) * 0.5f * _h);

                if (sx < -100 || sx >= _w + 100 || sy < -100 || sy >= _h + 100) return null;
                return (sx, sy, vz);
            }

            // 飞机投影（先旋转再投影）
            (int x, int y, float z)? ProjPlane(V3 v)
            {
                return ProjWorld(Rot(v));
            }

            // 绘制背景（不旋转）
            if (_showGrid) DrawGrid(ProjWorld);
            if (_showAxes) DrawAxes(ProjWorld);

            // 绘制飞机（旋转）
            DrawPaperPlane(ProjPlane);

            _bmp.WritePixels(new Int32Rect(0, 0, _w, _h), _pixels, _w * 4, 0);
        }

        private void DrawPaperPlane(Func<V3, (int x, int y, float z)?> proj)
        {
            // 美化配色：金属银、深空灰、亮白、科技蓝、警示红
            Color bodyMain = Color.FromRgb(240, 245, 250);   // 主体亮白
            Color bodyDark = Color.FromRgb(190, 200, 215);   // 阴影灰
            Color bodyWing = Color.FromRgb(220, 230, 240);   // 机翼浅灰
            Color bodyEdge = Color.FromRgb(160, 175, 195);   // 轮廓深灰
            Color cockpit = Color.FromRgb(80, 160, 255);   // 座舱科技蓝
            Color arrowRed = Color.FromRgb(255, 60, 90);    // 箭头亮红
            Color upYellow = Color.FromRgb(255, 220, 80);    // 上方向金黄

            // 【美化版】流线型飞机顶点（更长、更协调、更真实）
            V3 nose = new V3(0, 0, 3.8f);
            V3 bodyBack = new V3(0, 0, -3.2f);
            V3 wingL1 = new V3(-2.2f, 0, 1.2f);
            V3 wingR1 = new V3(2.2f, 0, 1.2f);
            V3 wingL2 = new V3(-4.0f, 0, -1.8f);
            V3 wingR2 = new V3(4.0f, 0, -1.8f);
            V3 tailL = new V3(-1.0f, 0, -3.0f);
            V3 tailR = new V3(1.0f, 0, -3.0f);
            V3 tailTop = new V3(0, 1.6f, -2.4f);
            V3 belly = new V3(0, -1.0f, 0);
            V3 cockpitPos = new V3(0, 0.4f, 0.5f);

            // ========== 机身主体 ==========
            FillTri(nose, wingL1, wingR1, bodyMain, proj);
            FillTri(wingL1, wingL2, bodyBack, bodyWing, proj);
            FillTri(wingR1, wingR2, bodyBack, bodyWing, proj);
            FillTri(wingL1, bodyBack, wingR1, bodyMain, proj);

            // ========== 机翼侧面 ==========
            FillTri(wingL1, belly, wingL2, bodyDark, proj);
            FillTri(wingR1, belly, wingR2, bodyDark, proj);

            // ========== 尾翼 ==========
            FillTri(bodyBack, tailTop, tailL, bodyWing, proj);
            FillTri(bodyBack, tailTop, tailR, bodyWing, proj);
            FillTri(tailL, tailTop, tailR, bodyMain, proj);

            // ========== 座舱（蓝色亮点） ==========
            FillTri(cockpitPos, wingL1, wingR1, cockpit, proj);

            // ========== 轮廓线（强化立体感） ==========
            Line3(nose, wingL2, bodyEdge, 2, proj);
            Line3(nose, wingR2, bodyEdge, 2, proj);
            Line3(wingL2, bodyBack, bodyEdge, 2, proj);
            Line3(wingR2, bodyBack, bodyEdge, 2, proj);
            Line3(wingL1, wingL2, bodyEdge, 2, proj);
            Line3(wingR1, wingR2, bodyEdge, 2, proj);
            Line3(bodyBack, tailTop, bodyEdge, 2, proj);
            Line3(tailTop, wingL2, bodyEdge, 1, proj);
            Line3(tailTop, wingR2, bodyEdge, 1, proj);

            // 机身中线
            Line3(nose, bodyBack, bodyMain, 2, proj);
            Line3(nose, belly, bodyDark, 2, proj);
            Line3(bodyBack, belly, bodyDark, 2, proj);

            // ========== 机头方向箭头（高亮红） ==========
            V3 arrTip = new V3(0, 0, 4.8f);
            V3 arrL = new V3(-0.45f, 0, 4.0f);
            V3 arrR = new V3(0.45f, 0, 4.0f);
            Line3(nose, arrTip, arrowRed, 4, proj);
            Line3(arrTip, arrL, arrowRed, 3, proj);
            Line3(arrTip, arrR, arrowRed, 3, proj);

            // ========== 上方向指示（亮黄） ==========
            V3 upTip = new V3(0, 3.0f, 0);
            Line3(new V3(0, 0, 0), upTip, upYellow, 3, proj);
            for (int i = 0; i < 12; i++)
            {
                float a1 = (float)(2 * Math.PI * i / 12);
                float a2 = (float)(2 * Math.PI * (i + 1) / 12);
                V3 c1 = new V3(0.22f * (float)Math.Cos(a1), 3.0f, 0.22f * (float)Math.Sin(a1));
                V3 c2 = new V3(0.22f * (float)Math.Cos(a2), 3.0f, 0.22f * (float)Math.Sin(a2));
                Line3(c1, c2, upYellow, 2, proj);
            }
        }

        private void FillTri(V3 p1, V3 p2, V3 p3, Color c, Func<V3, (int x, int y, float z)?> proj)
        {
            var r1 = proj(p1);
            var r2 = proj(p2);
            var r3 = proj(p3);
            if (!r1.HasValue || !r2.HasValue || !r3.HasValue) return;
            Line3(p1, p2, c, 1, proj);
            Line3(p2, p3, c, 1, proj);
            Line3(p3, p1, c, 1, proj);
        }

        private void DrawGrid(Func<V3, (int x, int y, float z)?> proj)
        {
            Color gc = Color.FromRgb(35, 45, 55);
            for (int i = -8; i <= 8; i++)
            {
                Line3(new V3(i, 0, -8), new V3(i, 0, 8), gc, 1, proj);
                Line3(new V3(-8, 0, i), new V3(8, 0, i), gc, 1, proj);
            }
        }

        private void DrawAxes(Func<V3, (int x, int y, float z)?> proj)
        {
            Line3(new V3(0, 0, 0), new V3(4, 0, 0), Color.FromRgb(255, 60, 60), 3, proj);
            Line3(new V3(0, 0, 0), new V3(0, 4, 0), Color.FromRgb(60, 255, 60), 3, proj);
            Line3(new V3(0, 0, 0), new V3(0, 0, 4), Color.FromRgb(60, 120, 255), 3, proj);
        }

        private void Line3(V3 p1, V3 p2, Color c, int thick, Func<V3, (int x, int y, float z)?> proj)
        {
            var r1 = proj(p1);
            var r2 = proj(p2);
            if (!r1.HasValue || !r2.HasValue) return;

            int x0 = r1.Value.x, y0 = r1.Value.y;
            int x1 = r2.Value.x, y1 = r2.Value.y;
            float z0 = r1.Value.z, z1 = r2.Value.z;

            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int total = dx + dy;
            if (total == 0) total = 1;

            while (true)
            {
                float t = (float)(Math.Abs(x1 - x0) + Math.Abs(y1 - y0)) / total;
                float z = z0 + (z1 - z0) * (1 - t);

                int half = thick / 2;
                for (int oy = -half; oy <= half; oy++)
                {
                    for (int ox = -half; ox <= half; ox++)
                    {
                        Pixel(x0 + ox, y0 + oy, z, c);
                    }
                }

                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        private void Pixel(int x, int y, float z, Color c)
        {
            if (x < 0 || x >= _w || y < 0 || y >= _h) return;
            int idx = y * _w + x;
            if (z >= _zbuf[idx]) return;
            _zbuf[idx] = z;
            int o = idx * 4;
            _pixels[o] = c.B;
            _pixels[o + 1] = c.G;
            _pixels[o + 2] = c.R;
            _pixels[o + 3] = 255;
        }

        private static V3 Cross(V3 a, V3 b) => new V3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        private static float Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        // ========== UI更新：标签对应修正后的轴向 ==========
        private void UpdateUI()
        {
            // X→Yaw(偏航), Y→Roll(横滚), Z→Pitch(俯仰)
            TxtRoll.Text = $"{_yaw:F1}°";     // 日志X = Yaw
            TxtPitch.Text = $"{_roll:F1}°";   // 日志Y = Roll
            TxtYaw.Text = $"{_pitch:F1}°";    // 日志Z = Pitch

            BarRoll.Value = Math.Max(0, Math.Min(360, _yaw + 180));
            BarPitch.Value = Math.Max(0, Math.Min(360, _roll + 180));
            BarYaw.Value = Math.Max(0, Math.Min(360, _pitch + 180));

            TxtDataInfo.Text = $"数据计数: {_dataCnt} | 最后更新: {(_lastDt == DateTime.MinValue ? "--" : _lastDt.ToString("HH:mm:ss.fff"))}";

            float dy = _rotX * 180 / (float)Math.PI;
            float dh = _rotY * 180 / (float)Math.PI;
            TxtCameraInfo.Text = $"距离: {_dist:F1}\n俯仰: {dy:F0}°\n水平: {dh:F0}°";
        }

        private void UpdateParentInfo()
        {
            TxtParentInfo.Text = $"所属串口: {ParentPortName} | 3D 姿态可视化";
        }

        // ========== 鼠标交互 ==========
        private void AttitudeImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastMp = e.GetPosition(AttitudeImage);
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _drag = true;
                AttitudeImage.CaptureMouse();
            }
        }

        private void AttitudeImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_drag) return;
            Point p = e.GetPosition(AttitudeImage);
            _rotY -= (float)(p.X - _lastMp.X) * 0.008f;
            _rotX += (float)(p.Y - _lastMp.Y) * 0.008f;
            _rotX = Math.Max(0.1f, Math.Min((float)Math.PI - 0.1f, _rotX));
            _lastMp = p;
        }

        private void AttitudeImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _drag = false;
            AttitudeImage.ReleaseMouseCapture();
        }

        private void AttitudeImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _dist -= e.Delta * 0.005f;
            _dist = Math.Max(3f, Math.Min(15f, _dist));
        }

        // ========== 按钮事件 ==========
        private void BtnResetView_Click(object sender, RoutedEventArgs e)
        {
            _dist = 10f;
            _rotY = 0.785f;
            _rotX = 0.6f;
        }

        private void BtnResetAttitude_Click(object sender, RoutedEventArgs e)
        {
            _tYaw = _tRoll = _tPitch = 0;
        }

        private void ChkShowGrid_Checked(object sender, RoutedEventArgs e) => _showGrid = true;
        private void ChkShowGrid_Unchecked(object sender, RoutedEventArgs e) => _showGrid = false;
        private void ChkShowAxes_Checked(object sender, RoutedEventArgs e) => _showAxes = true;
        private void ChkShowAxes_Unchecked(object sender, RoutedEventArgs e) => _showAxes = false;
        private void ChkAutoRotate_Checked(object sender, RoutedEventArgs e) => _autoRot = true;
        private void ChkAutoRotate_Unchecked(object sender, RoutedEventArgs e) => _autoRot = false;

        public void SetAttitude(float yaw, float roll, float pitch)
        {
            SetTarget(yaw, roll, pitch);
        }
    }
}