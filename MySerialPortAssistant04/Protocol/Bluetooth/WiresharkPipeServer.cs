using System.Collections.Concurrent;
using System.IO.Pipes;

namespace MySerialPortAssistant04.Protocol.Bluetooth;

/// <summary>
/// 命名管道服务，将 PCAPNG 流推送给 Wireshark（管道名：BluetoothHciToWireshark）。
/// </summary>
public class WiresharkPipeServer
{
    public const string PipeName = "BluetoothHciToWireshark";

    private NamedPipeServerStream? _pipe;
    private readonly ConcurrentQueue<byte[]> _queue = new();
    private readonly SemaphoreSlim _dataSemaphore = new(0);
    private Thread? _serverThread;
    private Thread? _sendThread;
    private volatile bool _running;
    private volatile bool _clientConnected;
    private bool _headerSent;

    public bool IsConnected => _clientConnected;
    public string LastError { get; private set; } = "";
    public int QueueCount => _queue.Count;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _serverThread = new Thread(ServerLoop) { IsBackground = true, Name = "WiresharkPipeServer" };
        _serverThread.Start();
    }

    public void Stop()
    {
        _running = false;
        _clientConnected = false;
        _headerSent = false;
        _dataSemaphore.Release(10);

        try { _pipe?.Close(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
    }

    public void EnqueuePacket(byte[] data)
    {
        if (!_running || data == null || data.Length == 0) return;

        while (_queue.Count > 500)
            _queue.TryDequeue(out _);

        _queue.Enqueue(data);
        _dataSemaphore.Release();
    }

    private void ServerLoop()
    {
        while (_running)
        {
            try
            {
                _pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough);

                LastError = "等待 Wireshark 连接...";
                _clientConnected = false;
                _headerSent = false;

                var connectTask = _pipe.WaitForConnectionAsync();
                var timeoutTask = Task.Delay(2000);
                Task.WhenAny(connectTask, timeoutTask).Wait();

                if (!connectTask.IsCompletedSuccessfully)
                {
                    Thread.Sleep(300);
                    continue;
                }

                _clientConnected = true;
                LastError = "已连接 ✅";

                _sendThread = new Thread(SendLoop) { IsBackground = true, Name = "WiresharkSend" };
                _sendThread.Start();

                while (_running && _pipe?.IsConnected == true)
                    Thread.Sleep(200);
            }
            catch (Exception ex)
            {
                LastError = $"错误: {ex.Message}";
                _clientConnected = false;
                _headerSent = false;
            }
            finally
            {
                _sendThread?.Join(500);
                try { _pipe?.Close(); } catch { }
                try { _pipe?.Dispose(); } catch { }
                _pipe = null;
            }
        }
    }

    private async void SendLoop()
    {
        try
        {
            while (_running && _pipe != null && _pipe.IsConnected)
            {
                if (!_dataSemaphore.Wait(10))
                    continue;

                if (!_headerSent)
                {
                    var header = PcapngHelper.CreateGlobalHeader();
                    await _pipe.WriteAsync(header, 0, header.Length);
                    _headerSent = true;
                }

                while (_queue.TryDequeue(out byte[]? pkt) && pkt != null)
                {
                    try
                    {
                        if (_pipe is { IsConnected: true })
                            await _pipe.WriteAsync(pkt, 0, pkt.Length);
                    }
                    catch
                    {
                        return;
                    }
                }
            }
        }
        catch
        {
            _clientConnected = false;
            LastError = "发送断开";
        }
    }
}
