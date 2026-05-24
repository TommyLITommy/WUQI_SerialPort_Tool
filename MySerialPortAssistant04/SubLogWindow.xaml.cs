using System;
using System.Text.RegularExpressions;
using System.Windows;

namespace MySerialPortAssistant04
{
    public partial class SubLogWindow : Window
    {
        private Regex? _currentRegex;

        public SubLogWindow()
        {
            InitializeComponent();
            _currentRegex = new Regex(".*", RegexOptions.Compiled);
        }

        // 主窗口通过这个方法推送日志
        public void ReceiveLog(string logLine)
        {
            try
            {
                if (_currentRegex.IsMatch(logLine))
                {
                    AppendLog(logLine);

                    // ======================
                    // 你可以在这里加自定义逻辑
                    // 例如：检测关键字、执行动作、发送指令等
                    // ======================
                    if (logLine.Contains("Critical"))
                    {
                        // 示例：触发告警
                    }
                }
            }
            catch { }
        }

        private void AppendLog(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLogOutput.AppendText($"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
                TxtLogOutput.ScrollToEnd();
            });
        }

        private void BtnApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string pattern = TxtFilterRegex.Text.Trim();
                _currentRegex = new Regex(pattern, RegexOptions.Compiled);
                MessageBox.Show($"过滤规则已应用：{pattern}", "提示");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"正则错误：{ex.Message}", "错误");
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLogOutput.Clear();
        }
    }
}