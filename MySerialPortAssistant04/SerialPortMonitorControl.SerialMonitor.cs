using System.IO;
using System.Text;
using System.Windows.Controls;
using MySerialPortAssistant04.Models;
using MySerialPortAssistant04.Protocol.Bluetooth;
using MySerialPortAssistant04.Services.Logging;
using RJCP.IO.Ports;

namespace MySerialPortAssistant04;

/// <summary>
/// 串口监控控件 — 串口读写、帧同步与 dbglog 匹配解析。
/// </summary>
public partial class SerialPortMonitorControl
{
    private const int BaudRate = 2_000_000;
    private static readonly byte[] Preamble = { 0xD0, 0xD2, 0xC5, 0xC2 };
    private const int MaxBufferSize = 10 * 1024 * 1024;
    private const int ReadBufferSize = 4 * 1024 * 1024;
    private const bool DebugSyncLoss = true;

    private void StartMonitor(string port)
    {
        try
        {
            StopMonitor();
            _serialPort = new SerialPortStream(port, BaudRate, 8, Parity.None, StopBits.One)
            {
                ReadBufferSize = ReadBufferSize,
                WriteBufferSize = 64 * 1024,
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = 100
            };

            _cts = new CancellationTokenSource();
            _serialPort.Open();
            _isMonitoring = true;
            _readTask = ReadSerialPortAsync(_cts.Token);

            if (CboHciSendMode.SelectedItem is ComboBoxItem item && item.Content?.ToString() == "UDP(Ellisys)")
            {
                _ellisysUdp.Open(TxtUdpTarget.Text);
                AppendLog($"✅ UDP 目标: {TxtUdpTarget.Text}");
            }

            Dispatcher.Invoke(() =>
            {
                BtnStartMonitor.IsEnabled = false;
                BtnStopMonitor.IsEnabled = true;
                CboSerialPort.IsEnabled = false;
                CboHciSendMode.IsEnabled = false;
                TxtUdpTarget.IsEnabled = false;
            });

            AppendLog($"✅ Started monitoring: {port} @ {BaudRate} bps");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Start failed: {ex.Message}");
        }
    }

    private void StopMonitor()
    {
        if (!_isMonitoring) return;
        _isMonitoring = false;

        try { _cts?.Cancel(); } catch { }

        if (_readTask != null)
        {
            try { _readTask.Wait(500); } catch { }
        }

        try
        {
            if (_serialPort?.IsOpen == true)
                _serialPort.Close();
        }
        catch { }

        try { _serialPort?.Dispose(); } catch { }
        try { _cts?.Dispose(); } catch { }

        _serialPort = null;
        _cts = null;
        _readTask = null;

        lock (_lockObj) _buffer.Clear();
        _ellisysUdp.Close();

        Dispatcher.BeginInvoke(() =>
        {
            BtnStartMonitor.IsEnabled = true;
            BtnStopMonitor.IsEnabled = false;
            CboSerialPort.IsEnabled = true;
            CboHciSendMode.IsEnabled = true;
            TxtUdpTarget.IsEnabled = true;
        });

        AppendLog("🔌 Stopped serial monitoring");
    }

