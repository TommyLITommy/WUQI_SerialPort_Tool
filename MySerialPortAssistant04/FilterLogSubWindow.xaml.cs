using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace MySerialPortAssistant04
{
    public partial class FilterLogSubWindow : Window, ILogReceiver
    {
        private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
        private readonly DispatcherTimer _timer;

        private Regex? _regex;

        // 高亮样式
        private readonly Brush _highlightBrush = Brushes.Yellow;
        private readonly Brush _textColor = Brushes.White;

        // 性能优化配置
        private const int MaxLines = 2000;
        private const int MaxBatchCount = 200;

        // 父窗口信息
        public int ParentPortIndex { get; private set; } = 0;
        public string ParentPortName => ParentPortIndex > 0 ? $"串口监控 #{ParentPortIndex}" : "未知";

        public FilterLogSubWindow(int parentPortIndex = 0)
        {
            InitializeComponent();

            // 设置父窗口信息
            ParentPortIndex = parentPortIndex;
            UpdateParentInfo();

            // 关闭自动换行，提升2M波特率渲染性能
            TxtLog.Document.PageWidth = 100000;
            TxtLog.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;

            // 优化定时器
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            _timer.Tick += (s, e) => RefreshLog();
            _timer.Start();
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
            this.Title = $"过滤日志窗口 - {ParentPortName}";
        }

        public void EnqueueLog(string log) => _logQueue.Enqueue(log);

        private void RefreshLog()
        {
            bool hasNewLog = false;
            int processed = 0;

            while (_logQueue.TryDequeue(out var log) && processed < MaxBatchCount)
            {
                if (IsMatch(log, out var matches))
                {
                    AddHighlightedLog(log, matches);
                    hasNewLog = true;
                }
                processed++;
            }

            if (hasNewLog)
            {
                while (TxtLog.Document.Blocks.Count > MaxLines)
                {
                    TxtLog.Document.Blocks.Remove(TxtLog.Document.Blocks.FirstBlock);
                }
                TxtLog.ScrollToEnd();
            }
        }

        private bool IsMatch(string log, out MatchCollection? matches)
        {
            matches = null;

            if (_regex == null)
                return true;

            matches = _regex.Matches(log);
            return matches.Count > 0;
        }

        private void AddHighlightedLog(string log, MatchCollection? matches)
        {
            if (string.IsNullOrEmpty(log)) return;

            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };

            // 无高亮高速模式
            if (_regex == null || matches == null || matches.Count == 0)
            {
                paragraph.Inlines.Add(new Run(log) { Foreground = _textColor });
                TxtLog.Document.Blocks.Add(paragraph);
                return;
            }

            // 高亮渲染
            int lastPos = 0;
            foreach (Match match in matches)
            {
                if (match.Index > lastPos)
                {
                    string preText = log.Substring(lastPos, match.Index - lastPos);
                    paragraph.Inlines.Add(new Run(preText) { Foreground = _textColor });
                }

                paragraph.Inlines.Add(new Run(match.Value)
                {
                    Background = _highlightBrush,
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold
                });

                lastPos = match.Index + match.Length;
            }

            if (lastPos < log.Length)
            {
                string postText = log.Substring(lastPos);
                paragraph.Inlines.Add(new Run(postText) { Foreground = _textColor });
            }

            TxtLog.Document.Blocks.Add(paragraph);
        }

        private void BtnFilter_Click(object sender, RoutedEventArgs e)
        {
            string pattern = TxtKeyword.Text.Trim();
            if (string.IsNullOrEmpty(pattern))
            {
                _regex = null;
                return;
            }

            try
            {
                _regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"正则表达式错误：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                _regex = null;
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Document.Blocks.Clear();
            while (_logQueue.TryDequeue(out _)) { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }
    }
}