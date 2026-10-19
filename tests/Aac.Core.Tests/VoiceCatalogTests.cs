using Aac.Core.Speech;

namespace Aac.Core.Tests;

public class VoiceCatalogTests
{
    [Fact]
    public void Every_natural_voice_has_a_unique_key_a_download_and_a_licence()
    {
        var all = VoiceCatalog.All.ToList();
        Assert.Equal(all.Count, all.Select(v => v.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var v in all)
        {
            Assert.NotNull(v.Pack);
            Assert.StartsWith("https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/", v.Pack!.Url);
            Assert.False(string.IsNullOrWhiteSpace(v.Pack.License));
            Assert.Same(v, VoiceCatalog.FindNatural(v.Key.ToUpperInvariant()));
        }
        // All the Kokoro voices share one download.
        Assert.Single(VoiceCatalog.Kokoro.Select(v => v.Pack!.Id).Distinct());
    }

    [Fact]
    public void Windows_voice_names_are_not_natural_voices()
    {
        Assert.Null(VoiceCatalog.FindNatural("Microsoft David Desktop"));
        Assert.Null(VoiceCatalog.FindNatural(null));
    }

    [Fact]
    public void The_speed_setting_maps_to_a_sensible_range()
    {
        Assert.Equal(1f, VoiceCatalog.SpeedFor(0));
        Assert.InRange(VoiceCatalog.SpeedFor(-10), 0.5f, 0.6f);
        Assert.InRange(VoiceCatalog.SpeedFor(10), 1.75f, 1.85f);
        Assert.Equal(VoiceCatalog.SpeedFor(10), VoiceCatalog.SpeedFor(99));
    }
}
