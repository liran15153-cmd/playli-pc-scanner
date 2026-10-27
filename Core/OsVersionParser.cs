using System.Globalization;

namespace Playli.PcScanner;

public static class OsVersionParser
{
    public static string WithBuildRevision(Version version, string? registryBuild, int? updateRevision)
    {
        var baseVersion = version.ToString(3);
        return updateRevision is >= 0 && registryBuild == version.Build.ToString(CultureInfo.InvariantCulture)
            ? baseVersion + "." + updateRevision.Value.ToString(CultureInfo.InvariantCulture)
            : baseVersion;
    }
}
