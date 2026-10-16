using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Fig.App.ViewModels;

namespace Fig.App.Views;

public partial class PreferencesView : UserControl
{
    public PreferencesView()
    {
        InitializeComponent();
        // while a shortcut row is recording, the next key press is the new shortcut, not a command
        AddHandler(KeyDownEvent, OnRecordKey, RoutingStrategies.Tunnel);
    }

    private void OnRecordKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is PreferencesViewModel { SelectedPage: KeyboardPageViewModel { Recording: not null } page })
        {
            page.Assign(e.Key, e.KeyModifiers);
            e.Handled = true;
        }
    }
}