    private async Task ReadSerialPortAsync(CancellationToken token)
    {
        byte[] buf = new byte[4096];
        while (!token.IsCancellationRequested && _serialPort != null && _serialPort.IsOpen)
        {
            try
            {
                int len = await _serialPort.ReadAsync(buf, 0, buf.Length, token);
                if (len <= 0) continue;

                bool overflow = false;
                lock (_lockObj)
                {
                    if (_buffer.Count + len > MaxBufferSize)
                    {
                        overflow = true;
                        _buffer.Clear();
                        _totalPacketsDropped += len;
                    }
                    else
                    {
                        _buffer.AddRange(buf.Take(len));
                        Interlocked.Increment(ref _totalPacketsReceived);
                    }
                }

                if (overflow)
                {
                    AppendLog("⚠️ Buffer overflow, cleared");
                    UpdateStats();
                    continue;
                }

                UpdateStats();
                ProcessBufferSafe(token);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { break; }
            catch (Exception ex)
            {
                AppendLog($"⚠️ Read exception: {ex.Message}");
                break;
            }
        }
    }

    private void ProcessBufferSafe(CancellationToken token)
    {
        while (true)
        {
            byte[]? packet = null;
            lock (_lockObj)
            {
                if (_buffer.Count < 12) break;

                bool match = true;
                for (int k = 0; k < 4; k++)
                {
                    if (_buffer[k] != Preamble[k])
                    {
                        match = false;
                        break;
                    }
                }

                if (!match)
                {
                    int next = -1;
                    for (int i = 1; i <= _buffer.Count - 4; i++)
                    {
                        bool m = true;
                        for (int k = 0; k < 4; k++)
                        {
                            if (_buffer[i + k] != Preamble[k])
                            {
                                m = false;
                                break;
                            }
                        }
                        if (m) { next = i; break; }
                    }

                    if (next == -1)
                    {
                        _totalPacketsDropped += _buffer.Count;
                        _buffer.Clear();
                        break;
                    }

                    _totalPacketsDropped += next;
                    _buffer.RemoveRange(0, next);
                    continue;
                }

                short len = (short)(_buffer[5] | (_buffer[6] << 8));
                int total = 4 + len;
                if (total < 12 || total > 4096)
                {
                    _buffer.RemoveAt(0);
                    _totalPacketsDropped++;
                    continue;
                }

                if (_buffer.Count < total) break;

                packet = new byte[total];
                _buffer.CopyTo(0, packet, 0, total);
                _buffer.RemoveRange(0, total);
            }

            if (packet != null)
                ParseAndMatchPacket(packet);
            else
                break;
        }
    }

    private void ParseAndMatchPacket(byte[] fullPacket)
    {
        if (fullPacket.Length < 12) return;

        byte type = fullPacket[7];
        ushort seq = BitConverter.ToUInt16(fullPacket, 9);

        if (type is 1 or 2)
        {
            if (_lastSequence != -1)
            {
                int expectedNext = (_lastSequence + 1) & 0xFFFF;
                if (expectedNext != seq)
                {
                    int gap = (seq - expectedNext) & 0xFFFF;
                    if (gap > 1 && DebugSyncLoss)
                    {
                        AppendLog($"⚠️ Seq gap (type 1/2): expected {expectedNext}, got {seq}, gap {gap}");
                        _sequenceGapCount++;
                    }
                }
            }
            _lastSequence = seq;
        }
        else if (type == 6)
        {
            if (_lastSequenceType6 != -1)
            {
                int expectedNext = (_lastSequenceType6 + 1) & 0xFFFF;
                if (expectedNext != seq)
                {
                    int gap = (seq - expectedNext) & 0xFFFF;
                    if (gap > 1 && DebugSyncLoss)
                    {
                        AppendLog($"⚠️ Seq gap (type 6): expected {expectedNext}, got {seq}, gap {gap}");
                        _sequenceGapCountType6++;
                    }
                }
            }
            _lastSequenceType6 = seq;
        }

        int offset = 12;
        if (offset + 4 > fullPacket.Length) return;

        int timestamp = BitConverter.ToInt32(fullPacket, offset);
        offset += 4;
        if (offset + 2 > fullPacket.Length) return;

        short coreIdSeqVer = BitConverter.ToInt16(fullPacket, offset);
        offset += 2;
        short coreId = (short)(coreIdSeqVer & 0x03);
        short dbgSequence = (short)((coreIdSeqVer & 0x0FFC) >> 2);

        if (offset + 2 > fullPacket.Length) return;
        short payloadLength = BitConverter.ToInt16(fullPacket, offset);
        offset += 2;

        if (offset + 1 > fullPacket.Length) return;
        byte lvlUseAddr = fullPacket[offset];
        byte useAddr = (byte)((lvlUseAddr >> 3) & 0x01);
        offset += 1;

        if (offset + 1 > fullPacket.Length) return;
        offset += 1; // moduleId

        string corePrefix = DbgLogFormatHelper.GetCorePrefix(coreId);
        string timePrefix = DbgLogFormatHelper.BuildTimePrefix(timestamp, dbgSequence, corePrefix);

        if (type == 1 && useAddr == 0x01)
        {
            if (offset + 4 > fullPacket.Length) return;
            int outAddr = BitConverter.ToInt32(fullPacket, offset);
            offset += 4;

            var parameters = new List<object>();
            int remainingBytes = fullPacket.Length - offset;
            int paramCount = Math.Min(payloadLength / 4, remainingBytes / 4);

            for (int i = 0; i < paramCount; i++)
            {
                if (offset + 4 > fullPacket.Length) break;
                uint paramValue = BitConverter.ToUInt32(fullPacket, offset);
                parameters.Add(paramValue);
                offset += 4;
            }

            if (_addressMap.TryGetValue((uint)outAddr, out LogEntry? entry))
            {
                string formatTemplate = DbgLogFormatHelper.ExtractFormatTemplate(entry.Content);
                try
                {
                    var converted = CFormatConverter.Convert(formatTemplate);
                    object[] finalParams = new object[parameters.Count];
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        uint raw = (uint)parameters[i];
                        if (converted.floatIndices.Contains(i))
                            finalParams[i] = BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);
                        else if (converted.signedIndices.Contains(i))
                            finalParams[i] = unchecked((int)raw);
                        else
                            finalParams[i] = raw;
                    }

                    string formatted = string.Format(converted.formatStr, finalParams)
                        .Replace("\\n", "").Replace("\\r", "").Trim();
                    AppendLog($"-{timePrefix}> {formatted}");
                    Interlocked.Increment(ref _totalPacketsParsed);
                    UpdateStats();
                }
                catch
                {
                    AppendLog($"⚠️ Format failed: {formatTemplate}");
                }
            }
        }
        else if (type == 2)
        {
            try
            {
                byte[] strBytes = new byte[payloadLength];
                Array.Copy(fullPacket, offset, strBytes, 0, payloadLength);
                int actualLen = Array.IndexOf(strBytes, (byte)0);
                if (actualLen == -1) actualLen = payloadLength;

                string raw = Encoding.UTF8.GetString(strBytes, 0, actualLen);
                var clean = new StringBuilder();
                foreach (char c in raw)
                {
                    if (c >= 32 || c is '\n' or '\r' or '\t')
                        clean.Append(c);
                }

                string msg = clean.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    AppendLog($"+{timePrefix}> {msg}");
                    Interlocked.Increment(ref _totalPacketsParsed);
                    UpdateStats();
                }
            }
            catch { }
        }
        else if (type == 6)
        {
            // ParseHciCommand(fullPacket, 13, timePrefix);
        }
    }
}
