using System.Reflection;

namespace Claude.Core;

// Reads the build version stamped by Directory.Build.props (SemVer + git
// short-hash + optional -dirty). Surface this in every app's UI so a stale or
// uncommitted binary is immediately visible. See
// ~/.claude/notes/idioms_dotnet_version_stamp.md
public static class BuildInfo
{
    public static string Version { get; } = ReadFor(Assembly.GetEntryAssembly());

    public static string VersionFor(Assembly? assembly) => ReadFor(assembly);

    static string ReadFor(Assembly? assembly) => Normalize(
        assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    // .NET may append "+<sourcerevision>" garbage when SourceRevisionId leaks
    // through despite our opt-out; keep "<version>+<hash>[-dirty]" intact and
    // drop anything after a second '+'.
    public static string Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return "unknown";
        var firstPlus = informationalVersion.IndexOf('+');
        if (firstPlus < 0) return informationalVersion;
        var secondPlus = informationalVersion.IndexOf('+', firstPlus + 1);
        return secondPlus < 0 ? informationalVersion : informationalVersion[..secondPlus];
    }

    // The git portion of a stamped version: the "<hash>[-dirty]" (or "nogit")
    // after the first '+'. This is the freshness signal — two assemblies built
    // from the same commit share it even when their SemVer prefixes differ (the
    // MAUI app derives its prefix from ApplicationDisplayVersion, so MegaSchoen.dll
    // is "1.0+<hash>" while every library is "0.1.0+<hash>"). Returns the whole
    // string when there is no '+' (e.g. "missing"/"unknown") so those still compare.
    public static string BuildStamp(string normalizedVersion)
    {
        var plus = normalizedVersion.IndexOf('+');
        return plus < 0 ? normalizedVersion : normalizedVersion[(plus + 1)..];
    }

    // Returns the InformationalVersion stamped into another assembly file (e.g. the
    // embedded ClaudeHookBridge.dll) without loading it into the running process.
    public static string VersionOfFile(string assemblyPath)
    {
        if (!File.Exists(assemblyPath)) return "missing";
        try
        {
            // FileVersionInfo.ProductVersion carries InformationalVersion for SDK builds.
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(assemblyPath);
            return Normalize(info.ProductVersion);
        }
        catch
        {
            return "unreadable";
        }
    }
}
