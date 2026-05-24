using System.IO;
using System.Net;
using System.Net.Sockets;

namespace MySerialPortAssistant04.Protocol.Bluetooth;

/// <summary>
/// 向 Ellisys 蓝牙分析仪发送 HCI 注入数据（UDP，格式与 Python 脚本一致）。
/// </summary>
public class EllisysUdpSender
{
    private Socket? _udpSocket;
    private IPEndPoint? _remoteEndPoint;
    private volatile bool _isOpen;

    private const byte InjectedHciPacketTypeCommand = 0x01;
    private const byte InjectedHciPacketTypeAclFromHost = 0x02;
    private const byte InjectedHciPacketTypeScoFromHost = 0x03;
    private const byte InjectedHciPacketTypeEvent = 0x84;

    public bool IsConnected => _isOpen;
    public string LastError { get; private set; } = "";

    public bool Open(string ipPort)
    {
        try
        {
            Close();
            var parts = ipPort.Split(':');
            string ip = parts[0];
            int port = int.Parse(parts[1]);

            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
            _udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _isOpen = true;
            LastError = "UDP 已就绪";
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"UDP 打开失败: {ex.Message}";
            _isOpen = false;
            return false;
        }
    }

    public void Close()
    {
        _isOpen = false;
        try { _udpSocket?.Close(); } catch { }
        try { _udpSocket?.Dispose(); } catch { }
        _udpSocket = null;
        _remoteEndPoint = null;
    }

    public void SendHciPacket(byte[] hciRaw)
    {
        if (!_isOpen || _udpSocket == null || _remoteEndPoint == null || hciRaw == null || hciRaw.Length < 1)
            return;

        try
        {
            byte type = hciRaw[0];
            byte ellisysType = type switch
            {
                0x01 => InjectedHciPacketTypeCommand,
                0x02 => InjectedHciPacketTypeAclFromHost,
                0x03 => InjectedHciPacketTypeScoFromHost,
                0x04 => InjectedHciPacketTypeEvent,
                _ => throw new Exception($"不支持的HCI类型: {type:X2}")
            };

            byte[] packet = GenerateEllisysPacket(ellisysType, hciRaw.Skip(1).ToArray());
            _udpSocket.SendTo(packet, _remoteEndPoint);
        }
        catch { }
    }

    private static byte[] GenerateEllisysPacket(byte packetType, byte[] data)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write((ushort)0x0002);
        w.Write((byte)0x01);

        w.Write((byte)0x02);
        DateTime now = DateTime.Now;
        w.Write((ushort)now.Year);
        w.Write((byte)now.Month);
        w.Write((byte)now.Day);

        long ns = (long)(now.TimeOfDay.TotalMilliseconds * 1_000_000);
        byte[] nsBytes = new byte[6];
        Buffer.BlockCopy(BitConverter.GetBytes(ns), 0, nsBytes, 0, 6);
        w.Write(nsBytes);

        w.Write((byte)0x80);
        w.Write((uint)12000000);

        w.Write((byte)0x81);
        w.Write(packetType);
        w.Write((byte)0x82);
        w.Write(data);

        return ms.ToArray();
    }
}
