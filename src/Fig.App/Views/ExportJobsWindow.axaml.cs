using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Fig.App.Views;

/// <summary>A small, non-modal window listing export jobs and their progress.</summary>
public partial class ExportJobsWindow : Window
{
    public ExportJobsWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
