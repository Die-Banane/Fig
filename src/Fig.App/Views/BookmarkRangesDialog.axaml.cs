using Avalonia.Controls;
using Avalonia.Interactivity;
using Fig.App.ViewModels;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.App.Views;

public partial class BookmarkRangesDialog : Window
{
    public BookmarkRangesViewModel Model { get; }

    public BookmarkRangesDialog() : this(new TimelineModel())
    {
    }

    public BookmarkRangesDialog(TimelineModel timeline)
    {
        Model = new BookmarkRangesViewModel(timeline);
        DataContext = Model;
        InitializeComponent();
    }

    private void Create_Click(object? sender, RoutedEventArgs e) => Close(Model.IncludeSequenceBounds);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
