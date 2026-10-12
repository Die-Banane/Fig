using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fig.App.Views;

/// <summary>A read-only cheat sheet of the editor's keyboard and mouse shortcuts.</summary>
public static class ShortcutsDialog
{
    private static readonly (string Section, (string Keys, string What)[] Items)[] Groups =
    [
        ("Playback", [("Space", "Play / pause")]),
        ("Editing", [
            ("Ctrl+K", "Split at playhead"),
            ("Delete", "Delete selection (leave a gap)"),
            ("Ctrl+Z / Ctrl+Y", "Undo / redo"),
            ("Ctrl+E", "Enable or disable the selected clip"),
            ("N", "Turn snapping on or off"),
            ("M", "Add a marker at the playhead"),
            ("Shift+Delete", "Delete the selected marker"),
        ]),
        ("Timeline", [
            ("Ctrl+Wheel", "Zoom in or out"),
            ("Wheel / Shift+Wheel", "Scroll sideways"),
            ("Middle-drag", "Pan the timeline"),
        ]),
        ("Workspace", [
            ("Ctrl+1", "Show or hide the media panel"),
            ("Ctrl+2", "Show or hide the inspector"),
            ("Ctrl+Shift+F", "Focus the preview"),
            ("Ctrl+S", "Save"),
            ("Ctrl+W", "Back to projects"),
        ]),
    ];

    public static void Show(Window owner)
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = "Keyboard shortcuts", FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var (section, items) in Groups)
        {
            panel.Children.Add(new TextBlock { Text = section, Foreground = new SolidColorBrush(Color.Parse("#8A8A8A")), Margin = new Thickness(0, 10, 0, 2) });
            foreach (var (keys, what) in items)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*") };
                row.Children.Add(new TextBlock { Text = keys, FontFamily = new FontFamily("monospace"), Foreground = new SolidColorBrush(Color.Parse("#C8C8C8")) });
                var desc = new TextBlock { Text = what };
                Grid.SetColumn(desc, 1);
                row.Children.Add(desc);
                panel.Children.Add(row);
            }
        }

        var close = new Button { Content = "Close", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true, IsDefault = true, Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(close);

        var dialog = new Window
        {
            Title = "Keyboard shortcuts",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#1D1E20")),
            Content = new ScrollViewer { Content = panel },
        };
        close.Click += (_, _) => dialog.Close();
        _ = dialog.ShowDialog(owner);
    }
}
