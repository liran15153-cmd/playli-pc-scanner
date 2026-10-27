using System.Text.RegularExpressions;

namespace Playli.PcScanner;

public static class GpuMemoryClassifier
{
    public static string FromReportedName(string? name)
    {
        // Classification of identified product families, not a measured UMA
        // architecture query, usable budget or performance estimate.
        var value=Regex.Replace(name??"",@"\((?:R|TM)\)|[®™]","",RegexOptions.IgnoreCase);
        if(Regex.IsMatch(value,@"\bIntel\s+(?:HD|UHD)\b",RegexOptions.IgnoreCase))return "unified";
        if(Regex.IsMatch(value,@"\b(?:RTX|GTX|GTS|GT)\s*\d{3,4}|\bGeForce\s+\d{4}\s+(?:GT|GS|Ultra)\b|\b(?:RX|R9|R7)\s*\d{3,4}|\bVega\s*(?:56|64)\b|\bArc\s*[AB]\d{3}",RegexOptions.IgnoreCase))return "dedicated";
        return "unknown";
    }
}
