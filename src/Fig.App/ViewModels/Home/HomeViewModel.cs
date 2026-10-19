using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.App.Services;
using Fig.Core.Project;

namespace Fig.App.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    public static readonly string[] SortOptions = ["Last edited", "Name", "Duration"];

    private readonly ProjectStore _store;
    private IReadOnlyList<ProjectSummary> _all = Array.Empty<ProjectSummary>();

    /// <summary>The projects currently shown (filtered by search, ordered by <see cref="SortBy"/>).</summary>
    public ObservableCollection<ProjectItemViewModel> Projects { get; } = new();

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _sortBy = SortOptions[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsList))]
    private bool _isGrid = true;

    public bool IsList => !IsGrid;

    /// <summary>Nothing to show (either no projects at all, or the search matched nothing).</summary>
    public bool IsEmpty => Projects.Count == 0;
    /// <summary>The store holds no projects: show the first-run "create your first project" state.</summary>
    public bool HasNoProjects => _all.Count == 0;
    public bool HasProjects => _all.Count > 0;
    public bool NoSearchResults => HasProjects && IsEmpty;
    public bool ShowProjects => !IsEmpty;
    public string CountText => HasProjects && Projects.Count != _all.Count
        ? $"{Projects.Count} of {_all.Count} projects"
        : _all.Count == 1 ? "1 project" : $"{_all.Count} projects";

    public event Action<Project>? ProjectOpened;
    public event Action<Project>? ProjectCreated;
    public Action<string>? Notify { get; set; }

    public HomeViewModel(ProjectStore store)
    {
        _store = store;
        Refresh();
    }

    public void Refresh()
    {
        _all = _store.ListProjects();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSortByChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchText?.Trim() ?? "";
        IEnumerable<ProjectSummary> items = _all;
        if (query.Length > 0)
            items = items.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        items = SortBy switch
        {
            "Name" => items.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            "Duration" => items.OrderByDescending(p => p.DurationSec).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => items.OrderByDescending(p => p.UpdatedAt),
        };

        Projects.Clear();
        foreach (var p in items)
            Projects.Add(new ProjectItemViewModel(p, this));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoProjects));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(NoSearchResults));
        OnPropertyChanged(nameof(ShowProjects));
        OnPropertyChanged(nameof(CountText));
    }

    [RelayCommand]
    private void ShowGrid() => IsGrid = true;

    [RelayCommand]
    private void ShowList() => IsGrid = false;

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    [RelayCommand]
    private void OpenProject(ProjectItemViewModel? item)
    {
        if (item is null)
            return;

        var project = _store.LoadProject(item.Id);
        if (project is null)
        {
            Notify?.Invoke($"Couldn't open \"{item.Name}\" — its project file is missing.");
            Refresh();
            return;
        }

        ProjectOpened?.Invoke(project);
    }

    [RelayCommand]
    private async Task RenameProjectAsync(ProjectItemViewModel? item)
    {
        if (item is null)
            return;
        var name = await Dialogs.PromptTextAsync(null, "Rename project", "Project name", item.Name, "Rename", item.Name);
        if (name is null || name == item.Name)
            return;
        if (!_store.RenameProject(item.Id, name))
            Notify?.Invoke($"Couldn't rename \"{item.Name}\".");
        Refresh();
    }

    [RelayCommand]
    private void ShowInFolder(ProjectItemViewModel? item)
    {
        if (item is null)
            return;
        var folder = _store.ProjectDirectory(item.Id);
        try
        {
            var launcher = OperatingSystem.IsWindows() ? "explorer" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            Process.Start(new ProcessStartInfo(launcher, $"\"{folder}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"Couldn't open the folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteProjectAsync(ProjectItemViewModel? item)
    {
        if (item is null)
            return;
        var confirmed = await Dialogs.ConfirmAsync(null, "Delete project?",
            $"\"{item.Name}\" and its project data will be permanently deleted. Your original media files are not touched.",
            "Delete", danger: true);
        if (!confirmed)
            return;
        _store.DeleteProject(item.Id);
        Refresh();
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        var name = await Dialogs.PromptTextAsync(null, "New project", "Project name", "Untitled", "Create");
        if (name is null)
            return;

        var id = _store.CreateProject(name);
        var project = _store.LoadProject(id);
        Refresh();
        if (project is not null)
            ProjectCreated?.Invoke(project);
    }

    /// <summary>
    /// Imports one or more OpenTimelineIO (.otio) projects: parses them into the fig
    /// model, saves each as a project in the store, and opens the first one.
    /// </summary>
    [RelayCommand]
    private async Task ImportOtioAsync()
    {
        var owner = Dialogs.MainWindow;
        if (owner is null)
            return;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Project",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Fig package or OpenTimelineIO")
                {
                    Patterns = new[] { "*" + ProjectStore.PackageExtension, "*.otio", "*.otioz", "*.json" },
                },
                new FilePickerFileType("Fig package") { Patterns = new[] { "*" + ProjectStore.PackageExtension } },
                new FilePickerFileType("OpenTimelineIO") { Patterns = new[] { "*.otio", "*.otioz", "*.json" } },
            },
        });

        Project? first = null;
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path is null)
                continue;

            if (path.EndsWith(ProjectStore.PackageExtension, StringComparison.OrdinalIgnoreCase))
            {
                // a shared project: unpacked as a new project of its own
                try
                {
                    var unpacked = await Task.Run(() => _store.LoadProject(_store.Unpack(path)));
                    if (unpacked is not null)
                    {
                        Notify?.Invoke($"Opened \"{unpacked.Name}\" from {Path.GetFileName(path)}");
                        first ??= unpacked;
                    }
                }
                catch (Exception ex)
                {
                    Notify?.Invoke($"Couldn't open {Path.GetFileName(path)}: {ex.Message}");
                }
                continue;
            }

            try
            {
                var result = OtioImporter.ImportFromFile(path);
                var project = result.Project;
                project.Id = _store.CreateProject(project.Name);
                _store.SaveProject(project);

                Notify?.Invoke(SummarizeImport(Path.GetFileName(path), result));
                first ??= project;
            }
            catch (Exception ex)
            {
                Notify?.Invoke($"Import failed for {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        Refresh();

        if (first is not null)
            ProjectOpened?.Invoke(first);
    }

    private static string SummarizeImport(string name, OtioImportResult result)
    {
        var tracks = result.Project.Timelines.Sum(t => t.Tracks.Count);
        var summary = $"Imported \"{name}\": {result.ClipsImported} clips, {tracks} track(s), "
                    + $"{result.MarkersImported} marker(s), {result.EffectsImported} effect(s), "
                    + $"{result.TransitionsImported} transition(s)";
        if (result.Warnings.Count > 0)
            summary += $" ({result.Warnings.Count} warning(s))";
        return summary;
    }
}
