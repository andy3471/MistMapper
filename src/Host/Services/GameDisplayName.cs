using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MistMapper.Host.Services;

/// <summary>Resolves a human-friendly game name from exe metadata / window title.</summary>
public static partial class GameDisplayName
{
    public static string Resolve(string? exePath, string? exeName, string? windowTitle = null)
    {
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                if (IsUseful(info.FileDescription))
                    return Clean(info.FileDescription!);
                if (IsUseful(info.ProductName))
                    return Clean(info.ProductName!);
            }
            catch
            {
                // access denied / missing file
            }
        }

        if (IsUseful(windowTitle))
            return CleanWindowTitle(windowTitle!);

        if (!string.IsNullOrWhiteSpace(exeName))
            return Path.GetFileNameWithoutExtension(exeName);

        return "Game";
    }

    static bool IsUseful(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals("unknown", StringComparison.OrdinalIgnoreCase);

    static string Clean(string value) => value.Trim();

    static string CleanWindowTitle(string title)
    {
        var t = title.Trim();
        t = EngineSuffixRegex().Replace(t, "");
        t = WhitespaceRegex().Replace(t, " ").Trim();
        return string.IsNullOrEmpty(t) ? title.Trim() : t;
    }

    [GeneratedRegex(@"\s+[-–—]\s+(Unreal Engine|Unity|CryEngine|Godot).*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EngineSuffixRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
