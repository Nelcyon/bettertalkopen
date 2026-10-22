using System.IO;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using Aac.Core.Input;
using Aac.Core.Layout;
using Aac.Core.Prediction;
using Aac.Core.Speech;
using Aac.Core.Storage;

namespace Aac.App;

/// <summary>
/// The everyday settings for a caregiver, opened from the tray icon. Saving writes settings.json
/// (keeping every setting not shown here) and restarts the app so all of it takes effect.
/// </summary>
public partial class SettingsWindow : Window
{
    private const string DefaultVoice = "(Windows default voice)";
    private readonly AppSettings _settings;
    private readonly Action _restart;

    private readonly Action _editButtons;
    private readonly Action<bool> _setUnlocked;
    private readonly Action _putPanelsBack;
    private readonly Action _setupGuide;
    private readonly Action _quit;

    public SettingsWindow(AppSettings current, Action restart, Action editButtons, Action<bool> setUnlocked, bool unlocked, Action putPanelsBack, Action setupGuide, Action quit)
    {
        InitializeComponent();
        // Wide enough for the whole QuadStick card and every tab heading in one row, but never bigger than the screen.
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, work.Width);
        Width = Math.Min(Width, work.Width);
        MinHeight = Math.Min(MinHeight, work.Height);
        Height = Math.Min(Height, work.Height);
        _restart = restart;
        _editButtons = editButtons;
        _setUnlocked = setUnlocked;
        LockUi.IsChecked = !unlocked;
        _putPanelsBack = putPanelsBack;
        _setupGuide = setupGuide;
        _quit = quit;
        // Start from what is on disk so hand edits made while the app was running are not lost.
        var onDisk = SettingsStore.Load(AppPaths.Settings, out var problem);
        _settings = File.Exists(AppPaths.Settings) && problem is null ? onDisk : current;
        // Button words changed on screen (edit mode) live in the running app; never let Save put back older ones.
        _settings.Phrases = current.Phrases;
        // Keys added in newer versions are not in an older settings file yet: show (and save) their defaults.
        foreach (var (key, action) in Aac.Core.Input.KeyMap.DefaultBindings)
            if (!_settings.KeyBindings.ContainsKey(key) && !_settings.KeyBindings.Values.Contains(action, StringComparer.OrdinalIgnoreCase))
                _settings.KeyBindings[key] = action;

        FillVoices();

