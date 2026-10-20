using System.Text.Json;
using Aac.Core.Layout;
using Aac.Core.Storage;

namespace Aac.Core.Tests;

public class AnchoringTests
{
    private static readonly PxRect Work = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(WindowCorner.TopLeft, 0, 0)]
    [InlineData(WindowCorner.TopRight, 1920 - 1100, 0)]
    [InlineData(WindowCorner.BottomLeft, 0, 1040 - 700)]
    [InlineData(WindowCorner.BottomRight, 1920 - 1100, 1040 - 700)]
    public void A_window_sits_flush_in_its_corner_of_the_work_area(WindowCorner corner, int x, int y)
    {
        Assert.Equal((x, y), Anchoring.Place(corner, Work, 1100, 700));
    }

    [Fact]
    public void A_second_screen_to_the_left_has_negative_coordinates_and_still_works()
    {
        var work = new PxRect(-2560, -300, 0, 1100);
        Assert.Equal((-2560 - 0 + 0, 1100 - 700), Anchoring.Place(WindowCorner.BottomLeft, work, 1000, 700));
        Assert.Equal((0 - 1000, -300), Anchoring.Place(WindowCorner.TopRight, work, 1000, 700));
    }

    [Theory]
    [InlineData(WindowCorner.TopLeft, 0, 0, true)]
    [InlineData(WindowCorner.TopLeft, 3, 3, true)]
    [InlineData(WindowCorner.TopLeft, 4, 0, false)]
    [InlineData(WindowCorner.TopRight, 1919, 0, true)]
    [InlineData(WindowCorner.TopRight, 1916, 3, true)]
    [InlineData(WindowCorner.TopRight, 1915, 0, false)]
    [InlineData(WindowCorner.BottomLeft, 0, 1079, true)]
    [InlineData(WindowCorner.BottomLeft, 0, 1075, false)]
    [InlineData(WindowCorner.BottomRight, 1919, 1079, true)]
    [InlineData(WindowCorner.BottomRight, 1920, 1079, false)] // one past the screen: not on it at all
    public void The_corner_is_a_small_square_on_the_chosen_screen(WindowCorner corner, int x, int y, bool inside)
    {
        var screen = new PxRect(0, 0, 1920, 1080);
        Assert.Equal(inside, Anchoring.InCorner(corner, screen, 3, x, y));
    }

    [Fact]
    public void The_corner_of_another_screen_does_not_count()
    {
        var screen = new PxRect(1920, 0, 3840, 1080);
        Assert.False(Anchoring.InCorner(WindowCorner.TopLeft, screen, 3, 0, 0));
        Assert.True(Anchoring.InCorner(WindowCorner.TopLeft, screen, 3, 1921, 2));
    }

    [Fact]
    public void Settings_default_to_the_top_left_of_the_main_screen_opening_from_the_corner()
    {
        var s = new AppSettings();
        Assert.Equal(WindowCorner.TopLeft, s.Anchor);
        Assert.Null(s.Screen);
        Assert.True(s.OpenFromCorner);
        Assert.False(s.StartWithWindows);
    }

    [Fact]
    public void An_older_settings_file_that_turned_the_corner_off_is_still_respected()
    {
        var s = JsonSerializer.Deserialize<AppSettings>("""{"OpenFromTopLeftCorner": false}""", SettingsStore.JsonOptions)!;
        Assert.False(s.OpenFromCorner);
        var written = JsonSerializer.Serialize(s, SettingsStore.JsonOptions);
        Assert.Contains("\"OpenFromCorner\": false", written);
        Assert.DoesNotContain("OpenFromTopLeftCorner", written);
    }

    [Fact]
    public void A_bottom_anchor_puts_the_drawer_rows_first()
    {
        var options = Aac.Core.Session.SessionOptions.From(new AppSettings { Anchor = WindowCorner.BottomRight });
        Assert.True(options.DrawerAbove);
        Assert.False(Aac.Core.Session.SessionOptions.From(new AppSettings()).DrawerAbove);
    }
}
