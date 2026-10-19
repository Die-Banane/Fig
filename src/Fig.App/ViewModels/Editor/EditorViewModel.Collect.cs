using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Project;

namespace Fig.App.ViewModels;

/// <summary>Collect Sources: copies the project's media into its folder, so the folder can just be zipped up.</summary>
public partial class EditorViewModel
{
    [RelayCommand]
    private async Task CollectSourcesAsync()
    {
        if (ProjectManager is not { } manager || Project is null || _store is null)
            return;
        StatusHint = "Collecting sources…";
        CollectResult result;
        try
        {
            result = await Task.Run(() => manager.CollectSources(
                p => Dispatcher.UIThread.Post(() => StatusHint = $"Collecting sources… {p:P0}")));
        }
        catch (Exception e)
        {
            StatusHint = "";
            Notify?.Invoke($"Couldn't collect sources: {e.Message}");
            return;
        }

        Preview.InvalidateSources();   // the same footage, read from its new place
        _store.SaveProject(Project);
        RefreshMediaHomes();
        StatusHint = "";
        var folder = _store.ProjectDirectory(Project.Id);
        var missing = result.Missing.Count == 0 ? "" : $" · {result.Missing.Count} missing: {string.Join(", ", result.Missing)}";
        Notify?.Invoke(result.Copied == 0 && result.Missing.Count == 0
            ? "Everything is already in the project folder"
            : $"Copied {result.Copied} file{(result.Copied == 1 ? "" : "s")} into the project folder{missing}");

    }

    /// <summary>
    /// Save as package: the project and every file it uses in one <c>.figpck</c>, to send to someone (they open it
    /// with Import Project on the home screen).
    /// </summary>
    [RelayCommand]
    private async Task SaveAsPackageAsync()
    {
        if (Project is null || _store is null || MainWindow is not { } owner)
            return;
        var file = await owner.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save as package",
            SuggestedFileName = Project.Name + ProjectStore.PackageExtension,
            DefaultExtension = ProjectStore.PackageExtension.TrimStart('.'),
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("Fig package") { Patterns = ["*" + ProjectStore.PackageExtension] }],
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;
        if (!path.EndsWith(ProjectStore.PackageExtension, StringComparison.OrdinalIgnoreCase))
            path += ProjectStore.PackageExtension;

        _store.SaveProject(Project);
        var project = Project;
        StatusHint = "Packing…";
        try
        {
            var result = await Task.Run(() => _store.Pack(project, path,
                p => Dispatcher.UIThread.Post(() => StatusHint = $"Packing… {p:P0}")));
            var missing = result.Missing.Count == 0 ? "" : $" · {result.Missing.Count} missing, left out: {string.Join(", ", result.Missing)}";
            Notify?.Invoke($"Saved {Path.GetFileName(path)} with {result.Files} file{(result.Files == 1 ? "" : "s")}{missing}");
        }
        catch (Exception e)
        {
            Notify?.Invoke($"Couldn't save the package: {e.Message}");
        }
        finally
        {
            StatusHint = "";
        }
    }

    /// <summary>Marks each media file as in the project's folder or outside it (the badge on its card).</summary>
    private void RefreshMediaHomes()
    {
        if (Project is null || _store is null)
            return;
        var folder = Path.GetFullPath(_store.ProjectDirectory(Project.Id)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var asset in Media)
            asset.InProject = !string.IsNullOrEmpty(asset.Url) && Path.GetFullPath(asset.Url).StartsWith(folder, StringComparison.Ordinal);
    }

    /// <summary>Shows a folder in the system's file manager.</summary>
    private static void OpenFolder(string folder)
    {
        try
        {
            var launcher = OperatingSystem.IsWindows() ? "explorer" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(launcher, $"\"{folder}\"") { UseShellExecute = false });
        }
        catch
        {
            // no file manager: the message says where it is
        }
    }
}
