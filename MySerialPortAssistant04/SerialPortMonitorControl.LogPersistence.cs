namespace MySerialPortAssistant04;

/// <summary>
/// 串口监控控件 — 运行时日志文件写入。
/// </summary>
public partial class SerialPortMonitorControl
{
    private void InitializeLogFile()
    {
        try
        {
            _serialLogWriter.Initialize(LogSaveDirectory, _instanceWebPort);
            AppendLog($"📄 Log saving started: {_serialLogWriter.CurrentFilePath}");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Log file init failed: {ex.Message}");
        }
    }

    public void CloseLogFile()
    {
        _serialLogWriter.Close();
        AppendLog("✅ Log file saved and closed");
    }
}
