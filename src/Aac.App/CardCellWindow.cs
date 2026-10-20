using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Aac.Core.Input;

namespace Aac.App;

/// <summary>
/// Changing one square of the QuadStick card: what the gesture sends (a key or click, something that stays with
/// the QuadStick or Windows, or nothing) and, for a key, which BetterTalk action it does.
/// </summary>
public sealed class CardCellWindow : Window
{
    private const string NothingAction = "";

    private readonly RadioButton _sendsKey, _sendsElsewhere, _sendsNothing;
    private readonly TextBox _key, _note;
    private readonly ComboBox _action;
    private readonly TextBlock _fixed, _problem;
    private string _keyName;

    /// <summary>The square as saved.</summary>
    public CardCell Cell { get; private set; } = new();

    /// <summary>For a key: the action it should do ("" for none).</summary>
    public string Action { get; private set; } = NothingAction;

    public CardCellWindow(string gesture, string tube, CardCell cell, string? currentAction, IEnumerable<ActionInfo> actions)
    {
        Title = $"{gesture}, {tube} tube";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FontSize = 18;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/BetterTalk;component/DarkTheme.xaml", UriKind.Relative) });
        Background = (Brush)FindResource("DkBg");
        Foreground = (Brush)FindResource("DkText");
        var muted = (Brush)FindResource("DkMuted");

        var kind = QuadStickCard.KindOf(cell);
        _keyName = kind is CardSendKind.Key or CardSendKind.LeftClick or CardSendKind.Clipboard ? cell.Key! : "";

        _sendsKey = new RadioButton { Content = "It sends a key or a mouse click", GroupName = "sends", Margin = new Thickness(0, 10, 0, 0) };
        _key = new TextBox { IsReadOnly = true, Width = 260, Margin = new Thickness(26, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var leftClick = new Button { Content = "It's a left click", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(10, 6, 0, 0) };
        var keyHint = new TextBlock { Text = "Click the box, then do the gesture (or press the key, or click a mouse button other than the left one in it).",
            Foreground = muted, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, 4, 0, 0) };
        _sendsElsewhere = new RadioButton { Content = "It stays with the QuadStick or Windows (a mode switch, the Windows key…)", GroupName = "sends", Margin = new Thickness(0, 14, 0, 0) };
        _note = new TextBox { Text = cell.Note ?? "", Margin = new Thickness(26, 6, 0, 0) };
        _sendsNothing = new RadioButton { Content = "Nothing", GroupName = "sends", Margin = new Thickness(0, 14, 0, 0) };

        _action = new ComboBox { Margin = new Thickness(0, 6, 0, 0) };
        _action.Items.Add(new ComboBoxItem { Content = "Nothing", Tag = NothingAction });
        foreach (var a in actions) _action.Items.Add(new ComboBoxItem { Content = a.Label, Tag = a.Name });
        _action.SelectedItem = _action.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, currentAction, StringComparison.OrdinalIgnoreCase))
            ?? _action.Items[0];
        _fixed = new TextBlock { Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        _problem = new TextBlock { Foreground = (Brush)FindResource("DkWarn"), FontSize = 14, TextWrapping = TextWrapping.Wrap };

        (kind switch
        {
            CardSendKind.Elsewhere => _sendsElsewhere,
            CardSendKind.Nothing => _sendsNothing,
            _ => _sendsKey,
        }).IsChecked = true;

        _key.PreviewKeyDown += Key_PreviewKeyDown;
        _key.PreviewMouseDown += (_, e) =>
        {
            if (MainWindow.MouseKeyName(e.ChangedButton) is not { } name) return;
            e.Handled = true;
            _key.Focus();
            SetKey(name);
        };
        _key.ContextMenuOpening += (_, e) => e.Handled = true;
        _key.GotFocus += (_, _) => _sendsKey.IsChecked = true;
        _note.GotFocus += (_, _) => _sendsElsewhere.IsChecked = true;
        leftClick.Click += (_, _) => SetKey(QuadStickCard.LeftClick);
        foreach (var r in new[] { _sendsKey, _sendsElsewhere, _sendsNothing }) r.Checked += (_, _) => ShowAction();

        var save = new Button { Content = "Save", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(10, 0, 0, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(10, 0, 0, 0), IsCancel = true };
        save.Click += (_, _) => Save();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0), Children = { save, cancel } };

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = $"What does a {gesture.ToLowerInvariant()} on the {tube.ToLowerInvariant()} tube send?", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_sendsKey);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _key, leftClick } });
        panel.Children.Add(keyHint);
        panel.Children.Add(_problem);
        panel.Children.Add(_sendsElsewhere);
        panel.Children.Add(_note);
        panel.Children.Add(_sendsNothing);
        panel.Children.Add(new TextBlock { Text = "What BetterTalk does with it", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 0) });
        panel.Children.Add(_action);
        panel.Children.Add(_fixed);
        panel.Children.Add(buttons);
        Content = panel;
        ShowKey();
        ShowAction();
    }

    /// <summary>The next key pressed in the box (Tab and Backspace included: they are on many cards). Ctrl goes only with C, X or V.</summary>
    private void Key_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if (SettingsWindow.IsModifierKey(key)) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && key is Key.C or Key.X or Key.V) { SetKey("Ctrl+" + key); return; }
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            _problem.Text = "BetterTalk takes single keys here (Ctrl-C, Ctrl-X and Ctrl-V are the exceptions). A gesture that sends a key combination can go under \"stays with the QuadStick or Windows\".";
            return;
        }
        SetKey(KeyMap.Normalize(key.ToString()));
    }

    private void SetKey(string name)
    {
        _keyName = name;
        _problem.Text = "";
        _sendsKey.IsChecked = true;
        ShowKey();
        ShowAction();
    }

    private void ShowKey() => _key.Text = _keyName.Length > 0 ? QuadStickCard.Display(_keyName) : "";

    private void ShowAction()
    {
        var kind = _sendsKey.IsChecked == true ? QuadStickCard.KindOf(new CardCell(_keyName)) : CardSendKind.Nothing;
        _action.Visibility = kind == CardSendKind.Key ? Visibility.Visible : Visibility.Collapsed;
        _fixed.Visibility = kind == CardSendKind.Key ? Visibility.Collapsed : Visibility.Visible;
        _fixed.Text = kind switch
        {
            CardSendKind.LeftClick => "Clicks what the pointer is on, as always.",
            CardSendKind.Clipboard => QuadStickCard.ClipboardMeaning(_keyName) + ".",
            _ when _sendsElsewhere.IsChecked == true => "Nothing: BetterTalk doesn't get it.",
            _ => "Nothing.",
        };
    }

    private void Save()
    {
        if (_sendsKey.IsChecked == true && _keyName.Length == 0)
        {
            _problem.Text = "Click the box and do the gesture first, or pick another choice.";
            return;
        }
        Cell = _sendsKey.IsChecked == true ? new CardCell(_keyName)
            : _sendsElsewhere.IsChecked == true && _note.Text.Trim().Length > 0 ? new CardCell(Note: _note.Text.Trim())
            : new CardCell();
        Action = QuadStickCard.KindOf(Cell) == CardSendKind.Key ? (string)((ComboBoxItem)_action.SelectedItem).Tag : NothingAction;
        DialogResult = true;
    }
}
