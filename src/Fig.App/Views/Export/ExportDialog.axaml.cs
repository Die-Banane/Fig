using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Fig.App.ViewModels;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.App.Views;

public partial class ExportDialog : Window
{
    public ExportDialogViewModel Model { get; }

    public ExportDialog()
        : this(30, 1920, 1080)
    {
    }

    public ExportDialog(double fps, int defaultWidth, int defaultHeight, TimelineModel? timeline = null,
        string projectName = "", bool chapterMode = false, Fig.App.Services.ExportPreferences? defaults = null)
    {
        Model = new ExportDialogViewModel(fps, defaultWidth, defaultHeight, timeline) { ProjectName = projectName };
        if (defaults is not null)
            Model.Quality = defaults.Quality;
        var videos = defaults?.Folder is { Length: > 0 } folder && Directory.Exists(folder)
            ? folder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrEmpty(videos))
            videos = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var safeName = string.IsNullOrWhiteSpace(projectName) ? "export" : projectName.Trim();
        Model.ChapterMode = chapterMode;
        Model.OutputPath = chapterMode ? Path.Combine(videos, safeName) : Path.Combine(videos, safeName + ".mp4");
        DataContext = Model;
        InitializeComponent();
        Title = chapterMode ? "Export marked ranges" : "Export timeline";
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.ChapterMode)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Export videos to",
                AllowMultiple = false,
            });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } folder)
                Model.OutputPath = folder;
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to",
            SuggestedFileName = Path.GetFileName(Model.OutputPath) is { Length: > 0 } n ? n : "export.mp4",
            DefaultExtension = "mp4",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("MP4 (H.264 / AAC)") { Patterns = new[] { "*.mp4" } },
            },
        });
        if (file is not null)
            Model.OutputPath = file.TryGetLocalPath() ?? "";
    }

    private async void CreateRanges_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.CreateRangesFromBookmarks is null)
            return;
        if (await Model.CreateRangesFromBookmarks())
            Model.RebuildChapters();
    }

    private void ClearColor_Click(object? sender, RoutedEventArgs e) => Model.SelectedColor = null;

    private void ToggleSettings_Click(object? sender, RoutedEventArgs e) => Model.ShowSettings = !Model.ShowSettings;

    private void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (!Model.CanExport)
            return;
        Close(Model.CreateOptions());
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
