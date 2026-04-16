namespace musicApp.Updater;

internal enum VersionBuild
{
    Portable,
    X64Installer,
    X86Installer,
    Arm64Installer,
}

internal static class VersionBuildExtensions
{
    public static bool TryParseFromFileContent(string? raw, out VersionBuild kind)
    {
        kind = VersionBuild.Portable;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var s = raw.Trim();
        if (string.Equals(s, "portable", StringComparison.OrdinalIgnoreCase))
        {
            kind = VersionBuild.Portable;
            return true;
        }
        if (string.Equals(s, "x64", StringComparison.OrdinalIgnoreCase))
        {
            kind = VersionBuild.X64Installer;
            return true;
        }
        if (string.Equals(s, "x86", StringComparison.OrdinalIgnoreCase))
        {
            kind = VersionBuild.X86Installer;
            return true;
        }
        if (string.Equals(s, "arm64", StringComparison.OrdinalIgnoreCase))
        {
            kind = VersionBuild.Arm64Installer;
            return true;
        }
        return false;
    }
}
