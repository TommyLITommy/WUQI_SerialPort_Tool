using System.Windows;

namespace MySerialPortAssistant04.Models;

/// <summary>
/// 分析子窗口的抽象基类。
/// </summary>
public abstract class SubWindowBase : Window
{
    public abstract string WindowTypeName { get; }
    public abstract void ReceiveRawLog(string rawLog);
    public virtual void Cleanup() { }
}
