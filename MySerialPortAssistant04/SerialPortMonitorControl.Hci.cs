using System.Text;
using System.Windows.Controls;
using MySerialPortAssistant04.Protocol.Bluetooth;
using MySerialPortAssistant04.Services.Hci;
using MySerialPortAssistant04.Services.Logging;

namespace MySerialPortAssistant04;

/// <summary>
/// 串口监控控件 — HCI 解析与转发（Wireshark / Ellisys）。
/// </summary>
public partial class SerialPortMonitorControl
{
    private void SendHciBySelectedMode(byte[] hciRaw)
    {
        if (hciRaw == null || hciRaw.Length == 0)
            return;

        if (CboHciSendMode.SelectedItem is not ComboBoxItem item)
            return;

        string? mode = item.Content?.ToString();
        if (mode == "命名管道(Wireshark)")
        {
            byte[] pcap = PcapngHelper.CreatePacket(hciRaw);
            _wiresharkPipe.EnqueuePacket(pcap);
        }
        else if (mode == "UDP(Ellisys)")
        {
            _ellisysUdp.SendHciPacket(hciRaw);
        }
    }

    private void ParseHciCommand(byte[] fullPacket, int payloadOffset, string timePrefix)
    {
        try
        {
            int remaining = fullPacket.Length - payloadOffset;
            const int crcLength = 4;
            int printLength = remaining >= crcLength ? remaining - crcLength : remaining;

            if (printLength > 0)
            {
                string payloadHex = BitConverter.ToString(fullPacket, payloadOffset, printLength).Replace("-", " ");
                AppendLog($"+{timePrefix}> [HCI_PAYLOAD] Raw: {payloadHex}");
            }
            else
            {
                AppendLog($"+{timePrefix}> [HCI_PAYLOAD] No data (only CRC or empty)");
            }
        }
        catch { }

        byte[] hciRaw = HciRawPacketBuilder.BuildHciRawPacket(fullPacket, payloadOffset);
        if (hciRaw.Length > 0)
            SendHciBySelectedMode(hciRaw);

        try
        {
            int remaining = fullPacket.Length - payloadOffset;
            const int crcLength = 4;
            int dataLength = remaining >= crcLength ? remaining - crcLength : remaining;

            if (dataLength < 3)
            {
                AppendLog($"+{timePrefix}> [HCI] Payload too short: {dataLength} bytes");
                return;
            }

            ushort opcode = BitConverter.ToUInt16(fullPacket, payloadOffset);
            byte ogf = (byte)((opcode >> 10) & 0x3F);
            ushort ocf = (ushort)(opcode & 0x03FF);
            byte paramLen = fullPacket[payloadOffset + 2];

            string ogfName = HciCommandNames.GetOgfName(ogf);
            string cmdName = HciCommandNames.GetCommandName(ogf, ocf);

            var paramHex = new StringBuilder();
            int paramStart = payloadOffset + 3;
            int actualParamBytes = Math.Min(paramLen, dataLength - 3);

            for (int i = 0; i < actualParamBytes; i++)
                paramHex.AppendFormat("{0:X2} ", fullPacket[paramStart + i]);

            string logMsg = $"+{timePrefix}> [HCI_CMD] {cmdName} (OGF:0x{ogf:X2}/{ogfName}, OCF:0x{ocf:X3}, Op:0x{opcode:X4}, Len:{paramLen})";
            if (paramHex.Length > 0)
                logMsg += $" Params:[{paramHex.ToString().Trim()}]";

            AppendLog(logMsg);
            Interlocked.Increment(ref _totalPacketsParsed);
            UpdateStats();
        }
        catch (Exception ex)
        {
            AppendLog($"+{timePrefix}> [HCI] Parse error: {ex.Message}");
        }
    }
}
