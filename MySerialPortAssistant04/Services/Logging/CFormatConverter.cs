using System.Text.RegularExpressions;

namespace MySerialPortAssistant04.Services.Logging;

/// <summary>
/// 将 C 风格 printf 格式串转换为 .NET string.Format 模板。
/// </summary>
public static class CFormatConverter
{
    private static readonly Regex CFormatRegex = new(
        @"%([0-9\.lLhHzZ]*)([dxXufFeEgGcs%p])",
        RegexOptions.Compiled);

    public static (string formatStr, List<int> floatIndices, List<int> signedIndices) Convert(string cFormat)
    {
        if (string.IsNullOrEmpty(cFormat))
            return (cFormat, new List<int>(), new List<int>());

        int idx = 0;
        var floatIndices = new List<int>();
        var signedIndices = new List<int>();

        string result = CFormatRegex.Replace(cFormat, m =>
        {
            string t = m.Groups[2].Value;
            if (t == "%") return "%";

            string spec = "";
            bool sign = false;
            switch (t)
            {
                case "d": sign = true; spec = "D"; break;
                case "x":
                case "X": spec = t; break;
                case "p": spec = "X8"; break;
                case "f":
                case "F": floatIndices.Add(idx); spec = "F6"; break;
            }

            if (sign) signedIndices.Add(idx);
            idx++;
            return $"{{{idx - 1}{(string.IsNullOrEmpty(spec) ? "" : $":{spec}")}}}";
        });

        return (result, floatIndices, signedIndices);
    }
}
