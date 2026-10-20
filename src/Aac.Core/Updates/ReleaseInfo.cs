using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aac.Core.Updates;

/// <summary>
/// The newest public release, as GitHub's "latest release" answer describes it: its version, what changed (the
/// release notes) and where its installer is. Read only when someone presses Check for updates; nothing is ever
/// sent or checked by itself.
/// </summary>
public sealed partial record ReleaseInfo(Version Version, string Tag, string Notes, string? InstallerUrl, string PageUrl)
{
    public const string Repository = "Nelcyon/bettertalkopen";
    public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string InstallerName = "BetterTalk-Setup.exe";

    /// <summary>The full feature list (README) of that release, as plain text.</summary>
    public string ReadmeUrl => $"https://raw.githubusercontent.com/{Repository}/{Tag}/README.md";

    /// <summary>Reads GitHub's answer, or null when it is not a usable release.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagElement) || tagElement.GetString() is not { Length: > 0 } tag) return null;
            if (ParseVersion(tag) is not { } version) return null;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
            var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "";
            var page = root.TryGetProperty("html_url", out var html) && html.ValueKind == JsonValueKind.String ? html.GetString() ?? "" : "";
            string? installer = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var asset in assets.EnumerateArray())
                    if (asset.TryGetProperty("name", out var name) && string.Equals(name.GetString(), InstallerName, StringComparison.OrdinalIgnoreCase)
                        && asset.TryGetProperty("browser_download_url", out var url))
                        installer = url.GetString();
            return new ReleaseInfo(version, tag, notes.Replace("\r\n", "\n").Trim(), installer, page);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"v0.3.0", "0.3" or "0.3.0.0" → a version with at least three parts; null for anything else.</summary>
    public static Version? ParseVersion(string text)
    {
        var t = text.Trim().TrimStart('v', 'V');
        var dash = t.IndexOf('-');
        if (dash >= 0) t = t[..dash];
        if (!Version.TryParse(t.Contains('.') ? t : t + ".0", out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>True when this release is newer than the version running now (the fourth number is ignored).</summary>
    public bool IsNewerThan(Version current) => Version > new Version(current.Major, current.Minor, Math.Max(0, current.Build));

    /// <summary>
    /// Release notes or a README (Markdown) as plain readable text: no **, #, &gt; or `, bullets as •, links as their
    /// words, and the lines of each paragraph joined so the box wraps them itself.
    /// </summary>
    public static string PlainText(string markdown)
    {
        var output = new List<string>();
        var joinable = false; // the last line is ordinary paragraph text that the next one continues
        var inCode = false;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var l = raw.TrimEnd();
            if (l.TrimStart().StartsWith("```", StringComparison.Ordinal)) { inCode = !inCode; joinable = false; continue; }
            if (inCode) { output.Add(l); continue; }
            l = Quote().Replace(l, "");
            if (l.Trim().Length == 0 || l.Trim() == "---") { output.Add(""); joinable = false; continue; }
            var heading = HeadingMark().Match(l);
            var bullet = Bullet().Match(l);
            var numbered = Numbered().IsMatch(l);
            var tableRow = l.TrimStart().StartsWith('|');
            if (heading.Success) l = l[heading.Length..];
            else if (bullet.Success) l = new string(' ', bullet.Groups[1].Length) + "• " + l[bullet.Length..];
            l = Link().Replace(l, "$1").Replace("**", "").Replace("__", "").Replace("`", "");
            if (joinable && !heading.Success && !bullet.Success && !numbered && !tableRow)
                output[^1] += " " + l.Trim();
            else
                output.Add(l);
            joinable = !heading.Success && !tableRow;
        }
        // At most one blank line in a row.
        var text = string.Join("\n", output);
        while (text.Contains("\n\n\n", StringComparison.Ordinal)) text = text.Replace("\n\n\n", "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"^\s*>\s?")] private static partial Regex Quote();
    [GeneratedRegex(@"^\s*\d+\.\s")] private static partial Regex Numbered();
    [GeneratedRegex(@"^#{1,6}\s+")] private static partial Regex HeadingMark();
    [GeneratedRegex(@"^(\s*)[-*]\s+")] private static partial Regex Bullet();
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")] private static partial Regex Link();
}
