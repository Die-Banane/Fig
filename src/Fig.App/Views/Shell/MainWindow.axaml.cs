using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Fig.App.Services;
using Fig.App.ViewModels;

namespace Fig.App.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<KeyGesture, ICommand> _typable = new();
    private AppViewModel? _app;

    public MainWindow()
    {
        InitializeComponent();
        // Shortcuts that are also typed characters (J, K, L, Shift+S) cannot be KeyBindings: a binding marks the
        // key handled, and a handled key never becomes text, so typing "shake" in a search box lost its "k".
        // They are routed here instead, after the focused control has had its chance, and never while typing.
        AddHandler(KeyDownEvent, OnTypableShortcut, RoutingStrategies.Bubble);
        DataContextChanged += (_, _) => Attach(DataContext as AppViewModel);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // a maximized window can't be resized, so its edges shouldn't offer to
        if (change.Property == WindowStateProperty)
            ResizeGrips.IsVisible = WindowState == WindowState.Normal;
    }

    private void Attach(AppViewModel? app)
    {
        if (_app is not null)
        {
            _app.Preferences.Changed -= ApplyShortcuts;
            _app.PropertyChanged -= OnAppPropertyChanged;
        }
        _app = app;
        if (_app is not null)
        {
            _app.Preferences.Changed += ApplyShortcuts;
            _app.PropertyChanged += OnAppPropertyChanged;
        }
        ApplyShortcuts();
    }

    private void OnAppPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppViewModel.ShortcutsSuspended))
            ApplyShortcuts();
    }

    /// <summary>Rebuilds every keyboard shortcut from the shortcut table and the user's changes to it.</summary>
    private void ApplyShortcuts()
    {
        KeyBindings.Clear();
        _typable.Clear();
        if (_app is null || _app.ShortcutsSuspended)
            return;
        foreach (var action in ShortcutCatalog.All)
        {
            if (ShortcutCatalog.Parse(ShortcutCatalog.GestureFor(_app.Preferences.Current, action)) is not { } gesture
                || action.Command(_app) is not { } command)
                continue;
            if (ShortcutCatalog.IsTypable(gesture))
                _typable[gesture] = command;
            else
                KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command });
        }
    }

    private void OnTypableShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _app is null)
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

        if (_typable.TryGetValue(new KeyGesture(e.Key, e.KeyModifiers), out var command) && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
