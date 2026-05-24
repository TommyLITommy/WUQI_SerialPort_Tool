namespace MySerialPortAssistant04.Models;

/// <summary>
/// dbglog_table 中解析出的一条日志格式条目，按地址索引供串口数据包匹配。
/// </summary>
public class LogEntry
{
    public string CoreName { get; set; } = "";
    public uint PointerOfAddress { get; set; }
    public uint Address { get; set; }
    public string Content { get; set; } = "";
    public long FileOffset { get; set; }
}
