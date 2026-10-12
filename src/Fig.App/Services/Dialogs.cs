using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fig.App.Services;

/// <summary>Small modal prompts that share the app's dark dialog styling.</summary>
public static class Dialogs
{
    private static readonly IBrush Surface = new SolidColorBrush(Color.Parse("#1D1E20"));
    private static readonly IBrush Primary = new SolidColorBrush(Color.Parse("#29466E"));
    private static readonly IBrush DangerFill = new SolidColorBrush(Color.Parse("#7A2B2F"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8A8A8A"));

    public static Window? MainWindow =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mw } ? mw : null;

    /// <summary>Asks for a line of text. Returns null if cancelled; blank input falls back to <paramref name="fallback"/>.</summary>
    public static async Task<string?> PromptTextAsync(Window? owner, string title, string label, string initial,
        string confirmText, string fallback = "Untitled")
    {
        owner ??= MainWindow;
        if (owner is null)
            return string.IsNullOrWhiteSpace(initial) ? fallback : initial.Trim();

        string? result = null;
        var dialog = NewDialog(title, 440);
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 4, 0, 0) };
        var create = new Button { Content = confirmText, MinWidth = 88, IsDefault = true, Background = Primary };
        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };
        create.Click += (_, _) =>
        {
            var typed = box.Text?.Trim();
            result = string.IsNullOrWhiteSpace(typed) ? fallback : typed;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = Layout(title, null, new TextBlock { Text = label, Foreground = Muted, FontSize = 12 }, box, create, cancel);
        box.AttachedToVisualTree += (_, _) => { box.Focus(); box.SelectAll(); };
        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>Asks the user to confirm a (usually destructive) action.</summary>
    public static async Task<bool> ConfirmAsync(Window? owner, string title, string message, string confirmText, bool danger = false)
    {
        owner ??= MainWindow;
        if (owner is null)
            return false;

        var confirmed = false;
        var dialog = NewDialog(title, 440);
        var ok = new Button { Content = confirmText, MinWidth = 88, Background = danger ? DangerFill : Primary };
        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true, IsDefault = true };
        ok.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = Layout(title, message, null, null, ok, cancel);
        await dialog.ShowDialog(owner);
        return confirmed;
    }

    private static Window NewDialog(string title, double width) => new()
    {
        Title = title,
        Width = width,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = Surface,
    };

    private static Control Layout(string title, string? message, Control? label, Control? input, Button confirm, Button cancel)
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White });
        if (message is not null)
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Muted });
        if (label is not null)
            panel.Children.Add(label);
        if (input is not null)
            panel.Children.Add(input);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancel, confirm },
        });
        return panel;
    }
}
