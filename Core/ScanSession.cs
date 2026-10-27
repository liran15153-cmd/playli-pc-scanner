using System.Text.RegularExpressions;

namespace Playli.PcScanner;

public static class ScanSession
{
    public static bool IsToken(string? value)=>value is not null&&Regex.IsMatch(value,"^[a-f0-9]{64}$",RegexOptions.CultureInvariant);
    public static string? TokenFromExecutable(string path)
    {
        var name=Path.GetFileName(path.Replace('\\','/'));
        var match=Regex.Match(name,@"^PLAYLI-PC-Check-([a-f0-9]{64})(?: \([1-9]\d{0,2}\))?\.exe$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        return match.Success?match.Groups[1].Value.ToLowerInvariant():null;
    }
}
