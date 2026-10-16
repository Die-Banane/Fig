using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Fig.App.Views;

/// <summary>
/// The window's own title bar: the brand, the menus, what is open, project actions and the window buttons.
/// The window has no system decorations, so this is the only chrome there is.
/// </summary>
public partial class TitleBar : UserControl
{
    public static readonly DirectProperty<TitleBar, Window?> OwnerProperty =
        AvaloniaProperty.RegisterDirect<TitleBar, Window?>(nameof(Owner), o => o.Owner);

    private Window? _owner;

    /// <summary>The window this bar belongs to; passed to commands that open dialogs.</summary>
    public Window? Owner
    {
        get => _owner;
        private set => SetAndRaise(OwnerProperty, ref _owner, value);
    }

    public TitleBar()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Owner = TopLevel.GetTopLevel(this) as Window;
        if (Owner is not null)
        {
            Owner.PropertyChanged += OnWindowPropertyChanged;
            UpdateMaximizeIcon();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Owner is not null)
            Owner.PropertyChanged -= OnWindowPropertyChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
            UpdateMaximizeIcon();
    }

    private void UpdateMaximizeIcon()
        => MaximizeIcon.Icon = Owner?.WindowState == WindowState.Maximized ? "copy" : "square";

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        if (Owner is not null)
            Owner.WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object? sender, RoutedEventArgs e)
    {
        if (Owner is not null)
            Owner.WindowState = Owner.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Owner?.Close();

    private void Exit_Click(object? sender, RoutedEventArgs e) => Owner?.Close();

    private void Shortcuts_Click(object? sender, RoutedEventArgs e)
    {
        if (Owner is not null)
            ShortcutsDialog.Show(Owner);
    }
}