        Rate.Value = _settings.Speech.Rate;
        Volume.Value = _settings.Speech.Volume;
        TextSize.Value = _settings.FontSize;
        SpeakWhen.SelectedIndex = _settings.Speech.SpeakEachWord ? 1 : 0;
        Highlight.SelectedItem = Highlight.Items.Cast<System.Windows.Controls.ComboBoxItem>().First(i => (string)i.Tag == _settings.SelectionHighlight.ToString());
        FillKeyRows();
        _card = new Dictionary<string, CardCell>(_settings.QuadStickCard);
        BuildCard();
        ShowUpdateState();
        Closed += (_, _) => _updateCancel.Cancel();
        Debounce.Value = Math.Clamp(_settings.Input.DebounceMs, 0, 1000);
        RepeatDelay.Value = Math.Clamp(_settings.Input.DeleteRepeatDelayMs, 200, 1500);
        RepeatInterval.Value = Math.Clamp(_settings.Input.DeleteRepeatIntervalMs, 30, 300);
        SuggestionOrder.SelectedIndex = _settings.SuggestionsFromMiddle ? 0 : 1;
        FillScreens();
        CornerChoice.SelectedItem = CornerChoice.Items.Cast<System.Windows.Controls.ComboBoxItem>().First(i => (string)i.Tag == _settings.Anchor.ToString());
        var corner = _settings.OpenFromCorner ? _settings.CornerTrigger.ToString() : "Off";
        Corner.SelectedItem = Corner.Items.Cast<System.Windows.Controls.ComboBoxItem>().First(i => (string)i.Tag == corner);
        CornerSize.Value = Math.Clamp(_settings.CornerPixels, 1, 40);
        CornerRest.Value = Math.Clamp(Math.Round(_settings.CornerDwellMs / 100.0) / 10.0, 0.1, 3);
        StartWithWindows.IsChecked = _settings.StartWithWindows;
        StartHidden.IsChecked = _settings.StartHidden;
        LockSize.IsChecked = _settings.LockWindowSize;
        AutoHide.IsChecked = _settings.AutoHideWhenMouseLeaves;
        AutoHideSeconds.Value = Math.Clamp(Math.Round(_settings.AutoHideAfterMs / 1000.0), 1, 10);
        AlwaysOnTop.IsChecked = _settings.AlwaysOnTop;
        QuickSpeak.IsChecked = _settings.Speech.QuickPhrasesSpeakImmediately;
        Learn.IsChecked = _settings.Privacy.LearnFromSpokenMessages;
        Pinned.Text = string.Join(Environment.NewLine, _settings.Phrases.Pinned);
        Quick.Text = string.Join(Environment.NewLine, _settings.Phrases.QuickPhrases);
        SpokenWords.Text = string.Join(Environment.NewLine, _settings.Phrases.Spoken);
        PhraseCategories.Text = string.Join(Environment.NewLine + Environment.NewLine,
            _settings.Phrases.Categories.Select(c => "[" + c.Name + "]" + Environment.NewLine + string.Join(Environment.NewLine, c.Phrases)));
        SpokenLookChoice.SelectedItem = SpokenLookChoice.Items.Cast<System.Windows.Controls.ComboBoxItem>().First(i => (string)i.Tag == _settings.SpokenButtonLook.ToString());
    }

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    /// <summary>"[Name]" lines start a category; the lines under it are its phrases.</summary>
    private static List<PhraseCategory> ParseCategories(string text)
    {
        var result = new List<PhraseCategory>();
        PhraseCategory? current = null;
        foreach (var line in Lines(text))
        {
            if (line.StartsWith('['))
            {
                current = new PhraseCategory { Name = line.Trim('[', ']', ' ') };
                result.Add(current);
            }
            else
            {
                if (current is null) { current = new PhraseCategory { Name = "Phrases" }; result.Add(current); }
                current.Phrases.Add(line);
            }
        }
        return result.Where(c => c.Name.Length > 0).ToList();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var chosen = SelectedVoiceKey;
        if (VoiceCatalog.FindNatural(chosen) is { Pack: { } pack } natural && !VoicePacks.IsInstalled(pack))
        {
            // A natural voice is downloaded before it is saved (joining a download already under way),
            // so the app never starts without its voice.
            if (!await Download(natural)) return;
        }
        _settings.Speech.VoiceName = chosen;
        _settings.Speech.Rate = (int)Rate.Value;
        _settings.Speech.Volume = (int)Volume.Value;
        _settings.FontSize = TextSize.Value;
        _settings.Speech.SpeakEachWord = SpeakWhen.SelectedIndex == 1;
        if (Enum.TryParse<Aac.Core.Input.FocusHighlight>((string)((System.Windows.Controls.ComboBoxItem)Highlight.SelectedItem).Tag, out var highlight))
            _settings.SelectionHighlight = highlight;
        foreach (var (box, info) in _keyBoxes) SetKey(info.Name, box.Text);
        _settings.QuadStickCard = _card;
        _settings.Hotkeys = _hotkeyBoxes.Where(b => b.Box.Text.Length > 0).ToDictionary(b => b.Box.Text, b => b.Info.Name);
        _settings.Input.DebounceMs = (int)Debounce.Value;
        _settings.Input.DeleteRepeatDelayMs = (int)RepeatDelay.Value;
        _settings.Input.DeleteRepeatIntervalMs = (int)RepeatInterval.Value;
        _settings.SuggestionsFromMiddle = SuggestionOrder.SelectedIndex != 1;
        _settings.Screen = (ScreenChoice.SelectedItem as ScreenRow)?.Key;
        if (Enum.TryParse<WindowCorner>((string)((System.Windows.Controls.ComboBoxItem)CornerChoice.SelectedItem).Tag, out var anchor)) _settings.Anchor = anchor;
        var cornerChoice = (string)((System.Windows.Controls.ComboBoxItem)Corner.SelectedItem).Tag;
        _settings.OpenFromCorner = cornerChoice != "Off";
        if (Enum.TryParse<CornerTrigger>(cornerChoice, out var trigger)) _settings.CornerTrigger = trigger;
        _settings.CornerPixels = (int)CornerSize.Value;
        _settings.CornerDwellMs = (int)Math.Round(CornerRest.Value * 1000);
        _settings.StartWithWindows = StartWithWindows.IsChecked == true;
        _settings.StartHidden = StartHidden.IsChecked == true;
        _settings.LockWindowSize = LockSize.IsChecked == true;
        _settings.AutoHideWhenMouseLeaves = AutoHide.IsChecked == true;
        _settings.AutoHideAfterMs = (int)AutoHideSeconds.Value * 1000;
        _settings.AlwaysOnTop = AlwaysOnTop.IsChecked == true;
        _settings.Speech.QuickPhrasesSpeakImmediately = QuickSpeak.IsChecked == true;
        _settings.Privacy.LearnFromSpokenMessages = Learn.IsChecked == true;
        _settings.Phrases.PinnedPhrases = Lines(Pinned.Text);
        var quick = Lines(Quick.Text);
        if (quick.Count > 0) _settings.Phrases.QuickPhrases = quick;
        _settings.Phrases.SpokenPhrases = Lines(SpokenWords.Text);
        var categories = ParseCategories(PhraseCategories.Text);
        if (categories.Count > 0) _settings.Phrases.Categories = categories;
        if (Enum.TryParse<SpokenLook>((string)((System.Windows.Controls.ComboBoxItem)SpokenLookChoice.SelectedItem).Tag, out var look)) _settings.SpokenButtonLook = look;

        try
        {
            SettingsStore.Save(AppPaths.Settings, _settings);
            ButtonStore.Save(AppPaths.Buttons, _settings.Phrases);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Settings could not be saved: {ex.Message}", "BetterTalk", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Close();
        _restart();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- voice ----------

    private const string Sample = "Hello. This is how I will sound when I talk to you.";
    private CancellationTokenSource? _downloading;
    private VoicePack? _downloadingPack;
    private Task<bool>? _download;
    private ISpeechEngine? _tester;
    private string? _testerKey;
    private SpeechSynthesizer? _windowsTester;
    private long _sampleId;
    private bool _voicesFilled;
    /// <summary>Waits a moment after the choice changes, so stepping through the list with the keyboard does not start a download for every voice passed.</summary>
    private readonly System.Windows.Threading.DispatcherTimer _previewDelay = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>
    /// The voice list: the Windows voices, then every free natural voice under its group's heading (each
    /// marked as downloaded or with its download size). Each entry's Tag is the value saved in settings
    /// (null for the Windows default). Picking a voice downloads it if needed and says a sample.
    /// </summary>
    private void FillVoices()
    {
        Voice.Items.Add(new VoiceRow(null, DefaultVoice));
        using (var synth = new SpeechSynthesizer())
            foreach (var v in synth.GetInstalledVoices().Where(v => v.Enabled))
                Voice.Items.Add(new VoiceRow(v.VoiceInfo.Name, $"{v.VoiceInfo.Name} (Windows voice, robotic)"));
        string? group = null;
        foreach (var v in VoiceCatalog.All)
        {
            if (v.Group != group) Voice.Items.Add(new VoiceRow(null, group = v.Group) { IsHeading = true });
            Voice.Items.Add(new VoiceRow(v.Key, "", v));
        }
        RefreshVoiceRows();

        var current = _settings.Speech.VoiceName;
        Voice.SelectedItem = VoiceRows.FirstOrDefault(r => !r.IsHeading && string.Equals(r.Key, current, StringComparison.OrdinalIgnoreCase))
            ?? Voice.Items[0];
        _previewDelay.Tick += (_, _) => { _previewDelay.Stop(); _ = Preview(); };
        _voicesFilled = true;
        ShowVoiceInfo();
    }

    private IEnumerable<VoiceRow> VoiceRows => Voice.Items.OfType<VoiceRow>();

    /// <summary>Marks which natural voices are downloaded (green, with a delete button) and shows the others' size.</summary>
    private void RefreshVoiceRows()
    {
        foreach (var row in VoiceRows)
        {
            if (row.Choice is not { Pack: { } pack } v) continue;
            row.Installed = VoicePacks.IsInstalled(pack);
            row.Label = $"{v.Name}: {v.Description}" + (row.Installed ? "  \u2713 downloaded" : $"  ({pack.SizeMb:0} MB)");
        }
    }

    private string? SelectedVoiceKey => (Voice.SelectedItem as VoiceRow)?.Key;

    /// <summary>
    /// The delete button beside a downloaded voice: removes its download (after asking), without choosing it.
    /// Voices that share one download (Kokoro, Kitten, the sets) go together, so the question names them.
    /// </summary>
    private void DeleteVoice_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true; // a press on the button must not also pick the voice (which would download it again)
        if ((sender as FrameworkElement)?.DataContext is not VoiceRow { Choice: { Pack: { } pack } voice }) return;
        var sharing = VoiceCatalog.All.Where(v => v.Pack == pack).ToList();
        var what = sharing.Count > 1
            ? $"the download shared by {sharing.Count} voices ({string.Join(", ", sharing.Take(6).Select(v => v.Name))}{(sharing.Count > 6 ? ", ..." : "")})"
            : $"the {voice.Name} voice";
        var inUse = VoiceCatalog.FindNatural(_settings.Speech.VoiceName)?.Pack == pack
            ? "\n\nThe app is using this voice now; after deleting it, it will use the Windows voice until you choose another." : "";
        if (MessageBox.Show(this, $"Delete {what} from this computer ({pack.SizeMb:0} MB)? It can be downloaded again any time.{inUse}",
                "Delete voice", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        // The test voice may have the files open.
        if (_tester is NaturalSpeechEngine { Voice.Pack: var testerPack } && testerPack == pack)
        {
            (_tester as IDisposable)?.Dispose();
            _tester = null;
            _testerKey = null;
        }
        try
        {
            VoicePacks.Delete(pack);
            if (VoiceCatalog.FindNatural(_settings.Speech.VoiceName)?.Pack == pack) _settings.Speech.VoiceName = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The voice could not be deleted ({ex.Message}). It may be in use: restart the app with another voice chosen, then try again.",
                "Delete voice", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshVoiceRows();
        ShowVoiceInfo();
    }

    /// <summary>Picking a voice downloads it (if needed) and says a sample in it.</summary>
    private void Voice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_voicesFilled) return;
        ShowVoiceInfo();
        _previewDelay.Stop();
        _previewDelay.Start();
    }

    private void ShowVoiceInfo()
    {
        if (!_voicesFilled || _download is not null) return; // the download shows its own progress
        var natural = VoiceCatalog.FindNatural(SelectedVoiceKey);
        if (natural?.Pack is not { } pack)
        {
            VoiceInfo.Text = "Windows voices are already installed and start instantly, but sound robotic.";
            return;
        }
        var installed = VoicePacks.IsInstalled(pack);
        VoiceInfo.Text = (installed ? "Downloaded; runs on this computer, no internet needed. " : $"Downloading it now ({pack.SizeMb:0} MB, once). ")
            + $"Licence: {pack.License}. From: {pack.Source}.";
    }

    /// <summary>
    /// Downloads a natural voice with a progress bar; true when it is ready. Asking for the voice already
    /// downloading joins that download; asking for a different one stops the first.
    /// </summary>
    private async Task<bool> Download(VoiceChoice voice)
    {
        if (voice.Pack is not { } pack) return false;
        if (VoicePacks.IsInstalled(pack)) return true;
        if (_download is not null)
        {
            if (_downloadingPack == pack) return await _download;
            _downloading?.Cancel();
            try { await _download; } catch (Exception) { }
        }
        _downloadingPack = pack;
        _download = DownloadNow(voice, pack);
        try { return await _download; }
        finally
        {
            if (_downloadingPack == pack) { _download = null; _downloadingPack = null; }
            ShowVoiceInfo();
        }
    }

    private async Task<bool> DownloadNow(VoiceChoice voice, VoicePack pack)
    {
        using var cancel = _downloading = new CancellationTokenSource();
        VoiceProgress.Value = 0;
        VoiceProgress.Visibility = Visibility.Visible;
        VoiceInfo.Text = $"Downloading {voice.Name} ({pack.SizeMb:0} MB, once)…";
        try
        {
            await VoicePacks.InstallAsync(pack, new Progress<double>(p =>
            {
                if (_downloadingPack != pack) return;
                VoiceProgress.Value = p;
                if (p >= 1) VoiceInfo.Text = $"Unpacking {voice.Name}…";
            }), cancel.Token);
            // Every voice sharing this download is now ready too.
            RefreshVoiceRows();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The {voice.Name} voice could not be downloaded: {ex.Message}", "BetterTalk", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            if (_downloading == cancel) _downloading = null;
            if (_downloadingPack == pack) VoiceProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void VoiceTest_Click(object sender, RoutedEventArgs e) => await Preview();

    /// <summary>Says a sample sentence in the chosen voice, at the chosen speed and volume (downloading it first if needed).</summary>
    private async Task Preview()
    {
        StopTest();
        var key = SelectedVoiceKey;
        var speech = new SpeechSettings { Rate = (int)Rate.Value, Volume = (int)Volume.Value };
        if (VoiceCatalog.FindNatural(key) is not { Pack: not null } natural)
        {
            _windowsTester = new SpeechSynthesizer();
            _windowsTester.SetOutputToDefaultAudioDevice();
            if (key is not null) try { _windowsTester.SelectVoice(key); } catch (ArgumentException) { }
            _windowsTester.Rate = speech.Rate;
            _windowsTester.Volume = speech.Volume;
            _windowsTester.SpeakAsync(Sample);
            return;
        }
        if (!await Download(natural) || SelectedVoiceKey != key) return; // the user may have picked another meanwhile
        // Keep the loaded voice while the window is open, so hearing it again is instant.
        var testerKey = $"{key}|{speech.Rate}|{speech.Volume}";
        if (_tester is null || _testerKey != testerKey)
        {
            (_tester as IDisposable)?.Dispose();
            var engine = new NaturalSpeechEngine(natural, speech);
            _tester = engine;
            _testerKey = testerKey;
            VoiceInfo.Text = $"Loading {natural.Name}…";
            var problem = await Task.Run(() => engine.WaitUntilLoaded(out var why) ? null : why);
            ShowVoiceInfo();
            if (problem is not null)
            {
                MessageBox.Show(this, $"The voice could not be loaded: {problem}", "BetterTalk", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (SelectedVoiceKey != key) return;
        }
        _tester.Speak(new Utterance(++_sampleId, Sample, Sample, UtteranceKind.Message));
    }

    private void StopTest()
    {
        _tester?.CancelAll();
        _windowsTester?.SpeakAsyncCancelAll();
        _windowsTester?.Dispose();
        _windowsTester = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewDelay.Stop();
        _downloading?.Cancel();
        StopTest();
        (_tester as IDisposable)?.Dispose();
        base.OnClosed(e);
    }

    // ---------- screen ----------

    /// <summary>One screen in the list; Key is what settings saves (null for the main screen).</summary>
    private sealed record ScreenRow(string Label, string? Key);

    /// <summary>Lists the screens Windows knows, the main one first, and selects the one in the settings (or the main one if it is unplugged).</summary>
    private void FillScreens()
    {
        var main = Screens.MainScreen();
        foreach (var s in Screens.All()) ScreenChoice.Items.Add(new ScreenRow(s.Describe(main), s.IsMain ? null : s.DeviceName));
        var chosen = Screens.Choose(_settings.Screen);
        ScreenChoice.SelectedItem = ScreenChoice.Items.Cast<ScreenRow>().First(r => (r.Key ?? main.DeviceName).Equals(chosen.IsMain ? main.DeviceName : chosen.DeviceName, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- hotkeys and keys ----------

    private readonly List<(TextBox Box, ActionInfo Info)> _hotkeyBoxes = new();
    private readonly List<(TextBox Box, ActionInfo Info)> _keyBoxes = new();

    /// <summary>One row per action: its name and a box that takes the next key pressed in it.</summary>
    private void FillKeyRows()
    {
        AddKeyRows(HotkeyRows, ActionCatalog.Hotkeys, _hotkeyBoxes, HotkeyBox_PreviewKeyDown);
        AddKeyRows(KeyRows, ActionCatalog.Keys.Where(k => k.Group == ActionCatalog.OpenGroup), _keyBoxes, KeyBox_PreviewKeyDown, KeyBox_PreviewMouseDown);
        AddKeyRows(FigureKeyRows, ActionCatalog.Keys.Where(k => k.Group == ActionCatalog.FigureGroup), _keyBoxes, KeyBox_PreviewKeyDown, KeyBox_PreviewMouseDown);
        foreach (var (box, info) in _hotkeyBoxes)
            box.Text = _settings.Hotkeys.FirstOrDefault(kv => string.Equals(kv.Value, info.Name, StringComparison.OrdinalIgnoreCase)).Key ?? "";
        foreach (var (box, info) in _keyBoxes) box.Text = KeyFor(info.Name) ?? "";
    }

    private static void AddKeyRows(Grid grid, IEnumerable<ActionInfo> actions, List<(TextBox, ActionInfo)> boxes,
        System.Windows.Input.KeyEventHandler onKey, System.Windows.Input.MouseButtonEventHandler? onMouse = null)
    {
        grid.ColumnDefinitions.Clear();
        // One action per row: the name on the left (it wraps), a same-size key box on the right, so nothing is cut off.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var i = 0;
        foreach (var info in actions)
        {
            var row = i;
            var col = 0;
            if (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition());
            var label = new TextBlock { Text = info.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, col);
            grid.Children.Add(label);
            var box = new TextBox { Margin = new Thickness(4), Width = 200, IsReadOnly = true, Tag = info,
                ToolTip = onMouse is null ? "Click here, then press the key you want. Backspace empties it."
                    : "Click here, then press the key you want, or click a mouse button other than the left one in it. Backspace empties it." };
            box.PreviewKeyDown += onKey;
            if (onMouse is not null)
            {
                box.PreviewMouseDown += onMouse;
                box.ContextMenuOpening += (_, e) => e.Handled = true; // right click is a key choice here, not the copy/paste menu
            }
            Grid.SetRow(box, row);
            Grid.SetColumn(box, col + 1);
            grid.Children.Add(box);
            boxes.Add((box, info));
            i++;
        }
    }

    private string? KeyFor(string action) =>
        _settings.KeyBindings.FirstOrDefault(kv => string.Equals(kv.Value, action, StringComparison.OrdinalIgnoreCase)).Key;

    /// <summary>Binds one key to the action, replacing whatever that key or action had before.</summary>
    private void SetKey(string action, string key)
    {
        foreach (var old in _settings.KeyBindings.Where(kv => string.Equals(kv.Value, action, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
            _settings.KeyBindings.Remove(old);
        if (key.Length > 0)
        {
            _settings.KeyBindings.Remove(key);
            _settings.KeyBindings[key] = action;
        }
    }

    internal static bool IsModifierKey(System.Windows.Input.Key key) => key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
        or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
        or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin;

    internal static HotkeyModifiers HeldModifiers()
    {
        var held = System.Windows.Input.Keyboard.Modifiers;
        var mods = HotkeyModifiers.None;
        if (held.HasFlag(System.Windows.Input.ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
        if (held.HasFlag(System.Windows.Input.ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (held.HasFlag(System.Windows.Input.ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (held.HasFlag(System.Windows.Input.ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;
        return mods;
    }

    /// <summary>A system-wide hotkey box: the next key, with the Ctrl/Alt/Shift/Win held with it, becomes its hotkey.</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox box || e.Key == System.Windows.Input.Key.Tab) return;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if (IsModifierKey(key)) return; // wait for the key that goes with it
        var mods = HeldModifiers();
        if (key == System.Windows.Input.Key.Back && mods == HotkeyModifiers.None) { box.Text = ""; HotkeyNote.Text = ""; return; }
        var spec = HotkeySpec.Format(mods, Aac.Core.Input.KeyMap.Normalize(key.ToString()));
        if (!HotkeySpec.TryParse(spec, out _, out _, out var problem)) { HotkeyNote.Text = problem; return; }
        HotkeyNote.Text = TakeFromOthers(_hotkeyBoxes, box, spec);
        box.Text = spec;
    }

    /// <summary>An in-app key box: the next key pressed becomes its key (one key, no Ctrl/Alt/Shift).</summary>
    private void KeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox box || e.Key == System.Windows.Input.Key.Tab) return;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if (IsModifierKey(key)) { KeyNote.Text = "Keys here are single keys: press the key on its own."; return; }
        if (key == System.Windows.Input.Key.Back) { box.Text = ""; KeyNote.Text = ""; RefreshCard(); return; }
        var name = Aac.Core.Input.KeyMap.Normalize(key.ToString());
        KeyNote.Text = TakeFromOthers(_keyBoxes, box, name);
        box.Text = name;
        RefreshCard();
    }

    /// <summary>An in-app key box: right, middle or a side mouse button can be its key too (the left one only clicks).</summary>
    private void KeyBox_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not TextBox box || MainWindow.MouseKeyName(e.ChangedButton) is not { } name) return;
        e.Handled = true;
        box.Focus();
        KeyNote.Text = TakeFromOthers(_keyBoxes, box, name);
        box.Text = name;
        RefreshCard();
    }

    /// <summary>Empties any other box in the list that had this key, and says which action lost it.</summary>
    private static string TakeFromOthers(List<(TextBox Box, ActionInfo Info)> boxes, TextBox except, string key)
    {
        var lost = new List<string>();
        foreach (var (box, info) in boxes)
        {
            if (box == except || !string.Equals(box.Text, key, StringComparison.OrdinalIgnoreCase)) continue;
            box.Text = "";
            lost.Add(info.Label);
        }
        return lost.Count == 0 ? "" : $"{key} was taken off \"{string.Join("\", \"", lost)}\".";
    }

    // ---------------- QuadStick card ----------------

    private Dictionary<string, CardCell> _card = new();
    private readonly Dictionary<string, (TextBlock Sends, TextBlock Does)> _cardCells = new();

    /// <summary>The card grid: tube names across the top, sip and puff strengths down the side, one square each.</summary>
    private void BuildCard()
    {
        CardGrid.Children.Clear();
        CardGrid.ColumnDefinitions.Clear();
        CardGrid.RowDefinitions.Clear();
        CardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        foreach (var _ in QuadStickCard.Tubes) CardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 128 });
        CardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < QuadStickCard.Tubes.Count; c++)
        {
            var head = new TextBlock { Text = QuadStickCard.Tubes[c].Replace("+", " + "), FontWeight = FontWeights.SemiBold, FontSize = 14,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 6), VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetColumn(head, c + 1);
            CardGrid.Children.Add(head);
        }
        for (var r = 0; r < QuadStickCard.Gestures.Count; r++)
        {
            var gesture = QuadStickCard.Gestures[r];
            CardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            // Puffs in muted red and sips in muted blue, as on the paper cards; the stronger one a little deeper.
            var tint = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(gesture switch
            {
                "Soft puff" => "#3A2730",
                "Puff" => "#4C2A33",
                "Sip" => "#22385A",
                _ => "#1F2F48",
            });
            var name = new TextBlock { Text = gesture, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(name, r + 1);
            CardGrid.Children.Add(name);
            for (var c = 0; c < QuadStickCard.Tubes.Count; c++)
            {
                var tube = QuadStickCard.Tubes[c];
                var id = QuadStickCard.CellId(gesture, tube);
                var sends = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 14, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
                var does = new TextBlock { FontSize = 12.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
                var square = new Button
                {
                    Background = new System.Windows.Media.SolidColorBrush(tint),
                    Margin = new Thickness(2), Padding = new Thickness(4, 6, 4, 6), MinHeight = 74,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center,
                    Content = new StackPanel { Children = { sends, does } },
                };
                square.Click += (_, _) => EditCardCell(gesture, tube);
                Grid.SetRow(square, r + 1);
                Grid.SetColumn(square, c + 1);
                CardGrid.Children.Add(square);
                _cardCells[id] = (sends, does);
            }
        }
        RefreshCard();
    }

    /// <summary>The in-app key box that has this key, if any.</summary>
    private (TextBox Box, ActionInfo Info)? KeyBoxWith(string key) =>
        _keyBoxes.Cast<(TextBox Box, ActionInfo Info)?>().FirstOrDefault(b => string.Equals(b!.Value.Box.Text, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>What each square sends and does, from the card and the key boxes as they are now.</summary>
    private void RefreshCard()
    {
        if (_cardCells.Count == 0) return;
        var text = (System.Windows.Media.Brush)FindResource("DkText");
        var muted = (System.Windows.Media.Brush)FindResource("DkMuted");
        var green = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA9, 0xE8, 0xA0)); // soft green: readable on the red and the blue squares
        foreach (var (id, (sends, does)) in _cardCells)
        {
            var cell = _card.GetValueOrDefault(id);
            sends.Foreground = text;
            does.Foreground = muted;
            switch (QuadStickCard.KindOf(cell))
            {
                case CardSendKind.Key:
                    sends.Text = QuadStickCard.Display(cell!.Key!);
                    if (KeyBoxWith(cell.Key!) is { } bound)
                    {
                        does.Text = bound.Info.Name == nameof(InputAction.Select) ? "Speaks (presses the stepped-to key in arrow mode)" : bound.Info.Label;
                        does.Foreground = green;
                    }
                    else does.Text = "Not used yet: click to give it a job";
                    break;
                case CardSendKind.LeftClick:
                    sends.Text = QuadStickCard.Display(cell!.Key!);
                    does.Text = "Clicks what the pointer is on";
                    break;
                case CardSendKind.Clipboard:
                    sends.Text = QuadStickCard.Display(cell!.Key!);
                    does.Text = QuadStickCard.ClipboardMeaning(cell.Key!);
                    does.Foreground = green;
                    break;
                case CardSendKind.Elsewhere:
                    sends.Text = cell!.Note!;
                    sends.Foreground = muted;
                    does.Text = "Not for BetterTalk";
                    break;
                default:
                    sends.Text = "—";
                    sends.Foreground = muted;
                    does.Text = "";
                    break;
            }
        }
    }

    private void EditCardCell(string gesture, string tube)
    {
        var id = QuadStickCard.CellId(gesture, tube);
        var cell = _card.GetValueOrDefault(id) ?? new CardCell();
        var current = QuadStickCard.KindOf(cell) == CardSendKind.Key && KeyBoxWith(cell.Key!) is { } b ? b.Info.Name : null;
        var editor = new CardCellWindow(gesture, tube, cell, current, _keyBoxes.Select(k => k.Info)) { Owner = this };
        if (editor.ShowDialog() != true) return;
        _card[id] = editor.Cell;
        CardNote.Text = "";
        if (QuadStickCard.KindOf(editor.Cell) == CardSendKind.Key)
        {
            var key = editor.Cell.Key!;
            if (editor.Action.Length == 0)
            {
                foreach (var (box, _) in _keyBoxes)
                    if (string.Equals(box.Text, key, StringComparison.OrdinalIgnoreCase)) box.Text = "";
            }
            else if (_keyBoxes.FirstOrDefault(k => k.Info.Name == editor.Action) is { Box: { } target })
            {
                // One key per action: giving this action the gesture's key takes it off the old key, and the key off any other action.
                CardNote.Text = TakeFromOthers(_keyBoxes, target, key);
                target.Text = key;
            }
        }
        RefreshCard();
    }

    private void ResetCard_Click(object sender, RoutedEventArgs e)
    {
        _card = QuadStickCard.Default();
        CardNote.Text = "";
        RefreshCard();
    }

    /// <summary>Every in-app key back to the defaults; the system-wide hotkeys are cleared (there are none by default).</summary>
    private void ResetKeys_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (box, _) in _hotkeyBoxes) box.Text = "";
        foreach (var (box, info) in _keyBoxes)
            box.Text = Aac.Core.Input.KeyMap.DefaultBindings.FirstOrDefault(kv => string.Equals(kv.Value, info.Name, StringComparison.OrdinalIgnoreCase)).Key ?? "";
        HotkeyNote.Text = "";
        KeyNote.Text = "";
        RefreshCard();
        Debounce.Value = new Aac.Core.Input.InputGateOptions().DebounceMs;
        RepeatDelay.Value = new Aac.Core.Input.InputGateOptions().DeleteRepeatDelayMs;
        RepeatInterval.Value = new Aac.Core.Input.InputGateOptions().DeleteRepeatIntervalMs;
    }

    /// <summary>Closes Settings and turns on edit mode, where buttons are changed by tapping them.</summary>
    /// <summary>Saves all the button words to one file (for another computer, or as a backup).</summary>
    private void ExportButtons_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save buttons to a file",
            FileName = "BetterTalk buttons.json",
            Filter = "BetterTalk buttons (*.json)|*.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ButtonStore.Save(dialog.FileName, _settings.Phrases);
            MessageBox.Show(this, $"The buttons were saved to {dialog.FileName}. On the other computer, open BetterTalk's Settings and choose \"Load buttons from a file\".",
                "Buttons saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The buttons could not be saved: {ex.Message}", "Save buttons", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Replaces this computer's buttons with ones from a file (saved by "Save buttons to a file", or a
    /// buttons.json copied from another computer). Asks first, then restarts the app to show them.
    /// </summary>
    private void ImportButtons_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load buttons from a file",
            Filter = "BetterTalk buttons (*.json)|*.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dialog.ShowDialog(this) != true) return;
        Aac.Core.Prediction.PhraseLibrary? loaded;
        try
        {
            loaded = System.Text.Json.JsonSerializer.Deserialize<Aac.Core.Prediction.PhraseLibrary>(File.ReadAllText(dialog.FileName), SettingsStore.JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
        {
            MessageBox.Show(this, $"That file could not be read as BetterTalk buttons: {ex.Message}", "Load buttons", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (loaded is null || (loaded.Pinned.Count == 0 && loaded.QuickPhrases.Count == 0 && loaded.Categories.Count == 0))
        {
            MessageBox.Show(this, "That file has no buttons in it.", "Load buttons", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var summary = $"{loaded.Pinned.Count} red, {loaded.QuickPhrases.Count} quick and {loaded.Categories.Sum(c => c.Phrases.Count)} phrase buttons";
        if (MessageBox.Show(this, $"Replace this computer's buttons with the {summary} in this file? The app restarts to show them.",
                "Load buttons", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        loaded.KeepAsLoaded();
        try
        {
            ButtonStore.Save(AppPaths.Buttons, loaded);
            _settings.Phrases = loaded;
            SettingsStore.Save(AppPaths.Settings, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The buttons could not be loaded: {ex.Message}", "Load buttons", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Close();
        _restart();
    }

    private void EditButtons_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _editButtons();
    }

    /// <summary>Takes effect at once (no restart): unticked closes this window so the panels can be dragged.</summary>
    private void LockUi_Click(object sender, RoutedEventArgs e)
    {
        var locked = LockUi.IsChecked == true;
        if (!locked) Close();
        _setUnlocked(!locked);
    }

    private void PutPanelsBack_Click(object sender, RoutedEventArgs e) => _putPanelsBack();

    private void SetupGuide_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _setupGuide();
    }

    // ---------- updates ----------

    private Aac.Core.Updates.ReleaseInfo? _release;
    private readonly CancellationTokenSource _updateCancel = new();

    private void ShowUpdateState()
    {
        UpdateVersion.Text = $"This is BetterTalk {Updater.CurrentText}.";
        var snap = Updater.Snapshot();
        GoBack.Content = snap is null ? "No earlier version kept yet" : $"Go back to version {snap.Version} (kept {snap.TakenAt:d MMM yyyy})";
        GoBack.IsEnabled = snap is not null;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdate.IsEnabled = false;
        InstallUpdate.Visibility = FullReadme.Visibility = Visibility.Collapsed;
        UpdateStatus.Text = "Looking for a new version…";
        try
        {
            _release = await Updater.CheckAsync(_updateCancel.Token);
            if (_release is null) { UpdateStatus.Text = "No released version was found."; return; }
            var newer = _release.IsNewerThan(Updater.Current);
            UpdateStatus.Text = newer
                ? $"Version {_release.Tag.TrimStart('v')} is available. Here is what is new; press Update now if you want it."
                : $"You have the newest version. Here is what the latest release ({_release.Tag.TrimStart('v')}) brought.";
            UpdateNotes.Text = _release.Notes.Length > 0 ? Aac.Core.Updates.ReleaseInfo.PlainText(_release.Notes) : "(The release has no notes.)";
            UpdateNotes.Visibility = Visibility.Visible;
            FullReadme.Visibility = Visibility.Visible;
            InstallUpdate.Visibility = newer && _release.InstallerUrl is not null ? Visibility.Visible : Visibility.Collapsed;
            InstallUpdate.Content = $"Update to {_release.Tag.TrimStart('v')}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            UpdateStatus.Text = $"Could not reach the update page ({ex.Message}). Check the internet connection and try again.";
        }
        finally
        {
            CheckUpdate.IsEnabled = true;
        }
    }

    private async void FullReadme_Click(object sender, RoutedEventArgs e)
    {
        if (_release is null) return;
        try
        {
            UpdateNotes.Text = Aac.Core.Updates.ReleaseInfo.PlainText(await Updater.ReadmeAsync(_release, _updateCancel.Token));
            UpdateStatus.Text = $"The full feature list of version {_release.Tag.TrimStart('v')}:";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            UpdateStatus.Text = $"The feature list could not be loaded ({ex.Message}).";
        }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_release is null) return;
        if (MessageBox.Show(this, $"Update BetterTalk to {_release.Tag.TrimStart('v')}?\n\nA snapshot of this version and your setup is kept first, so you can go back to it in this tab. BetterTalk closes while the update installs, then starts again.",
                "BetterTalk update", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        CheckUpdate.IsEnabled = InstallUpdate.IsEnabled = GoBack.IsEnabled = false;
        try
        {
            UpdateStatus.Text = "Keeping a snapshot of this version and your setup…";
            await Task.Run(Updater.TakeSnapshot);
            UpdateStatus.Text = "Downloading the new version…";
            UpdateProgress.Visibility = Visibility.Visible;
            var installer = await Updater.DownloadAsync(_release, new Progress<double>(p => UpdateProgress.Value = p), _updateCancel.Token);
            UpdateStatus.Text = "Installing. BetterTalk will start again in a moment.";
            Updater.StartInstaller(installer);
            Close();
            _quit();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            UpdateStatus.Text = $"The update did not finish ({ex.Message}). Nothing was changed; BetterTalk is still the version you had.";
            UpdateProgress.Visibility = Visibility.Collapsed;
            CheckUpdate.IsEnabled = InstallUpdate.IsEnabled = true;
            ShowUpdateState();
        }
    }

    private void GoBack_Click(object sender, RoutedEventArgs e)
    {
        if (Updater.Snapshot() is not { } snap) return;
        if (MessageBox.Show(this, $"Go back to BetterTalk {snap.Version}?\n\nThe program and its setup (settings, buttons, panel layout, learned words) go back to how they were on {snap.TakenAt:d MMM yyyy}. BetterTalk closes and starts again.",
                "BetterTalk", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            Updater.StartRestore();
            Close();
            _quit();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            UpdateStatus.Text = $"Could not go back ({ex.Message}).";
        }
    }

}

/// <summary>One line in the voice list: a voice (Key is what settings saves) or a group heading.</summary>
public sealed class VoiceRow : System.ComponentModel.INotifyPropertyChanged
{
    public VoiceRow(string? key, string label, VoiceChoice? choice = null)
    {
        Key = key;
        _label = label;
        Choice = choice;
    }

    public string? Key { get; }
    public VoiceChoice? Choice { get; }
    public bool IsHeading { get; init; }

    private string _label;
    public string Label { get => _label; set { _label = value; Changed(nameof(Label)); } }

    private bool _installed;
    /// <summary>A natural voice whose download is on this computer.</summary>
    public bool Installed { get => _installed; set { _installed = value; Changed(nameof(Installed)); } }

    public override string ToString() => Label;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

/// <summary>
/// A voice row in the open list gets its delete button; the closed box showing the chosen voice gets just
/// the name (its content is not inside a list item).
/// </summary>
public sealed class VoiceRowTemplates : DataTemplateSelector
{
    public DataTemplate? InList { get; set; }
    public DataTemplate? Chosen { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        (container as FrameworkElement)?.TemplatedParent is ComboBoxItem ? InList : Chosen;
}
