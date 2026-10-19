using Aac.Core.Prediction;
using Aac.Core.Storage;

namespace Aac.Core.Tests;

public class ButtonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qs-buttons-" + Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_dir, "buttons.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void The_first_start_moves_the_buttons_out_of_settings_into_their_own_file()
    {
        var fromSettings = PhraseLibrary.CreateDefault();
        fromSettings.PinnedPhrases = new List<string> { "Water", "Lights" };
        var loaded = ButtonStore.Load(File_, fromSettings, out var problem);
        Assert.Null(problem);
        Assert.Equal(new[] { "Water", "Lights" }, loaded.Pinned);
        Assert.True(File.Exists(File_));
    }

    [Fact]
    public void Saved_buttons_come_back_after_a_restart_and_win_over_the_settings_copy()
    {
        var buttons = PhraseLibrary.CreateDefault();
        buttons.PinnedPhrases = new List<string> { "Water", "Lights", "Blanket", "Hang on... let me think" };
        buttons.QuickPhrases.Add("Turn on the fan.");
        ButtonStore.Save(File_, buttons);

        var oldSettingsCopy = PhraseLibrary.CreateDefault();
        var loaded = ButtonStore.Load(File_, oldSettingsCopy, out _);
        Assert.Equal(buttons.Pinned, loaded.Pinned);
        Assert.Contains("Turn on the fan.", loaded.QuickPhrases);
    }

    [Fact]
    public void An_unreadable_file_is_kept_aside_and_the_settings_copy_is_used()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "{ not json");
        var fallback = PhraseLibrary.CreateDefault();
        var loaded = ButtonStore.Load(File_, fallback, out var problem);
        Assert.Same(fallback, loaded);
        Assert.NotNull(problem);
        Assert.True(File.Exists(Path.Combine(_dir, "buttons.unreadable.json")));
    }
}
