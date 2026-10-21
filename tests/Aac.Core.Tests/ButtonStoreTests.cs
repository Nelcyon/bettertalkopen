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

    private const string OldUntouchedFile = """
        {
          "PinnedPhrases": [ "Help!", "I need the bathroom.", "I'm in pain.", "Yes.", "No." ],
          "QuickPhrases": [ "Yes.", "No.", "Wait.", "Stop.", "I need help.", "Please repeat that.", "That's not what I meant.",
                            "Give me a moment.", "I'm uncomfortable.", "I need to reposition." ],
          "Categories": [ { "Name": "Needs", "Phrases": [ "I need water." ] } ],
          "SpokenListVersion": 1
        }
        """;

    [Fact]
    public void An_update_gives_never_changed_buttons_the_new_starting_buttons_and_writes_them_into_the_file()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, OldUntouchedFile);
        var loaded = ButtonStore.Load(File_, PhraseLibrary.CreateDefault(), out var problem);
        Assert.Null(problem);
        Assert.Equal(PhraseLibrary.DefaultPinnedPhrases, loaded.Pinned);
        Assert.Equal(PhraseLibrary.DefaultQuickPhrases, loaded.QuickPhrases);
        Assert.Equal(new[] { "I need water." }, loaded.Categories.Single().Phrases); // phrase pages are untouched

        // Written out in full, so a later version's starting buttons can never change them again.
        var onDisk = System.Text.Json.JsonSerializer.Deserialize<PhraseLibrary>(File.ReadAllText(File_), SettingsStore.JsonOptions)!;
        Assert.Equal(PhraseLibrary.DefaultPinnedPhrases, onDisk.PinnedPhrases);
        Assert.Equal(2, onDisk.ButtonsVersion);
        Assert.False(onDisk.Upgrade());
        Assert.Equal(OldUntouchedFile, File.ReadAllText(Path.Combine(_dir, "buttons.before-upgrade.json")));
    }

    [Fact]
    public void An_update_never_changes_buttons_someone_has_changed()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """{ "PinnedPhrases": [ "Water", "Lights" ], "QuickPhrases": [ "Fan on" ], "Categories": [] }""");
        var loaded = ButtonStore.Load(File_, PhraseLibrary.CreateDefault(), out _);
        Assert.Equal(new[] { "Water", "Lights" }, loaded.Pinned);
        Assert.Equal(new[] { "Fan on" }, loaded.QuickPhrases);

        // Even a red row set to exactly the old starting buttons stays when the quick row was changed.
        File.WriteAllText(File_, """{ "PinnedPhrases": [ "Help!", "I need the bathroom.", "I'm in pain.", "Yes.", "No." ], "QuickPhrases": [ "Fan on" ], "Categories": [] }""");
        loaded = ButtonStore.Load(File_, PhraseLibrary.CreateDefault(), out _);
        Assert.Equal("Help!", loaded.Pinned[0]);
        Assert.Equal(new[] { "Fan on" }, loaded.QuickPhrases);
    }
}
