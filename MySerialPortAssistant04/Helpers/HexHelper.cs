using System.Text;

namespace MySerialPortAssistant04.Helpers;

/// <summary>
/// 十六进制显示工具。
/// </summary>
public static class HexHelper
{
    public static string ToHexString(byte[] packet, int offset, int length = 10)
    {
        if (packet == null || offset < 0 || offset + length > packet.Length)
            throw new ArgumentException("参数越界或数组为空");

        var sb = new StringBuilder(length * 3);
        for (int i = offset; i < offset + length; i++)
        {
            sb.Append(packet[i].ToString("X2"));
            if (i < offset + length - 1)
                sb.Append(' ');
        }
        return sb.ToString();
    }
}
