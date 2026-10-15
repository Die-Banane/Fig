using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Fig.App.Services;

/// <summary>Space belongs to transport in the editor, including its separate popup roots.</summary>
internal sealed class EditorTransportKeys : IDisposable
{
    private readonly Control _editor;
    private readonly TopLevel _owner;
    private readonly Func<ICommand?> _command;
    private readonly Action _focusTimeline;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly HashSet<ContextMenu> _menus = new();
    private bool _spaceDown;

    public EditorTransportKeys(Control editor, Func<ICommand?> command, Action focusTimeline)
    {
        _editor = editor;
        _owner = TopLevel.GetTopLevel(editor) ?? throw new InvalidOperationException("Editor must be attached first");
        _command = command;
        _focusTimeline = focusTimeline;
        // Popup event routes can end at their presenter rather than include a TopLevel.
        // Observe input elements, then scope strictly to this editor's owning window.
        _subscriptions.Add(InputElement.KeyDownEvent.AddClassHandler<InputElement>(OnKeyDown, RoutingStrategies.Tunnel));
        _subscriptions.Add(InputElement.KeyUpEvent.AddClassHandler<InputElement>(OnKeyUp, RoutingStrategies.Tunnel, true));
        _subscriptions.Add(MenuBase.OpenedEvent.AddClassHandler<ContextMenu>((menu, _) =>
        {
            if (Owns(TopLevel.GetTopLevel(menu))) _menus.Add(menu);
        }));
        _subscriptions.Add(MenuBase.ClosedEvent.AddClassHandler<ContextMenu>((menu, _) => _menus.Remove(menu)));
        if (_owner is WindowBase window) window.Deactivated += OnDeactivated;
    }

    private bool Owns(TopLevel? root)
    {
        while (root is PopupRoot popup) root = popup.ParentTopLevel;
        return ReferenceEquals(root, _owner);
    }

    private void OnKeyDown(InputElement sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None || !_editor.IsVisible
            || e.Source is not Visual source || !Owns(TopLevel.GetTopLevel(source)))
            return;
        // Text entry (including NumericUpDown's TextBox) keeps ordinary spaces.
        if (source.GetSelfAndVisualAncestors().OfType<TextBox>().Any()) return;

        e.Handled = true;
        if (_spaceDown) return; // holding Space must not oscillate play/pause
        _spaceDown = true;
        foreach (var menu in _menus.ToArray()) menu.Close();
        foreach (var control in _owner.GetVisualDescendants().OfType<Control>().ToArray())
        {
            if (control is MenuBase menu) menu.Close();
            if (control is Button button && button.Flyout?.IsOpen == true) button.Flyout.Hide();
            if (control.ContextFlyout?.IsOpen == true) control.ContextFlyout.Hide();
        }
        _focusTimeline();
        var command = _command();
        if (command?.CanExecute(null) == true) command.Execute(null);
    }

    private void OnKeyUp(InputElement sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !_spaceDown || e.Source is not Visual source || !Owns(TopLevel.GetTopLevel(source))) return;
        // Swallow release too: buttons and menu launchers can otherwise reopen on key-up.
        _spaceDown = false;
        e.Handled = true;
    }

    private void OnDeactivated(object? sender, EventArgs e) => _spaceDown = false;

    public void Dispose()
    {
        if (_owner is WindowBase window) window.Deactivated -= OnDeactivated;
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
        _menus.Clear();
        _spaceDown = false;
    }
}
