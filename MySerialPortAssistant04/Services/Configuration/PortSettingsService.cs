using System.Configuration;

namespace MySerialPortAssistant04.Services.Configuration;

/// <summary>
/// 每个串口监控控件记住上次使用的 COM 口（App.config AppSettings）。
/// </summary>
public sealed class PortSettingsService
{
    private readonly string _settingsKey;

    public PortSettingsService(string instanceKey) => _settingsKey = instanceKey;

    public string? LoadLastPort()
    {
        try
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            return config.AppSettings.Settings[_settingsKey]?.Value;
        }
        catch
        {
            return null;
        }
    }

    public void SaveLastPort(string portName)
    {
        try
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            if (config.AppSettings.Settings[_settingsKey] == null)
                config.AppSettings.Settings.Add(_settingsKey, portName);
            else
                config.AppSettings.Settings[_settingsKey].Value = portName;

            config.Save(ConfigurationSaveMode.Modified);
        }
        catch { }
    }
}
