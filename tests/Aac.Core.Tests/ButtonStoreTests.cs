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
    public void A_pre_035_file_with_never_changed_buttons_gets_the_035_buttons_once_and_writes_them_into_the_file()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, OldUntouchedFile);
        var loaded = ButtonStore.Load(File_, PhraseLibrary.CreateDefault(), out var problem);
        Assert.Null(problem);
        Assert.Equal(PhraseLibrary.UpgradePinned035, loaded.Pinned);
        Assert.Equal(PhraseLibrary.UpgradeQuick035, loaded.QuickPhrases);
        Assert.Equal(new[] { "I need water." }, loaded.Categories.Single().Phrases); // phrase pages are untouched

        // Written out in full, so a later version's starting buttons can never change them again.
        var onDisk = System.Text.Json.JsonSerializer.Deserialize<PhraseLibrary>(File.ReadAllText(File_), SettingsStore.JsonOptions)!;
        Assert.Equal(PhraseLibrary.UpgradePinned035, onDisk.PinnedPhrases);
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

    private static PhraseLibrary OnDisk(string path) =>
        System.Text.Json.JsonSerializer.Deserialize<PhraseLibrary>(File.ReadAllText(path), SettingsStore.JsonOptions)!;

    [Fact]
    public void A_fresh_install_starts_with_the_plain_starting_buttons()
    {
        // No buttons.json yet: the buttons come from a new settings file.
        var loaded = ButtonStore.Load(File_, new AppSettings().Phrases, out var problem);
        Assert.Null(problem);
        Assert.Equal(new[] { "Help!", "I need the bathroom.", "I'm in pain.", "Yes.", "No." }, loaded.Pinned);
        Assert.Equal("Yes.", loaded.QuickPhrases[0]);
        Assert.Equal(10, loaded.QuickPhrases.Count);
        Assert.DoesNotContain(PhraseLibrary.UpgradePinned035[0], loaded.Pinned);

        var onDisk = OnDisk(File_);
        Assert.Equal(PhraseLibrary.DefaultPinnedPhrases, onDisk.PinnedPhrases);
        Assert.Equal(PhraseLibrary.DefaultQuickPhrases, onDisk.QuickPhrases);
        Assert.Equal(2, onDisk.ButtonsVersion);
        Assert.False(File.Exists(Path.Combine(_dir, "buttons.before-upgrade.json")));
    }

    [Fact]
    public void A_file_written_by_035_to_037_is_never_changed_by_an_update()
    {
        // What 0.3.5 to 0.3.7 wrote: every row out in full, ButtonsVersion 2.
        var written = new PhraseLibrary
        {
            PinnedPhrases = PhraseLibrary.UpgradePinned035.ToList(),
            QuickPhrases = PhraseLibrary.UpgradeQuick035.ToList(),
            SpokenPhrases = PhraseLibrary.DefaultSpokenPhrases.ToList(),
            SpokenListVersion = 1,
            ButtonsVersion = 2,
            Categories = { new() { Name = "Needs", Phrases = { "I need water." } } },
        };
        ButtonStore.Save(File_, written);
        var before = File.ReadAllText(File_);

        for (var start = 0; start < 3; start++)
        {
            var loaded = ButtonStore.Load(File_, PhraseLibrary.CreateDefault(), out var problem);
            Assert.Null(problem);
            Assert.Equal(PhraseLibrary.UpgradePinned035, loaded.Pinned);
            Assert.Equal(PhraseLibrary.UpgradeQuick035, loaded.QuickPhrases);
        }
        Assert.Equal(before, File.ReadAllText(File_));
        Assert.False(File.Exists(Path.Combine(_dir, "buttons.before-upgrade.json")));
    }

    [Fact]
    public void A_current_file_keeps_its_buttons_even_when_they_are_the_old_starting_buttons_or_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """{ "PinnedPhrases": [ "Help!", "I need the bathroom.", "I'm in pain.", "Yes.", "No." ], "QuickPhrases": [], "Categories": [], "SpokenListVersion": 1, "ButtonsVersion": 2 }""");
        var loaded = ButtonStore.Load(File_, PhraseLibrary.CreateDefault(), out _);
        Assert.Equal(PhraseLibrary.DefaultPinnedPhrases, loaded.Pinned);
        Assert.Empty(loaded.QuickPhrases);

        File.WriteAllText(File_, """{ "PinnedPhrases": [ "Water" ], "QuickPhrases": [ "Fan on" ], "Categories": [], "SpokenListVersion": 1, "ButtonsVersion": 2 }""");
        loaded = ButtonStore.Load(File_, PhraseLibrary.CreateDefault(), out _);
        Assert.Equal(new[] { "Water" }, loaded.Pinned);
        Assert.Equal(new[] { "Fan on" }, loaded.QuickPhrases);
    }

    [Fact]
    public void A_pre_035_file_with_its_own_quick_buttons_keeps_them_and_only_a_missing_red_row_is_filled()
    {
        Directory.CreateDirectory(_dir);
        const string old = """{ "QuickPhrases": [ "Fan on", "Lights off" ], "Categories": [ { "Name": "Mine", "Phrases": [ "Hello" ] } ] }""";
        File.WriteAllText(File_, old);
        var loaded = ButtonStore.Load(File_, PhraseLibrary.CreateDefault(), out _);
        Assert.Equal(new[] { "Fan on", "Lights off" }, loaded.QuickPhrases);
        Assert.Equal("Hello", loaded.Categories.Single().Phrases.Single());
        // A red row that was never written out showed the 0.3.5 buttons in 0.3.5 to 0.3.7, so it keeps showing them.
        Assert.Equal(PhraseLibrary.UpgradePinned035, loaded.Pinned);
        Assert.Equal(PhraseLibrary.UpgradePinned035, OnDisk(File_).PinnedPhrases);
        Assert.Equal(old, File.ReadAllText(Path.Combine(_dir, "buttons.before-upgrade.json")));
    }

    [Fact]
    public void The_one_off_upgrade_runs_once_and_keeps_the_first_copy_of_the_old_file()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, OldUntouchedFile);
        ButtonStore.Load(File_, PhraseLibrary.CreateDefault(), out _);

        // The person then changes a button; later starts and updates keep it and the first copy stays.
        var mine = OnDisk(File_);
        mine.PinnedPhrases![0] = "Water";
        ButtonStore.Save(File_, mine);
        var afterEdit = File.ReadAllText(File_);
        for (var start = 0; start < 2; start++)
            Assert.Equal("Water", ButtonStore.Load(File_, PhraseLibrary.CreateDefault(), out _).Pinned[0]);
        Assert.Equal(afterEdit, File.ReadAllText(File_));
        Assert.Equal(OldUntouchedFile, File.ReadAllText(Path.Combine(_dir, "buttons.before-upgrade.json")));
    }

    [Fact]
    public void A_buttons_file_loaded_on_purpose_is_kept_exactly_even_without_a_version()
    {
        // An exported file from before ButtonsVersion existed, holding the old starting buttons.
        var loaded = System.Text.Json.JsonSerializer.Deserialize<PhraseLibrary>(OldUntouchedFile, SettingsStore.JsonOptions)!;
        loaded.KeepAsLoaded();
        ButtonStore.Save(File_, loaded);

        var next = ButtonStore.Load(File_, PhraseLibrary.CreateDefault(), out _);
        Assert.Equal(PhraseLibrary.DefaultPinnedPhrases, next.Pinned);
        Assert.Equal(PhraseLibrary.DefaultQuickPhrases, next.QuickPhrases);
        Assert.False(File.Exists(Path.Combine(_dir, "buttons.before-upgrade.json")));
    }
}
