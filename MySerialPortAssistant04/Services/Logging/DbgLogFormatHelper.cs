namespace MySerialPortAssistant04.Services.Logging;

/// <summary>
/// dbglog 日志行前缀与格式模板提取。
/// </summary>
public static class DbgLogFormatHelper
{
    public static string ExtractFormatTemplate(string rawContent)
    {
        const string prefix = "[Format: ";
        if (!rawContent.Contains(prefix))
            return rawContent;

        int start = rawContent.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        int end = rawContent.LastIndexOf(']');
        if (start < rawContent.Length && end > start)
            return rawContent.Substring(start, end - start).TrimEnd("\r\n\\".ToCharArray());

        return rawContent;
    }

    public static string GetCorePrefix(short coreId) => coreId switch
    {
        0 => "A",
        1 => "B",
        2 => "D",
        _ => "U"
    };

    public static string BuildTimePrefix(int timestampMs, short sequence, string corePrefix)
    {
        int totalSeconds = timestampMs / 1000;
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;
        return $"[{corePrefix}-{sequence}] [{hours:D2}:{minutes:D2}:{seconds:D2}.{timestampMs % 1000 / 100}]";
    }
}
