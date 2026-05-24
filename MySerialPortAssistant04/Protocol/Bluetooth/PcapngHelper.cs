using System.IO;
using System.Text;

namespace MySerialPortAssistant04.Protocol.Bluetooth;

/// <summary>
/// PCAPNG 格式封装，供 Wireshark 命名管道识别 Bluetooth HCI H4 数据包。
/// </summary>
public static class PcapngHelper
{
    private const uint BlockTypeShb = 0x0A0D0D0A;
    private const uint BlockTypeIdb = 0x00000001;
    private const uint BlockTypeEpb = 0x00000006;
    private const uint ByteOrderMagic = 0x1A2B3C4D;
    private const ushort LinkTypeBluetoothHciH4WithPhdr = 201;

    public static byte[] CreateSectionHeaderBlock()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(BlockTypeShb);
        long blockLengthPos = ms.Position;
        writer.Write(0);

        writer.Write(ByteOrderMagic);
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write(0xFFFFFFFFFFFFFFFF);

        writer.Write((ushort)0);
        writer.Write((ushort)0);

        while (ms.Position % 4 != 0)
            writer.Write((byte)0);

        int blockLength = (int)ms.Position + 4;
        ms.Position = blockLengthPos;
        writer.Write(blockLength);
        ms.Position = ms.Length;
        writer.Write(blockLength);

        return ms.ToArray();
    }

    public static byte[] CreateInterfaceDescriptionBlock()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(BlockTypeIdb);
        long blockLengthPos = ms.Position;
        writer.Write(0);

        writer.Write(LinkTypeBluetoothHciH4WithPhdr);
        writer.Write((ushort)0);
        writer.Write(0x0000FFFF);

        writer.Write((ushort)2);
        writer.Write((ushort)4);
        writer.Write(Encoding.ASCII.GetBytes("hci0"));

        writer.Write((ushort)9);
        writer.Write((ushort)1);
        writer.Write((byte)6);
        while (ms.Position % 4 != 0) writer.Write((byte)0);

        writer.Write((ushort)0);
        writer.Write((ushort)0);

        while (ms.Position % 4 != 0)
            writer.Write((byte)0);

        int blockLength = (int)ms.Position + 4;
        ms.Position = blockLengthPos;
        writer.Write(blockLength);
        ms.Position = ms.Length;
        writer.Write(blockLength);

        return ms.ToArray();
    }

    public static byte[] CreateEnhancedPacketBlock(byte[] hciData)
    {
        if (hciData == null || hciData.Length == 0)
            return Array.Empty<byte>();

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(BlockTypeEpb);
        long blockLengthPos = ms.Position;
        writer.Write(0);

        writer.Write(0);

        DateTime now = DateTime.UtcNow;
        long epochMicro = (long)(now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMicroseconds;
        uint tsHigh = (uint)(epochMicro >> 32);
        uint tsLow = (uint)(epochMicro & 0xFFFFFFFF);
        writer.Write(tsHigh);
        writer.Write(tsLow);

        byte[] pseudoHeader = new byte[4];
        byte[] fullPacket = pseudoHeader.Concat(hciData).ToArray();

        writer.Write((uint)fullPacket.Length);
        writer.Write((uint)fullPacket.Length);
        writer.Write(fullPacket);

        int padding = (4 - (fullPacket.Length % 4)) % 4;
        for (int i = 0; i < padding; i++)
            writer.Write((byte)0);

        writer.Write((ushort)0);
        writer.Write((ushort)0);

        int blockLength = (int)ms.Position + 4;
        ms.Position = blockLengthPos;
        writer.Write(blockLength);
        ms.Position = ms.Length;
        writer.Write(blockLength);

        return ms.ToArray();
    }

    public static byte[] CreateGlobalHeader()
    {
        var shb = CreateSectionHeaderBlock();
        var idb = CreateInterfaceDescriptionBlock();
        byte[] result = new byte[shb.Length + idb.Length];
        Buffer.BlockCopy(shb, 0, result, 0, shb.Length);
        Buffer.BlockCopy(idb, 0, result, shb.Length, idb.Length);
        return result;
    }

    public static byte[] CreatePacket(byte[] hciData) => CreateEnhancedPacketBlock(hciData);
}
