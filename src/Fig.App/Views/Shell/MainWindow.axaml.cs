using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Fig.App.ViewModels;

namespace Fig.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Shortcuts on plain letters cannot be KeyBindings: a binding marks the key handled, and a handled
        // key never becomes text, so typing "shake" in a search box lost its "k". They are routed here
        // instead, after the focused control has had its chance, and never while the user is typing.
        AddHandler(KeyDownEvent, OnPlainShortcut, RoutingStrategies.Bubble);
    }

    private void OnPlainShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not AppViewModel app)
            return;
        if (e.KeyModifiers != KeyModifiers.None && e.KeyModifiers != KeyModifiers.Shift)
            return;
        if (e.Source is Visual source)
        {
            var chain = source.GetSelfAndVisualAncestors().ToList();
            if (chain.OfType<TextBox>().Any())
                return;
            // full-screen tools such as the effect graph editor own their keys
            if (chain.OfType<Control>().Any(c => c.Classes.Contains("no-transport")))
                return;
        }

        ICommand? command = (e.Key, e.KeyModifiers) switch
        {
            (Key.L, KeyModifiers.None) => app.Editor.ShuttleForwardCommand,
            (Key.J, KeyModifiers.None) => app.Editor.ShuttleBackCommand,
            (Key.K, KeyModifiers.None) => app.Editor.ShuttleStopCommand,
            (Key.S, KeyModifiers.Shift) => app.Editor.ToggleAudioScrubCommand,
            _ => null,
        };
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
