using System.Windows;
using System.Windows.Controls;
using Aac.Core.Session;

namespace Aac.App;

/// <summary>
/// The small box for changing a button's words in edit mode: type the new text, then Save; Delete
/// removes the button. Large text so a caregiver can use it from a distance.
/// </summary>
public sealed class PhraseEditWindow : Window
{
    private readonly TextBox _text;

    /// <summary>The new text, "" to delete, or null when cancelled.</summary>
    public string? Result { get; private set; }

    public PhraseEditWindow(PhraseEdit edit)
    {
        var where = edit.List switch
        {
            PhraseList.Urgent => "red button (top row)",
            PhraseList.Quick => "Quick button",
            _ => $"phrase in “{edit.Category}”",
        };
        Title = edit.OldText is null ? $"Add a {where}" : $"Change this {where}";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        FontSize = 22;

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

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = edit.OldText is null ? "Words for the new button:" : "New words for this button:", TextWrapping = TextWrapping.Wrap },
                _text,
                buttons,
            },
        };
        Loaded += (_, _) => { _text.Focus(); _text.SelectAll(); };
    }
}
