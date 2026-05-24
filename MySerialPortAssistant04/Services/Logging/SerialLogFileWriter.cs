using System.IO;
using System.Text;

namespace MySerialPortAssistant04.Services.Logging;

/// <summary>
/// 串口监控运行时日志落盘，支持按大小自动轮转。
/// </summary>
public sealed class SerialLogFileWriter : IDisposable
{
    private const long MaxFileSizeBytes = 50 * 1024 * 1024;

    private readonly object _fileLock = new();
    private StreamWriter? _writer;
    private string _currentPath = "";
    private long _currentSize;
    private string _saveDirectory = "SerialLogs";
    private int _instanceId;

    public string? CurrentFilePath => string.IsNullOrEmpty(_currentPath) ? null : _currentPath;

    public void Initialize(string saveDirectory, int instanceId)
    {
        _saveDirectory = saveDirectory;
        _instanceId = instanceId;

        if (!Directory.Exists(_saveDirectory))
            Directory.CreateDirectory(_saveDirectory);

        CreateNewFile();
    }

    public void WriteLine(string message)
    {
        lock (_fileLock)
        {
            if (_writer == null) return;

            try
            {
                string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
                byte[] bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                _writer.BaseStream.Write(bytes, 0, bytes.Length);
                _currentSize += bytes.Length;
                RotateIfNeeded();
            }
            catch { }
        }
    }

    public void Close()
    {
        lock (_fileLock)
        {
            if (_writer == null) return;

            try
            {
                _writer.Flush();
                _writer.Close();
            }
            catch { }

            try { _writer.Dispose(); } catch { }
            _writer = null;
        }
    }

    public void Dispose() => Close();

    private void RotateIfNeeded()
    {
        if (_writer == null || _currentSize < MaxFileSizeBytes) return;

        try
        {
            _writer.Close();
            _writer.Dispose();
            CreateNewFile();
        }
        catch { }
    }

    private void CreateNewFile()
    {
        string fileName = $"SerialLog_{_instanceId}_{DateTime.Now:yyyyMMdd_HHmmssfff}.log";
        _currentPath = Path.Combine(_saveDirectory, fileName);
        _writer = new StreamWriter(
            new FileStream(_currentPath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096))
        {
            AutoFlush = true
        };
        _currentSize = 0;
    }
}
