using System.Text;
using MySerialPortAssistant04.Helpers;
using MySerialPortAssistant04.Models;

namespace MySerialPortAssistant04.Services.Logging;

/// <summary>
/// 解析 dbglog_table.txt，建立地址到格式字符串的索引。
/// </summary>
public sealed class LogTableParseResult
{
    public Dictionary<uint, LogEntry> AddressMap { get; } = new();
    public List<List<LogEntry>> CoreGroups { get; } = new();
}

public static class LogTableParser
{
    private static readonly byte[][] CoreStartMarkers =
    {
        Encoding.ASCII.GetBytes("dcore_dbglog_start\r\n"),
        Encoding.ASCII.GetBytes("bcore_dbglog_start\r\n"),
        Encoding.ASCII.GetBytes("acore_dbglog_start\r\n")
    };

    private static readonly string[] CoreNames = { "dcore", "bcore", "acore" };

    public static LogTableParseResult Parse(byte[] fileData)
    {
        var result = new LogTableParseResult();
        for (int i = 0; i < 3; i++)
            result.CoreGroups.Add(new List<LogEntry>());

        for (int coreIndex = 0; coreIndex < 3; coreIndex++)
        {
            var marker = CoreStartMarkers[coreIndex];
            var coreName = CoreNames[coreIndex];
            int searchStart = 0;

            while (true)
            {
                int pos = ByteArrayHelper.IndexOf(fileData, marker, searchStart);
                if (pos == -1) break;

                int header = pos + marker.Length;
                if (header + 16 > fileData.Length)
                {
                    searchStart = pos + 1;
                    continue;
                }

                uint addrStart = BitConverter.ToUInt32(fileData, header);
                uint addrEnd = BitConverter.ToUInt32(fileData, header + 4);
                uint logStart = BitConverter.ToUInt32(fileData, header + 8);
                uint logEnd = BitConverter.ToUInt32(fileData, header + 12);

                if (addrEnd > addrStart && (addrEnd - addrStart) % 4 == 0)
                {
                    int count = (int)((addrEnd - addrStart) / 4);
                    int cursor = header + 16;
                    var addresses = new List<uint>();

                    for (int j = 0; j < count; j++)
                    {
                        if (cursor + 4 > fileData.Length) break;
                        addresses.Add(BitConverter.ToUInt32(fileData, cursor));
                        cursor += 4;
                    }

                    int dataBase = cursor;
                    int entryIndex = 0;

                    foreach (uint addr in addresses)
                    {
                        long relative = addr - logStart;
                        long absolute = dataBase + relative;
                        string value = "<Invalid>";

                        if (relative >= 0 && absolute < fileData.Length)
                        {
                            int maxLen = Math.Min(500, fileData.Length - (int)absolute);
                            int nullEnd = -1;
                            for (int k = 0; k < maxLen; k++)
                            {
                                if (fileData[absolute + k] == 0)
                                {
                                    nullEnd = k;
                                    break;
                                }
                            }
                            if (nullEnd == -1) nullEnd = maxLen;

                            byte[] bytes = new byte[nullEnd];
                            Array.Copy(fileData, absolute, bytes, 0, nullEnd);
                            value = Encoding.UTF8.GetString(bytes).Replace("\r", "").Replace("\n", "\\n");
                        }

                        uint pointer = addrStart + (uint)(4 * entryIndex);
                        var entry = new LogEntry
                        {
                            CoreName = coreName,
                            PointerOfAddress = pointer,
                            Address = addr,
                            FileOffset = absolute,
                            Content = value
                        };

                        if (!result.AddressMap.ContainsKey(pointer))
                            result.AddressMap.Add(pointer, entry);

                        result.CoreGroups[coreIndex].Add(entry);
                        entryIndex++;
                    }
                }

                searchStart = pos + 1;
            }
        }

        return result;
    }
}
