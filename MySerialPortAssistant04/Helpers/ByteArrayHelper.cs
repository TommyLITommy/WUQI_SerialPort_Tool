namespace MySerialPortAssistant04.Helpers;

/// <summary>
/// 字节数组搜索等通用工具。
/// </summary>
public static class ByteArrayHelper
{
    /// <summary>
    /// 在 haystack 中从 start 起查找 needle 首次出现的位置，未找到返回 -1。
    /// </summary>
    public static int IndexOf(byte[] haystack, byte[] needle, int start = 0)
    {
        for (int i = start; i <= haystack.Length - needle.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    found = false;
                    break;
                }
            }
            if (found) return i;
        }
        return -1;
    }
}
