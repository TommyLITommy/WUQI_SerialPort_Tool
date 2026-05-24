using MySerialPortAssistant04.Models;
using MySerialPortAssistant04.Services.Logging;

namespace MySerialPortAssistant04;

/// <summary>
/// 串口监控控件 — dbglog_table 加载与地址索引。
/// </summary>
public partial class SerialPortMonitorControl
{
    private void LoadLogFile(string path)
    {
        _addressMap.Clear();
        _coreGroups.Clear();

        if (!File.Exists(path))
        {
            AppendLog($"❌ File not found: {path}");
            MessageBox.Show("Log file load failed!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            byte[] fileData = File.ReadAllBytes(path);
            var parsed = LogTableParser.Parse(fileData);
            _addressMap = parsed.AddressMap;
            _coreGroups = parsed.CoreGroups;
            AppendLog($"✅ Log file loaded, indexed {_addressMap.Count} entries");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Parse error: {ex.Message}");
            MessageBox.Show("Log file load failed!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
