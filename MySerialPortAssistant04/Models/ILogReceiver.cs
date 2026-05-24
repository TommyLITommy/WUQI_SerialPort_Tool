namespace MySerialPortAssistant04.Models;

/// <summary>
/// 子窗口接收实时日志的接口。
/// </summary>
public interface ILogReceiver
{
    void EnqueueLog(string log);
}
