using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.App.Services;
using Fig.App.Views;
using Fig.Core.Input;
using Fig.Core.Media;
using Fig.Core.Project;
using Fig.Core.Templates;
using Fig.Core.Timeline;
using ProjectModel = Fig.Core.Project.Project;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// Exporting, and the export jobs panel.
/// </summary>
public partial class EditorViewModel
{
    /// <summary>Runs timeline exports as background jobs (progress shown in the jobs popup).</summary>
    public ExportJobRunner Exports { get; }

    [ObservableProperty]
    private bool _isJobsOpen;

    public bool HasExportJobs => Exports.Jobs.Count > 0;
    public bool ShowEmptyJobs => !HasExportJobs;
    public int ActiveExportCount => Exports.Jobs.Count(j => j.IsActive);

    /// <summary>
    /// Opens the export dialog (resolution, fps, quality, output path), then queues the export
    /// as a background job visible in the jobs popup.
    /// </summary>
    [RelayCommand]
    private Task ExportAsync(Window? owner) => RunExportAsync(owner, chapterMode: false);

    /// <summary>Exports each marked range of the timeline as its own video.</summary>
    [RelayCommand]
    private Task ExportMarkedRangesAsync(Window? owner) => RunExportAsync(owner, chapterMode: true);

    private async Task RunExportAsync(Window? owner, bool chapterMode)
    {
        if (Project is null || Editor is null)
            return;
        owner ??= MainWindow;
        if (owner is null)
            return;

        var timeline = Editor.Document;
        var settings = Project.Export;
        // "Match project" is the frame shape's size when one is picked
        var (matchWidth, matchHeight) = timeline.Shape == FrameShape.Auto ? (settings.Width, settings.Height) : Preview.FrameSize;
        var dialog = new ExportDialog(timeline.Rate.Fps, matchWidth, matchHeight, timeline, Project.Name, chapterMode,
            Preferences.Current.Export);
        dialog.Model.CreateRangesFromBookmarks = () => CreateRangesFromBookmarksAsync(dialog);
        var options = await dialog.ShowDialog<ExportOptions?>(owner);
        if (options is null)
            return;

        var width = Math.Max(2, options.Width & ~1);
        var height = Math.Max(2, options.Height & ~1);
        var crf = Math.Clamp(options.Crf, 0, 51);
        var encoder = options.Encoder;
        Remember(p => p.Export.Encoder = encoder);

        if (options.Chapters is { Count: > 0 } chapters)
        {
            System.IO.Directory.CreateDirectory(options.OutputPath);
            foreach (var chapter in chapters)
                Exports.Enqueue(System.IO.Path.Combine(options.OutputPath, chapter.FileName), width, height, crf,
                    Project, timeline, chapter.StartSec, chapter.EndSec, encoder, options.Turn);
            IsJobsOpen = true;
            Notify?.Invoke($"Exporting {chapters.Count} video{(chapters.Count == 1 ? "" : "s")} — you can keep editing");
            return;
        }

        Exports.Enqueue(options.OutputPath, width, height, crf, Project, timeline, encoder: encoder, turn: options.Turn);
        IsJobsOpen = true;
        Notify?.Invoke($"Export started: {System.IO.Path.GetFileName(options.OutputPath)} — you can keep editing");
    }

    [RelayCommand]
    private void ToggleJobs() => IsJobsOpen = !IsJobsOpen;
}
