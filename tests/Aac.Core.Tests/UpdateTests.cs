using Aac.Core.Updates;

namespace Aac.Core.Tests;

/// <summary>Check for updates: reading GitHub's latest-release answer and telling whether it is newer.</summary>
public class UpdateTests
{
    private const string Answer = """
        {"tag_name":"v0.4.0","draft":false,"html_url":"https://github.com/Nelcyon/bettertalkopen/releases/tag/v0.4.0",
         "body":"## What's new\r\n- Move mode\r\n\r\n---\r\nTo install: download it\r\n",
         "assets":[{"name":"notes.txt","browser_download_url":"https://x/notes.txt"},
                   {"name":"BetterTalk-Setup.exe","browser_download_url":"https://x/BetterTalk-Setup.exe"}]}
        """;

    [Fact]
    public void Reads_the_version_notes_and_installer()
    {
        var r = ReleaseInfo.Parse(Answer)!;
        Assert.Equal(new Version(0, 4, 0), r.Version);
        Assert.Equal("## What's new\n- Move mode", r.Notes);
        Assert.Equal("https://x/BetterTalk-Setup.exe", r.InstallerUrl);
        Assert.EndsWith("/v0.4.0/README.md", r.ReadmeUrl);
    }

    [Fact]
    public void Newer_only_when_the_number_is_higher()
    {
        var r = ReleaseInfo.Parse(Answer)!;
        Assert.True(r.IsNewerThan(new Version(0, 3, 0, 0)));
        Assert.False(r.IsNewerThan(new Version(0, 4, 0, 0)));
        Assert.False(r.IsNewerThan(new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("0.3", "0.3.0")]
    [InlineData("v2.0.0-beta", "2.0.0")]
    public void Versions_are_read_from_tags(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), ReleaseInfo.ParseVersion(tag));

    [Fact]
    public void Junk_is_not_a_release()
    {
        Assert.Null(ReleaseInfo.Parse("not json"));
        Assert.Null(ReleaseInfo.Parse("{\"message\":\"Not Found\"}"));
        Assert.Null(ReleaseInfo.Parse("{\"tag_name\":\"latest\"}"));
    }

    [Fact]
    public void Notes_are_shown_as_plain_text()
    {
        var plain = ReleaseInfo.PlainText("## 0.4.0\n\n- **Move mode.** See [the guide](https://x/y).\n  - nested `code`\n---\nDownload **BetterTalk-Setup.exe**");
        Assert.Equal("0.4.0\n\n• Move mode. See the guide.\n  • nested code\n\nDownload BetterTalk-Setup.exe", plain);
    }

    [Fact]
    public void Paragraph_lines_are_joined_and_quotes_lose_their_marker()
    {
        var plain = ReleaseInfo.PlainText("> Status: prototype,\n> nearly done.\n\nBetterTalk lets you control a computer with a\nQuadStick or a switch.\n- one\n  continued\n- two");
        Assert.Equal("Status: prototype, nearly done.\n\nBetterTalk lets you control a computer with a QuadStick or a switch.\n• one continued\n• two", plain);
    }
}
