using Avalonia.Controls;
using Avalonia.Input;

namespace Fig.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    /// <summary>"/" jumps to search, unless the user is already typing in a text box.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        if (e.Key == Key.Escape && SearchBox.IsFocused && !string.IsNullOrEmpty(SearchBox.Text))
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Oem2 && e.KeyModifiers == KeyModifiers.None
                 && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }
}
