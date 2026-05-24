namespace MySerialPortAssistant04.Protocol.Bluetooth;

/// <summary>
/// 从串口完整帧中提取 HCI H4 原始数据包。
/// </summary>
public static class HciRawPacketBuilder
{
    public static byte[] BuildHciRawPacket(byte[] fullPacket, int payloadOffset)
    {
        int remaining = fullPacket.Length - payloadOffset;
        if (remaining < 1) return Array.Empty<byte>();

        byte h4Type = fullPacket[payloadOffset];

        switch (h4Type)
        {
            case 0x01:
                if (remaining < 4) return Array.Empty<byte>();
                int cmdParamLen = fullPacket[payloadOffset + 3];
                int cmdTotalLen = 1 + 3 + cmdParamLen;
                if (remaining >= cmdTotalLen)
                    return fullPacket.Skip(payloadOffset).Take(cmdTotalLen).ToArray();
                break;

            case 0x02:
                if (remaining < 5) return Array.Empty<byte>();
                int aclDataLen = BitConverter.ToUInt16(fullPacket, payloadOffset + 3);
                int aclTotalLen = 1 + 4 + aclDataLen;
                if (remaining >= aclTotalLen)
                    return fullPacket.Skip(payloadOffset).Take(aclTotalLen).ToArray();
                break;

            case 0x03:
                if (remaining < 4) return Array.Empty<byte>();
                int scoDataLen = fullPacket[payloadOffset + 3];
                int scoTotalLen = 1 + 3 + scoDataLen;
                if (remaining >= scoTotalLen)
                    return fullPacket.Skip(payloadOffset).Take(scoTotalLen).ToArray();
                break;

            case 0x04:
                if (remaining < 3) return Array.Empty<byte>();
                int evtParamLen = fullPacket[payloadOffset + 2];
                int evtTotalLen = 1 + 2 + evtParamLen;
                if (remaining >= evtTotalLen)
                    return fullPacket.Skip(payloadOffset).Take(evtTotalLen).ToArray();
                break;
        }

        return Array.Empty<byte>();
    }
}
