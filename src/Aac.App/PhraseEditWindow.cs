using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Aac.Core.Session;

namespace Aac.App;

/// <summary>
/// The small box for changing a button in edit mode: type the new text, then Save; Delete removes the button;
/// ◀ Earlier and Later ▶ move it along its row at once (the screen behind shows where it is now). Large text
/// so it can be used from a distance, in the same dark look as Settings.
/// </summary>
public sealed class PhraseEditWindow : Window
{
    private readonly TextBox _text;

    /// <summary>The new text, "" to delete, or null when cancelled.</summary>
    public string? Result { get; private set; }

    public PhraseEditWindow(PhraseEdit edit, CommunicationSession session)
    {
        var where = edit.List switch
        {
            PhraseList.Urgent => "red button (top row)",
            PhraseList.Quick => "Quick button",
            PhraseList.Spoken => "word said out loud (Itch)",
            _ => $"phrase in “{edit.Category}”",
        };
        Title = edit.OldText is null ? $"Add a {where}" : $"Change this {where}";
        Width = 680;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        FontSize = 22;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/BetterTalk;component/DarkTheme.xaml", UriKind.Relative) });
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x16, 0x24));
        Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xFA));

        _text = new TextBox { Text = edit.OldText ?? "", Margin = new Thickness(0, 8, 0, 16), Padding = new Thickness(6), AcceptsReturn = false };
        var save = new Button { Content = "Save", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(10, 0, 0, 0), IsDefault = true };
        var delete = new Button { Content = "Delete button", Padding = new Thickness(16, 6, 16, 6), Visibility = edit.OldText is null ? Visibility.Collapsed : Visibility.Visible };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(10, 0, 0, 0), IsCancel = true };
        save.Click += (_, _) => { Result = _text.Text.Trim(); DialogResult = Result.Length > 0 || edit.OldText is not null; };
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(this, $"Delete “{edit.OldText}”?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Result = "";
            DialogResult = true;
        };

        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(delete, Dock.Left);
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(save, Dock.Right);
        buttons.Children.Add(delete);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = edit.OldText is null ? "Words for the new button:" : "New words for this button:", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_text);

        // Moving it along its row: applied at once, so it can be pressed again and again while watching the screen.
        if (edit.OldText is not null && session.PlaceOf(edit) is not null)
        {
            var place = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0), MinWidth = 120, TextAlignment = TextAlignment.Center };
            var earlier = new Button { Content = "◀ Earlier", Padding = new Thickness(18, 6, 18, 6), ToolTip = "Moves this button one place towards the start of its row" };
            var later = new Button { Content = "Later ▶", Padding = new Thickness(18, 6, 18, 6), ToolTip = "Moves this button one place towards the end of its row" };
            void ShowPlace()
            {
                if (session.PlaceOf(edit) is not { } p) return;
                place.Text = $"{p.Position} of {p.Count}";
                earlier.IsEnabled = p.Position > 1;
                later.IsEnabled = p.Position < p.Count;
            }
            earlier.Click += (_, _) => { session.MoveButton(edit, -1); ShowPlace(); };
            later.Click += (_, _) => { session.MoveButton(edit, +1); ShowPlace(); };
            ShowPlace();
            panel.Children.Add(new TextBlock { Text = "Its place in the row:", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 18),
                Children = { earlier, place, later },
            });
        }
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { _text.Focus(); _text.SelectAll(); };
    }
}
