using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fig.App.Services;

namespace Fig.App.Tests;

public class TransportKeyTests
{
    private static void Down(Window window) => window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
    private static void Up(Window window) => window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");

    [AvaloniaFact]
    public void FocusedButton_SpaceTogglesOncePerPress_AndNeverActivatesButton()
    {
        int plays = 0, clicks = 0;
        var button = new Button { Content = "Delete" };
        button.Click += (_, _) => clicks++;
        var editor = new StackPanel { Focusable = true, Children = { button } };
        var window = new Window { Content = editor, Width = 400, Height = 200 };
        window.Show();
        try
        {
            var command = new RelayCommand(() => plays++);
            using var keys = new EditorTransportKeys(editor, () => command, () => editor.Focus());
            button.Focus();
            Down(window);
            Down(window); // OS key repeat
            Up(window);
            Assert.Equal(1, plays);
            Assert.Equal(0, clicks);
            Down(window);
            Up(window);
            Assert.Equal(2, plays);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ContextMenu_SpaceClosesMenuAndPlays_WithoutRunningSelectedAction()
    {
        int plays = 0, deletes = 0;
        var item = new MenuItem { Header = "Delete clip" };
        item.Click += (_, _) => deletes++;
        var menu = new ContextMenu { Items = { item } };
        var editor = new Button { Content = "Editor", ContextMenu = menu };
        var window = new Window { Content = editor, Width = 400, Height = 200 };
        window.Show();
        try
        {
            var command = new RelayCommand(() => plays++);
            using var keys = new EditorTransportKeys(editor, () => command, () => editor.Focus());
            menu.Open(editor);
            Dispatcher.UIThread.RunJobs();
            item.Focus();
            Assert.True(menu.IsOpen);
            Down(window);
            Up(window);
            Assert.Equal(1, plays);
            Assert.Equal(0, deletes);
            Assert.False(menu.IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MenuFlyout_SpaceDoesNotReopenLauncherOnRelease()
    {
        int plays = 0, actions = 0;
        var item = new MenuItem { Header = "An action" };
        item.Click += (_, _) => actions++;
        var flyout = new MenuFlyout { Items = { item } };
        var launcher = new Button { Content = "More", Flyout = flyout };
        var editor = new StackPanel { Focusable = true, Children = { launcher } };
        var window = new Window { Content = editor, Width = 400, Height = 200 };
        window.Show();
        try
        {
            var command = new RelayCommand(() => plays++);
            using var keys = new EditorTransportKeys(editor, () => command, () => editor.Focus());
            flyout.ShowAt(launcher);
            Dispatcher.UIThread.RunJobs();
            item.Focus();
            Assert.True(flyout.IsOpen);
            Down(window);
            Up(window);
            Assert.False(flyout.IsOpen);
            Assert.Equal(1, plays);
            Assert.Equal(0, actions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TextEntryAndSeparateDialogs_KeepTheirNormalSpaceBehavior()
    {
        int plays = 0, dialogClicks = 0;
        var text = new TextBox { Text = "Name" };
        var editor = new StackPanel { Focusable = true, Children = { text } };
        var window = new Window { Content = editor, Width = 400, Height = 200 };
        window.Show();
        try
        {
            var command = new RelayCommand(() => plays++);
            using var keys = new EditorTransportKeys(editor, () => command, () => editor.Focus());
            text.Focus();
            text.CaretIndex = text.Text.Length;
            Down(window);
            window.KeyTextInput(" ");
            Up(window);
            Assert.Equal("Name ", text.Text);
            Assert.Equal(0, plays);
            var button = new Button { Content = "Confirm" };
            button.Click += (_, _) => dialogClicks++;
            var dialog = new Window { Content = button, Width = 200, Height = 100 };
            dialog.Show();
            try
            {
                button.Focus();
                Down(dialog);
                Up(dialog);
                Assert.Equal(0, plays);
                Assert.Equal(1, dialogClicks);
            }
            finally { dialog.Close(); }
        }
        finally { window.Close(); }
    }
}
