using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Aac.Core.Input;
using Aac.Core.Layout;
using Aac.Core.Storage;

namespace Aac.App;

/// <summary>
/// The set-up guide: a few questions, asked on the first start (and again from Settings or the tray menu),
/// that make the main choices at once: how the user chooses buttons, how BetterTalk comes back, which
/// screen and corner it sits in, whether it hides by itself, and the voice. The window moves as the corner is
/// chosen, so the choice can be seen. Finish saves the choices and restarts BetterTalk; Skip changes nothing.
/// </summary>
public sealed class SetupGuideWindow : Window
{
    private readonly AppSettings _settings;
    private readonly MainWindow _main;
    private readonly Action _finish;
    private readonly (string? Screen, WindowCorner Anchor) _original;

    // The choices, filled from the current settings so running the guide again shows what is set now.
    private FocusHighlight _highlight;
    private string _cornerWay;
    private bool _useHotkey;
    private string _hotkey;
    private string? _screen;
    private WindowCorner _anchor;
    private bool _autoHide;
    private int _autoHideSeconds;
    private string? _voice;

    private readonly ContentControl _body = new() { Focusable = false };
    private readonly TextBlock _step = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xAF, 0xBC, 0xD2)), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back = new() { Content = "◀ Back", Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(10, 0, 0, 0) };
    private readonly Button _next = new() { Content = "Next ▶", Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(10, 0, 0, 0), FontWeight = FontWeights.SemiBold, IsDefault = true };
    private readonly Button _skip = new() { Content = "Skip for now", Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(10, 0, 0, 0), IsCancel = true };
    private readonly Func<UIElement>[] _pages;
    private int _page;
    private SpeechSynthesizer? _synth;
    private string? _defaultVoice;
    private bool _finished;

    public SetupGuideWindow(AppSettings settings, MainWindow main, Action finish)
    {
        _settings = settings;
        _main = main;
        _finish = finish;
        _original = (settings.Screen, settings.Anchor);
        _highlight = settings.SelectionHighlight;
        _cornerWay = settings.OpenFromCorner ? settings.CornerTrigger.ToString() : "Off";
        _hotkey = settings.Hotkeys.FirstOrDefault(h => string.Equals(h.Value, nameof(HotkeyAction.ShowOrHide), StringComparison.OrdinalIgnoreCase)).Key ?? "";
        _useHotkey = _hotkey.Length > 0;
        _screen = settings.Screen;
        _anchor = settings.Anchor;
        _autoHide = settings.AutoHideWhenMouseLeaves;
        _autoHideSeconds = Math.Clamp((int)Math.Round(settings.AutoHideAfterMs / 1000.0), 1, 10);
        _voice = settings.Speech.VoiceName;

        Title = "BetterTalk set-up";
        Width = 780;
        Height = 640;
        MinWidth = 600;
        MinHeight = 520;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 16;
        // The same dark, readable look as the Settings window.
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/BetterTalk;component/DarkTheme.xaml", UriKind.Relative) });
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x16, 0x24));
        Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xFA));
        Owner = main;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;

        _pages = new Func<UIElement>[] { Welcome, Pointing, Opening, Place, AutoHidePage, VoicePage, Done };
        _back.Click += (_, _) => ShowPage(_page - 1);
        _next.Click += (_, _) => { if (_page == _pages.Length - 1) Finish(); else ShowPage(_page + 1); };
        _skip.Click += (_, _) => Close();

        var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(_next, Dock.Right);
        DockPanel.SetDock(_back, Dock.Right);
        DockPanel.SetDock(_skip, Dock.Right);
        buttons.Children.Add(_next);
        buttons.Children.Add(_back);
        buttons.Children.Add(_skip);
        buttons.Children.Add(_step);
        var root = new DockPanel { Margin = new Thickness(22) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        ShowPage(0);

        Closed += (_, _) =>
        {
            _synth?.Dispose();
            // Skipped or closed: the window goes back to where it was.
            if (!_finished) _main.PreviewPlacement(_original.Screen, _original.Anchor);
        };
    }

    private void ShowPage(int index)
    {
        _page = Math.Clamp(index, 0, _pages.Length - 1);
        _body.Content = _pages[_page]();
        _step.Text = $"Step {_page + 1} of {_pages.Length}";
        _back.Visibility = _page > 0 ? Visibility.Visible : Visibility.Collapsed;
        _next.Content = _page == _pages.Length - 1 ? "Finish" : "Next ▶";
    }

    // ---------- the pages ----------

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
    private static TextBlock Para(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xFA)) };
    private static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0xAF, 0xBC, 0xD2)), Margin = new Thickness(26, 0, 0, 10) };

    private static RadioButton Radio(string group, string text, bool isChecked, Action chosen)
    {
        var r = new RadioButton { GroupName = group, IsChecked = isChecked, Margin = new Thickness(0, 6, 0, 2), VerticalContentAlignment = VerticalAlignment.Top };
        r.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 640 };
        r.Checked += (_, _) => chosen();
        return r;
    }

    private UIElement Welcome()
    {
        var p = new StackPanel();
        p.Children.Add(Heading("Welcome to BetterTalk"));
        p.Children.Add(Para("A few questions set BetterTalk up for the person who will use it. It takes about a minute."));
        p.Children.Add(Para("Everything here can be changed later in Settings: right-click the BetterTalk icon near the clock and choose Settings. Skip for now keeps the usual choices."));
        p.Children.Add(Para("You will be asked: how the user chooses buttons, how BetterTalk comes back after hiding, which screen and corner it sits in, whether it hides by itself, and which voice it speaks with."));
        return p;
    }

    private UIElement Pointing()
    {
        var p = new StackPanel();
        p.Children.Add(Heading("How will the user choose buttons?"));
        p.Children.Add(Para("BetterTalk is a keyboard of big buttons on the screen. A pressed button always flashes green."));
        p.Children.Add(Radio("point", "By pointing and clicking: a mouse, a QuadStick in mouse mode, an eye tracker or a touch screen.",
            _highlight != FocusHighlight.Always, () => _highlight = FocusHighlight.WhileUsingKeys));
        p.Children.Add(Hint("Arrow keys and switches work as well. A yellow frame then shows which button is chosen, but only while they are being used, so it never gets in the way of pointing."));
        p.Children.Add(Radio("point", "By stepping from button to button with arrow keys or switches, and pressing Select.",
            _highlight == FocusHighlight.Always, () => _highlight = FocusHighlight.Always));
        p.Children.Add(Hint("The yellow frame always shows which button is chosen."));
        return p;
    }

    private UIElement Opening()
    {
        var p = new StackPanel();
        p.Children.Add(Heading("How should BetterTalk come back?"));
        p.Children.Add(Para("BetterTalk hides out of the way while the user plays a game or uses the browser, and comes back when wanted. Clicking the BetterTalk icon near the clock always works. Choose what else should bring it back:"));
        p.Children.Add(Radio("corner", "A click in the corner of the screen where BetterTalk sits.", _cornerWay == "Click", () => _cornerWay = "Click"));
        p.Children.Add(Hint("Good for a mouse, a QuadStick in mouse mode or an eye tracker: push the pointer into the corner and click."));
        p.Children.Add(Radio("corner", "The pointer resting in that corner for a moment.", _cornerWay == "Rest", () => _cornerWay = "Rest"));
        p.Children.Add(Hint("For people who cannot click: just leave the pointer in the corner."));
        p.Children.Add(Radio("corner", "Nothing in the corner.", _cornerWay == "Off", () => _cornerWay = "Off"));

        var hotkey = new CheckBox { IsChecked = _useHotkey, Margin = new Thickness(0, 18, 0, 2), VerticalContentAlignment = VerticalAlignment.Center };
        hotkey.Content = new TextBlock { Text = "A hotkey that works in every program (press the keys in the box):", TextWrapping = TextWrapping.Wrap, MaxWidth = 640 };
        var box = new TextBox { Text = _hotkey, IsReadOnly = true, Width = 220, Margin = new Thickness(26, 4, 0, 2), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = _useHotkey, Padding = new Thickness(6, 4, 6, 4) };
        var note = Hint("For example Ctrl+Shift+F12. A hotkey needs Ctrl, Alt, Shift or Win with it, or an F-key on its own, so no plain key is taken from other programs.");
        box.PreviewKeyDown += (sender, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Tab) return;
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            e.Handled = true;
            if (SettingsWindow.IsModifierKey(key)) return; // wait for the key that goes with it
            var mods = SettingsWindow.HeldModifiers();
            if (key == System.Windows.Input.Key.Back && mods == HotkeyModifiers.None) { box.Text = _hotkey = ""; return; }
            var spec = HotkeySpec.Format(mods, KeyMap.Normalize(key.ToString()));
            if (!HotkeySpec.TryParse(spec, out _, out _, out var problem)) { note.Text = problem; return; }
            note.Text = $"{spec} shows or hides BetterTalk from anywhere.";
            box.Text = _hotkey = spec;
        };
        hotkey.Checked += (_, _) => { _useHotkey = true; box.IsEnabled = true; box.Focus(); };
        hotkey.Unchecked += (_, _) => { _useHotkey = false; box.IsEnabled = false; };
        p.Children.Add(hotkey);
        p.Children.Add(box);
        p.Children.Add(note);
        return p;
    }

    private sealed record ScreenRow(string Label, string? Key) { public override string ToString() => Label; }

    private UIElement Place()
    {
        var p = new StackPanel();
        p.Children.Add(Heading("Which screen and corner?"));
        p.Children.Add(Para("BetterTalk sits in a corner of a screen and grows away from it when a panel opens, so the keyboard never moves. The window moves as you choose, so you can see where it will be."));

        var main = Screens.MainScreen();
        var screens = new ComboBox { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 14), Padding = new Thickness(8, 6, 8, 6) };
        foreach (var s in Screens.All()) screens.Items.Add(new ScreenRow(s.Describe(main), s.IsMain ? null : s.DeviceName));
        var chosen = Screens.Choose(_screen);
        screens.SelectedItem = screens.Items.Cast<ScreenRow>().First(r => (r.Key ?? main.DeviceName).Equals(chosen.IsMain ? main.DeviceName : chosen.DeviceName, StringComparison.OrdinalIgnoreCase));
        screens.SelectionChanged += (_, _) => { if (screens.SelectedItem is ScreenRow r) { _screen = r.Key; Preview(); } };
        if (Screens.All().Count > 1)
        {
            p.Children.Add(new TextBlock { Text = "Screen", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            p.Children.Add(screens);
        }

        p.Children.Add(new TextBlock { Text = "Corner", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 4) });
        var grid = new UniformGrid { Rows = 2, Columns = 2, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var corners = new List<(WindowCorner Corner, RadioButton Button)>();
        foreach (var (corner, label) in new[] { (WindowCorner.TopLeft, "Top left"), (WindowCorner.TopRight, "Top right"), (WindowCorner.BottomLeft, "Bottom left"), (WindowCorner.BottomRight, "Bottom right") })
        {
            var b = new RadioButton
            {
                Content = label, GroupName = "corner-pick", IsChecked = _anchor == corner, Margin = new Thickness(4),
                Padding = new Thickness(12, 16, 12, 16), HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            b.SetResourceReference(StyleProperty, typeof(ToggleButton)); // looks like a button, works like a radio
            b.Checked += (_, _) => { _anchor = corner; Preview(); };
            corners.Add((corner, b));
            grid.Children.Add(b);
        }
        p.Children.Add(grid);
        p.Children.Add(Hint("Top left is the usual choice. With a bottom corner the message box sits at the top of the window and the window grows upward."));
        return p;

        void Preview() => _main.PreviewPlacement(_screen, _anchor);
    }

    private UIElement AutoHidePage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading("Hide by itself?"));
        p.Children.Add(Para("BetterTalk can tuck itself away once the pointer has been off it for a moment, so the user gets the whole screen back for their game or browser. It comes back the way you chose earlier, and it never hides while it is speaking."));
        var on = new CheckBox { IsChecked = _autoHide, Margin = new Thickness(0, 6, 0, 2), VerticalContentAlignment = VerticalAlignment.Center };
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 640 };
        on.Content = label;
        var slider = new Slider { Minimum = 1, Maximum = 10, Value = _autoHideSeconds, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 360, Margin = new Thickness(26, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = _autoHide };
        void Describe() => label.Text = $"Hide BetterTalk when the pointer has been away from it for {_autoHideSeconds} second{(_autoHideSeconds == 1 ? "" : "s")}";
        Describe();
        slider.ValueChanged += (_, _) => { _autoHideSeconds = (int)slider.Value; Describe(); };
        on.Checked += (_, _) => { _autoHide = true; slider.IsEnabled = true; };
        on.Unchecked += (_, _) => { _autoHide = false; slider.IsEnabled = false; };
        p.Children.Add(on);
        p.Children.Add(slider);
        p.Children.Add(Hint("Two seconds suits most people. Untick it if the user would rather hide BetterTalk only with its yellow Hide button, which is always there."));
        return p;
    }

    private sealed record VoiceRow(string Label, string? Key) { public override string ToString() => Label; }

    private UIElement VoicePage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading("Which voice?"));
        p.Children.Add(Para("These are the voices built into Windows. Pick one and press Hear it. More natural voices can be downloaded later in Settings → Voice & speaking."));
        var list = new ListBox { Width = 460, MaxHeight = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 8) };
        list.Items.Add(new VoiceRow("The Windows default voice", null));
        try
        {
            _synth ??= new SpeechSynthesizer();
            _defaultVoice ??= _synth.Voice.Name;
            foreach (var v in _synth.GetInstalledVoices().Where(v => v.Enabled))
                list.Items.Add(new VoiceRow($"{v.VoiceInfo.Name} ({v.VoiceInfo.Culture.DisplayName}, {v.VoiceInfo.Gender.ToString().ToLowerInvariant()})", v.VoiceInfo.Name));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or PlatformNotSupportedException)
        {
            p.Children.Add(Hint($"The Windows voices could not be listed ({ex.Message}); the default voice will be used."));
        }
        list.SelectedItem = list.Items.Cast<VoiceRow>().FirstOrDefault(r => string.Equals(r.Key, _voice, StringComparison.OrdinalIgnoreCase)) ?? list.Items[0];
        var hear = new Button { Content = "🔊 Hear it", Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left };
        var note = Hint("");
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is VoiceRow r) { _voice = r.Key; Hear(); } };
        hear.Click += (_, _) => Hear();
        p.Children.Add(list);
        p.Children.Add(hear);
        p.Children.Add(note);
        return p;

        void Hear()
        {
            if (_synth is null) return;
            try
            {
                _synth.SpeakAsyncCancelAll();
                _synth.SelectVoice(_voice ?? _defaultVoice ?? _synth.Voice.Name);
                _synth.SpeakAsync("Hello. This is how I sound.");
                note.Text = "";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                note.Text = $"That voice could not be played ({ex.Message}).";
            }
        }
    }

    private UIElement Done()
    {
        var p = new StackPanel();
        p.Children.Add(Heading("All set"));
        p.Children.Add(Para("Finish saves these choices and restarts BetterTalk:"));
        var lines = new[]
        {
            _highlight == FocusHighlight.Always ? "Buttons are chosen by stepping with keys or switches; the yellow frame always shows." : "Buttons are chosen by pointing and clicking; the yellow frame shows only while keys or switches are used.",
            _cornerWay switch { "Click" => "A click in its corner brings BetterTalk back.", "Rest" => "The pointer resting in its corner brings BetterTalk back.", _ => "Nothing in the corner brings it back (the icon near the clock always does)." },
            _useHotkey && _hotkey.Length > 0 ? $"{_hotkey} shows or hides it from any program." : "No hotkey.",
            $"It sits in the {CornerWords(_anchor)} corner of {Screens.Choose(_screen).Describe(Screens.MainScreen()).ToLowerInvariant()}.",
            _autoHide ? $"It hides by itself after {_autoHideSeconds} second{(_autoHideSeconds == 1 ? "" : "s")} with the pointer away." : "It stays until it is hidden with the Hide button.",
            _voice is null ? "It speaks with the Windows default voice." : $"It speaks with the voice {_voice}.",
        };
        foreach (var line in lines) p.Children.Add(new TextBlock { Text = "•  " + line, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 2, 0, 2) });
        p.Children.Add(Para(""));
        p.Children.Add(Para("Settings has everything else: text size, the words on the buttons, hotkeys for every action, the Itch figure and more."));
        return p;
    }

    private static string CornerWords(WindowCorner corner) => corner switch
    {
        WindowCorner.TopLeft => "top-left",
        WindowCorner.TopRight => "top-right",
        WindowCorner.BottomLeft => "bottom-left",
        _ => "bottom-right",
    };

    /// <summary>Writes every choice into the settings; the app then saves them and restarts.</summary>
    private void Finish()
    {
        _settings.SelectionHighlight = _highlight;
        _settings.OpenFromCorner = _cornerWay != "Off";
        if (Enum.TryParse<CornerTrigger>(_cornerWay, out var trigger)) _settings.CornerTrigger = trigger;
        foreach (var old in _settings.Hotkeys.Where(h => string.Equals(h.Value, nameof(HotkeyAction.ShowOrHide), StringComparison.OrdinalIgnoreCase)).Select(h => h.Key).ToList())
            _settings.Hotkeys.Remove(old);
        if (_useHotkey && HotkeySpec.TryParse(_hotkey, out _, out _, out _)) _settings.Hotkeys[_hotkey] = nameof(HotkeyAction.ShowOrHide);
        _settings.Screen = _screen;
        _settings.Anchor = _anchor;
        _settings.AutoHideWhenMouseLeaves = _autoHide;
        _settings.AutoHideAfterMs = _autoHideSeconds * 1000;
        _settings.Speech.VoiceName = _voice;
        _finished = true;
        Close();
        _finish();
    }
}
