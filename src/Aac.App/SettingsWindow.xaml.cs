using System.IO;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
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

    public SettingsWindow(AppSettings current, Action restart, Action editButtons)
    {
        InitializeComponent();
        _restart = restart;
        _editButtons = editButtons;
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
        foreach (var (box, action) in DollKeys) box.Text = KeyFor(action) ?? "";
        var corner = _settings.OpenFromTopLeftCorner ? _settings.CornerTrigger.ToString() : "Off";
        Corner.SelectedItem = Corner.Items.Cast<System.Windows.Controls.ComboBoxItem>().First(i => (string)i.Tag == corner);
        StartHidden.IsChecked = _settings.StartHidden;
        LockSize.IsChecked = _settings.LockWindowSize;
        AutoHide.IsChecked = _settings.AutoHideWhenMouseLeaves;
        AutoHideSeconds.Value = Math.Clamp(Math.Round(_settings.AutoHideAfterMs / 1000.0), 1, 10);
        AlwaysOnTop.IsChecked = _settings.AlwaysOnTop;
        QuickSpeak.IsChecked = _settings.Speech.QuickPhrasesSpeakImmediately;
        Learn.IsChecked = _settings.Privacy.LearnFromSpokenMessages;
        Pinned.Text = string.Join(Environment.NewLine, _settings.Phrases.Pinned);
        Quick.Text = string.Join(Environment.NewLine, _settings.Phrases.QuickPhrases);
    }

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

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
        foreach (var (box, action) in DollKeys) SetKey(action, box.Text);
        var cornerChoice = (string)((System.Windows.Controls.ComboBoxItem)Corner.SelectedItem).Tag;
        _settings.OpenFromTopLeftCorner = cornerChoice != "Off";
        if (Enum.TryParse<CornerTrigger>(cornerChoice, out var trigger)) _settings.CornerTrigger = trigger;
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

    private (System.Windows.Controls.TextBox Box, Aac.Core.Input.InputAction Action)[] DollKeys => new[]
    {
        (KeyTurnLeft, Aac.Core.Input.InputAction.DollTurnLeft), (KeyTurnRight, Aac.Core.Input.InputAction.DollTurnRight),
        (KeyCloser, Aac.Core.Input.InputAction.DollCloser), (KeyFarther, Aac.Core.Input.InputAction.DollFarther),
        (KeyMoveUp, Aac.Core.Input.InputAction.DollMoveUp), (KeyMoveDown, Aac.Core.Input.InputAction.DollMoveDown),
        (KeyMoveLeft, Aac.Core.Input.InputAction.DollMoveLeft), (KeyMoveRight, Aac.Core.Input.InputAction.DollMoveRight),
    };

    private string? KeyFor(Aac.Core.Input.InputAction action) =>
        _settings.KeyBindings.FirstOrDefault(kv => string.Equals(kv.Value, action.ToString(), StringComparison.OrdinalIgnoreCase)).Key;

    /// <summary>Binds one key to the action, replacing whatever that key or action had before.</summary>
    private void SetKey(Aac.Core.Input.InputAction action, string key)
    {
        foreach (var old in _settings.KeyBindings.Where(kv => string.Equals(kv.Value, action.ToString(), StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
            _settings.KeyBindings.Remove(old);
        if (key.Length > 0) _settings.KeyBindings[key] = action.ToString();
    }

    /// <summary>The next key pressed in one of the doll key boxes becomes its key (Backspace clears it).</summary>
    private void KeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box || e.Key == System.Windows.Input.Key.Tab) return;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        box.Text = key == System.Windows.Input.Key.Back ? "" : key.ToString();
        e.Handled = true;
    }

    /// <summary>Closes Settings and turns on edit mode, where buttons are changed by tapping them.</summary>
    private void EditButtons_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _editButtons();
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e) => App.OpenSettingsFile();
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
