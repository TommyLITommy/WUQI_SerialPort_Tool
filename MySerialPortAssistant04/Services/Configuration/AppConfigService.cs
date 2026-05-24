namespace MySerialPortAssistant04.Services.Configuration;

/// <summary>
/// 主窗口持久化配置（日志文件路径等），存储于程序目录 config.txt。
/// </summary>
public sealed class AppConfigService
{
    private readonly string _configFilePath;

    public AppConfigService()
    {
        _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
    }

    /// <summary>
    /// 读取上次保存的日志文件路径；文件不存在或配置缺失时返回 null。
    /// </summary>
    public string? LoadLogFilePath()
    {
        try
        {
            if (!File.Exists(_configFilePath))
                return null;

            foreach (var line in File.ReadAllLines(_configFilePath))
            {
                if (!line.StartsWith("LogFilePath=", StringComparison.OrdinalIgnoreCase))
                    continue;

                var path = line["LogFilePath=".Length..].Trim();
                return string.IsNullOrEmpty(path) ? null : path;
            }
        }
        catch { }

        return null;
    }

    public void SaveLogFilePath(string logFilePath)
    {
        try
        {
            var lines = new[]
            {
                $"LogFilePath={logFilePath}",
                $"LastSaveTime={DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            };
            File.WriteAllLines(_configFilePath, lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
        }
    }
}
