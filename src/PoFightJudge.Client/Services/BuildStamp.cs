using System.Reflection;

namespace PoFightJudge.Client.Services;

/// <summary>
/// Which build this is, as the day it was built (YYMMDD). The project file stamps it into the assembly at build
/// time (<c>AssemblyMetadata("BuildDate", …)</c>), so it is always the build's own and never typed by hand.
/// </summary>
public static class BuildStamp
{
    public static string Version { get; } =
        typeof(BuildStamp).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, "BuildDate", StringComparison.Ordinal))?.Value ?? "dev";
}
