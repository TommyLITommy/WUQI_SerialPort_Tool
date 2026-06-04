using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace MySerialPortAssistant04.Services.UI;

/// <summary>
/// 在任务栏应用图标上显示当前已连接（监听中）的串口数量。
/// </summary>
public sealed class TaskbarConnectionBadge
{
    private readonly Window _window;
    private readonly TaskbarItemInfo _taskbarItem;
    private readonly Dictionary<int, ImageSource> _badgeCache = new();

    public TaskbarConnectionBadge(Window window)
    {
        _window = window;
        _taskbarItem = window.TaskbarItemInfo ?? new TaskbarItemInfo();
        window.TaskbarItemInfo = _taskbarItem;
    }

    public void Update(int connectedCount)
    {
        if (!_window.Dispatcher.CheckAccess())
        {
            _window.Dispatcher.Invoke(() => Update(connectedCount));
            return;
        }

        int count = Math.Max(0, connectedCount);
        string label = count > 99 ? "99+" : count.ToString();
        _taskbarItem.Overlay = GetOrCreateBadge(count, label);
        _taskbarItem.Description = count == 0
            ? "多串口监控系统 — 无串口连接"
            : $"多串口监控系统 — 已连接 {count} 个串口";
        _window.Title = $"多串口监控系统 ({count})";
    }

    private ImageSource GetOrCreateBadge(int key, string label)
    {
        if (_badgeCache.TryGetValue(key, out var cached))
            return cached;

        bool isZero = key == 0;
        var fillBrush = new SolidColorBrush(isZero
            ? Color.FromRgb(0x75, 0x75, 0x75)
            : Color.FromRgb(0x2E, 0x7D, 0x32));

        // 任务栏 Overlay 固定为 16×16 像素，更大画布会被系统裁切
        const int size = 16;
        const double dpi = 96;
        double pixelsPerDip = 1.0;
        double center = size / 2.0;
        double radius = 6.5;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(
                fillBrush,
                new Pen(Brushes.White, 1.5),
                new Point(center, center),
                radius,
                radius);

            double fontSize = label.Length switch
            {
                1 => 11,
                2 => 9,
                _ => 7
            };

            var text = new FormattedText(
                label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                fontSize,
                Brushes.White,
                pixelsPerDip);

            dc.DrawText(
                text,
                new Point(center - text.Width / 2, center - text.Height / 2));
        }

        var bitmap = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        _badgeCache[key] = bitmap;
        return bitmap;
    }
}
