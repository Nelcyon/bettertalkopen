using System.Text.Json;
using Aac.Core.Layout;
using Aac.Core.Storage;

namespace Aac.Core.Tests;

/// <summary>"Arrange the panels": where each Itch panel is, and the file that remembers it.</summary>
public class LayoutTests
{
    [Fact]
    public void Everything_starts_beside_the_keyboard_at_its_usual_width()
    {
        var layout = new PanelLayout();
        Assert.True(layout.IsDefault);
        Assert.False(layout.IsDetached(PanelKind.Figure));
        Assert.False(layout.IsDetached(PanelKind.Move));
        Assert.Equal(560, layout.WidthOf(PanelKind.Figure, 560));
        Assert.Null(layout.FloatOf(PanelKind.Move));
    }

    [Fact]
    public void Widths_stay_within_bounds_and_put_everything_back_forgets_it_all()
    {
        var layout = new PanelLayout();
        layout.SetWidth(PanelKind.Figure, 560 + PanelLayout.WidthStep);
        Assert.Equal(620, layout.WidthOf(PanelKind.Figure, 560));
        layout.SetWidth(PanelKind.Figure, 10);
        Assert.Equal(PanelLayout.MinWidth, layout.WidthOf(PanelKind.Figure, 560));
        layout.SetWidth(PanelKind.Figure, 99999);
        Assert.Equal(PanelLayout.MaxWidth, layout.WidthOf(PanelKind.Figure, 560));
        Assert.False(layout.IsDefault);

        layout.SetDetached(PanelKind.Move, true);
        Assert.True(layout.IsDetached(PanelKind.Move));
        layout.SetFloat(PanelKind.Move, new PxRect(100, 200, 700, 500));
        Assert.Equal(new PxRect(100, 200, 700, 500), layout.FloatOf(PanelKind.Move));

        layout.PutEverythingBack();
        Assert.True(layout.IsDefault);
        Assert.False(layout.IsDetached(PanelKind.Move));
        Assert.Null(layout.FloatOf(PanelKind.Move));
        Assert.Equal(560, layout.WidthOf(PanelKind.Figure, 560));
    }

    [Fact]
    public void The_layout_file_round_trips_with_readable_names_and_a_missing_file_means_the_usual_arrangement()
    {
        var layout = new PanelLayout();
        layout.SetDetached(PanelKind.Figure, true);
        layout.SetFloat(PanelKind.Figure, new PxRect(10, 20, 410, 920));
        layout.SetWidth(PanelKind.Move, 720);

        var json = JsonSerializer.Serialize(layout, SettingsStore.JsonOptions);
        Assert.Contains("\"Figure\"", json);
        Assert.Contains("\"Detached\": true", json);
        var back = JsonSerializer.Deserialize<PanelLayout>(json, SettingsStore.JsonOptions)!;
        Assert.True(back.IsDetached(PanelKind.Figure));
        Assert.Equal(new PxRect(10, 20, 410, 920), back.FloatOf(PanelKind.Figure));
        Assert.Equal(720, back.WidthOf(PanelKind.Move, 600));

        var dir = Path.Combine(Path.GetTempPath(), "bettertalk-layout-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "layout.json");
        try
        {
            Assert.True(LayoutStore.Load(path).IsDefault);
            LayoutStore.Save(path, layout);
            Assert.True(LayoutStore.Load(path).IsDetached(PanelKind.Figure));
            File.WriteAllText(path, "{ not json");
            Assert.True(LayoutStore.Load(path).IsDefault);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

/// <summary>Move mode: a dragged panel snaps to the edges of the others and slides along them.</summary>
public class PanelSnapTests
{
    private static readonly PxRect Work = new(0, 0, 1920, 1040);
    private static readonly PxRect Keyboard = new(0, 0, 1000, 700);

    [Fact]
    public void A_panel_near_the_keyboard_snaps_beside_it_with_the_gap()
    {
        var moved = PanelSnap.Snap(new PxRect(1015, 100, 1415, 500), new[] { Keyboard }, Work, threshold: 20, gap: 8);
        Assert.Equal(1008, moved.Left);
        Assert.Equal(400, moved.Width);
        Assert.Equal(100, moved.Top); // the other axis follows the mouse: it slides along the edge
    }

    [Fact]
    public void Lines_up_with_the_top_too_when_close()
    {
        var moved = PanelSnap.Snap(new PxRect(1012, 12, 1412, 412), new[] { Keyboard }, Work, 20, 8);
        Assert.Equal((1008, 0), (moved.Left, moved.Top));
    }

    [Fact]
    public void Far_away_it_stays_where_it_was_dropped()
    {
        var at = new PxRect(1300, 300, 1600, 600);
        Assert.Equal(at, PanelSnap.Snap(at, new[] { Keyboard }, Work, 20, 8));
    }

    [Fact]
    public void A_panel_below_another_snaps_under_it()
    {
        var figure = new PxRect(1008, 0, 1600, 500);
        var moved = PanelSnap.Snap(new PxRect(1100, 515, 1500, 800), new[] { Keyboard, figure }, Work, 20, 8);
        Assert.Equal(508, moved.Top);
    }

    [Fact]
    public void Never_leaves_the_screen()
    {
        var moved = PanelSnap.Snap(new PxRect(1800, 900, 2200, 1200), Array.Empty<PxRect>(), Work, 20, 8);
        Assert.Equal((1520, 740), (moved.Left, moved.Top));
    }
}
