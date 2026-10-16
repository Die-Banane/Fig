using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;
using Fig.App.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.App.Services;

namespace Fig.App.ViewModels;

/// <summary>
/// The Preferences page: a rail of sections on the left, the open section's settings on the right.
/// Every change is written to <see cref="PreferenceService.Current"/> and saved at once.
/// </summary>
public sealed partial class PreferencesViewModel : ViewModelBase
{
    private readonly Action _close;

    /// <param name="openProjectCache">The open project's preview cache, which Storage must not clear; null when none is open.</param>
    /// <param name="suspendShortcuts">Takes the window's shortcuts off (true) and back on (false) while a key is recorded.</param>
    public PreferencesViewModel(PreferenceService service, Action close, string? openProjectCache = null,
        Action<bool>? suspendShortcuts = null)
    {
        _close = close;
        Pages =
        [
            new GeneralPageViewModel(service),
            new EditingPageViewModel(service),
            new PlaybackPageViewModel(service),
            new ExportPageViewModel(service),
            new KeyboardPageViewModel(service, suspendShortcuts),
            new RenderingPageViewModel(service),
            new StoragePageViewModel(service, openProjectCache),
        ];
        _selectedPage = Pages[0];
    }

    public IReadOnlyList<PreferencePageViewModel> Pages { get; }

    [ObservableProperty]
    private PreferencePageViewModel _selectedPage;

    /// <summary>Esc and the close button: a shortcut being recorded is cancelled first, the page closes second.</summary>
    [RelayCommand]
    private void Close()
    {
        if (Pages.OfType<KeyboardPageViewModel>().FirstOrDefault(p => p.Recording is not null) is { } keyboard)
        {
            keyboard.CancelRecording();
            return;
        }
        _close();
    }
}

/// <summary>One section of the Preferences page, editing one section of <see cref="Preferences"/>.</summary>
public abstract partial class PreferencePageViewModel : ViewModelBase
{
    private bool _loading;

    protected PreferencePageViewModel(PreferenceService service)
    {
        Service = service;
        // something else (a toolbar toggle) may change a value while the page is open
        Service.Changed += Reload;
    }

    protected PreferenceService Service { get; }

    public abstract string Title { get; }

    public abstract string Icon { get; }

    /// <summary>False for a page that shows things rather than settings (Storage), so it has no reset.</summary>
    public virtual bool CanReset => true;

    /// <summary>Puts this section's values back to the shipped defaults.</summary>
    [RelayCommand]
    public void ResetToDefaults()
    {
        Reset(Service.Current);
        Service.Save();
    }

    /// <summary>Reads this section's values from the preferences into the page's properties.</summary>
    protected abstract void Read(Preferences current);

    /// <summary>Replaces this section with a fresh default instance.</summary>
    protected abstract void Reset(Preferences current);

    /// <summary>Writes one change from the page and saves. Ignored while the page is reading values in.</summary>
    protected void Apply(Action<Preferences> change)
    {
        if (_loading)
            return;
        change(Service.Current);
        Service.Save();
    }

    protected void Reload()
    {
        _loading = true;
        try { Read(Service.Current); }
        finally { _loading = false; }
    }
}

public sealed partial class GeneralPageViewModel : PreferencePageViewModel
{
    public GeneralPageViewModel(PreferenceService service) : base(service) => Reload();

    public override string Title => "General";
    public override string Icon => "settings-2";

    [ObservableProperty] private double _autosaveSeconds;
    [ObservableProperty] private bool _askBeforeClosingUnsaved;

    protected override void Read(Preferences current)
    {
        AutosaveSeconds = current.General.AutosaveSeconds;
        AskBeforeClosingUnsaved = current.General.AskBeforeClosingUnsaved;
    }

    protected override void Reset(Preferences current) => current.General = new();

    partial void OnAutosaveSecondsChanged(double value) => Apply(p => p.General.AutosaveSeconds = value);
    partial void OnAskBeforeClosingUnsavedChanged(bool value) => Apply(p => p.General.AskBeforeClosingUnsaved = value);
}

public sealed partial class EditingPageViewModel : PreferencePageViewModel
{
    public EditingPageViewModel(PreferenceService service) : base(service) => Reload();

    public override string Title => "Editing";
    public override string Icon => "scissors";

    [ObservableProperty] private bool _snap;
    [ObservableProperty] private double _snapReachPx;
    [ObservableProperty] private bool _linkedSelection;
    [ObservableProperty] private int _undoSteps;

    protected override void Read(Preferences current)
    {
        Snap = current.Editing.Snap;
        SnapReachPx = current.Editing.SnapReachPx;
        LinkedSelection = current.Editing.LinkedSelection;
        UndoSteps = current.Editing.UndoSteps;
    }

    protected override void Reset(Preferences current) => current.Editing = new();

    partial void OnSnapChanged(bool value) => Apply(p => p.Editing.Snap = value);
    partial void OnSnapReachPxChanged(double value) => Apply(p => p.Editing.SnapReachPx = value);
    partial void OnLinkedSelectionChanged(bool value) => Apply(p => p.Editing.LinkedSelection = value);
    partial void OnUndoStepsChanged(int value) => Apply(p => p.Editing.UndoSteps = value);
}

public sealed partial class PlaybackPageViewModel : PreferencePageViewModel
{
    public PlaybackPageViewModel(PreferenceService service) : base(service) => Reload();

    public override string Title => "Playback";
    public override string Icon => "square-play";

    [ObservableProperty] private bool _audioScrub;
    [ObservableProperty] private bool _loop;

    protected override void Read(Preferences current)
    {
        AudioScrub = current.Playback.AudioScrub;
        Loop = current.Playback.Loop;
    }

    protected override void Reset(Preferences current) => current.Playback = new();

    partial void OnAudioScrubChanged(bool value) => Apply(p => p.Playback.AudioScrub = value);
    partial void OnLoopChanged(bool value) => Apply(p => p.Playback.Loop = value);
}

public sealed partial class ExportPageViewModel : PreferencePageViewModel
{
    public ExportPageViewModel(PreferenceService service) : base(service) => Reload();

    public override string Title => "Export";
    public override string Icon => "upload";

    [ObservableProperty] private int _quality;
    [ObservableProperty] private string _folder = "";

    protected override void Read(Preferences current)
    {
        Quality = current.Export.Quality;
        Folder = current.Export.Folder ?? "";
    }

    protected override void Reset(Preferences current) => current.Export = new();

    partial void OnQualityChanged(int value) => Apply(p => p.Export.Quality = value);
    partial void OnFolderChanged(string value) => Apply(p => p.Export.Folder = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
}

// ---- Keyboard ------------------------------------------------------------------------------------

/// <summary>Every rebindable shortcut, one row each; click a row's shortcut, then press the new keys.</summary>
public sealed partial class KeyboardPageViewModel : PreferencePageViewModel
{
    private readonly Action<bool> _suspendShortcuts;

    public KeyboardPageViewModel(PreferenceService service, Action<bool>? suspendShortcuts = null) : base(service)
    {
        _suspendShortcuts = suspendShortcuts ?? (_ => { });
        string? previous = null;
        foreach (var action in ShortcutCatalog.All)
        {
            Rows.Add(new ShortcutRowViewModel(this, action, showSection: action.Section != previous));
            previous = action.Section;
        }
        Reload();
    }

    public override string Title => "Keyboard";
    public override string Icon => "keyboard";

    public ObservableCollection<ShortcutRowViewModel> Rows { get; } = new();

    /// <summary>The row waiting for keys, if any.</summary>
    public ShortcutRowViewModel? Recording => Rows.FirstOrDefault(r => r.IsRecording);

    protected override void Read(Preferences current)
    {
        foreach (var row in Rows)
            row.Refresh(current);
    }

    protected override void Reset(Preferences current) => current.Keyboard = new();

    internal void StartRecording(ShortcutRowViewModel row)
    {
        foreach (var other in Rows)
            other.IsRecording = ReferenceEquals(other, row);
        _suspendShortcuts(true);
    }

    public void CancelRecording()
    {
        foreach (var row in Rows)
            row.IsRecording = false;
        _suspendShortcuts(false);
    }

    /// <summary>
    /// Gives the recording row these keys. Escape cancels. Keys another action already has are taken from it,
    /// so no two actions ever share a shortcut.
    /// </summary>
    public void Assign(Key key, KeyModifiers modifiers)
    {
        if (Recording is not { } row)
            return;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin)
            return;   // a modifier on its own: wait for the key that goes with it
        CancelRecording();
        if (key == Key.Escape && modifiers == KeyModifiers.None)
            return;

        var gesture = new KeyGesture(key, modifiers).ToString();
        string? takenFrom = null;
        Apply(p =>
        {
            foreach (var other in Rows.Where(r => !ReferenceEquals(r, row)))
                if (ShortcutCatalog.GestureFor(p, other.Action) == gesture)
                {
                    p.Keyboard.Shortcuts[other.Action.Id] = "";
                    takenFrom = other.Label;
                }
            SetGesture(p, row.Action, gesture);
        });
        row.Note = takenFrom is null ? null : $"Taken from \"{takenFrom}\", which now has no shortcut.";
    }

    internal void SetGesture(ShortcutAction action, string gesture) => Apply(p => SetGesture(p, action, gesture));

    private static void SetGesture(Preferences p, ShortcutAction action, string gesture)
    {
        if (gesture == action.DefaultGesture)
            p.Keyboard.Shortcuts.Remove(action.Id);   // only changed shortcuts are stored
        else
            p.Keyboard.Shortcuts[action.Id] = gesture;
    }
}

public sealed partial class ShortcutRowViewModel : ViewModelBase
{
    private readonly KeyboardPageViewModel _page;

    public ShortcutRowViewModel(KeyboardPageViewModel page, ShortcutAction action, bool showSection)
    {
        _page = page;
        Action = action;
        ShowSection = showSection;
    }

    public ShortcutAction Action { get; }
    public string Label => Action.Label;
    public string Section => Action.Section;

    /// <summary>The first row of each section carries its heading.</summary>
    public bool ShowSection { get; }

    [ObservableProperty] private string _gestureText = "";
    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private string? _note;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private bool _isRecording;

    /// <summary>What the shortcut button shows: the keys, or a prompt while recording.</summary>
    public string ButtonText => IsRecording ? "Press keys…" : GestureText;

    partial void OnGestureTextChanged(string value) => OnPropertyChanged(nameof(ButtonText));

    internal void Refresh(Preferences current)
    {
        GestureText = ShortcutCatalog.Display(ShortcutCatalog.GestureFor(current, Action));
        IsModified = current.Keyboard.Shortcuts.ContainsKey(Action.Id);
    }

    [RelayCommand]
    private void Record()
    {
        Note = null;
        _page.StartRecording(this);
    }

    /// <summary>Leaves the action with no shortcut.</summary>
    [RelayCommand]
    private void Clear()
    {
        Note = null;
        _page.SetGesture(Action, "");
    }

    /// <summary>Back to the shortcut it ships with.</summary>
    [RelayCommand]
    private void Reset()
    {
        Note = null;
        _page.SetGesture(Action, Action.DefaultGesture);
    }
}

// ---- Rendering -----------------------------------------------------------------------------------

public sealed record RendererOption(RendererChoice Value, string Label, string Note);

public sealed partial class RenderingPageViewModel : PreferencePageViewModel
{
    public RenderingPageViewModel(PreferenceService service) : base(service)
    {
        Reload();
        RefreshCanvasStatus();
    }

    public override string Title => "Rendering";
    public override string Icon => "gauge";

    public IReadOnlyList<RendererOption> Options { get; } =
    [
        new(RendererChoice.Automatic, "Automatic", "The GPU when this window's canvas is GPU-backed, the CPU otherwise."),
        new(RendererChoice.Gpu, "GPU", "Always the GPU shaders. On a software canvas they run on the CPU, slowly."),
        new(RendererChoice.Cpu, "CPU", "Always the CPU. Slower with effects, but uses no graphics driver."),
    ];

    [ObservableProperty] private RendererOption? _selected;
    [ObservableProperty] private string _canvasStatus = "";

    protected override void Read(Preferences current)
        => Selected = Options.FirstOrDefault(o => o.Value == current.Rendering.Renderer) ?? Options[0];

    protected override void Reset(Preferences current) => current.Rendering = new();

    partial void OnSelectedChanged(RendererOption? value)
    {
        if (value is not null)
            Apply(p => p.Rendering.Renderer = value.Value);
    }

    [RelayCommand]
    private void RefreshCanvasStatus() => CanvasStatus = GpuJobHost.Current?.IsGpuBacked switch
    {
        true => "This window draws on the GPU.",
        false => "This window draws in software: there is no GPU-backed canvas.",
        null => "Not known yet: the window has not drawn a frame on the GPU path.",
    };
}

// ---- Storage -------------------------------------------------------------------------------------

/// <summary>One of Fig's folders, with how much it holds.</summary>
public sealed partial class StorageRow : ViewModelBase
{
    public StorageRow(string label, string note, string path)
    {
        Label = label;
        Note = note;
        Path = path;
    }

    public string Label { get; }
    public string Note { get; }
    public string Path { get; }

    [ObservableProperty] private string _size = "…";

    [RelayCommand]
    private void Open()
    {
        try
        {
            Directory.CreateDirectory(Path);
            Process.Start(new ProcessStartInfo { FileName = Path, UseShellExecute = true });
        }
        catch (Exception)
        {
            // no file manager to hand it to; the path is shown on the row anyway
        }
    }
}

public sealed partial class StoragePageViewModel : PreferencePageViewModel
{
    private readonly string? _openProjectCache;

    public StoragePageViewModel(PreferenceService service, string? openProjectCache) : base(service)
    {
        _openProjectCache = openProjectCache;
        Folders =
        [
            new("Projects", "Every project: its timeline and its previews.", AppPaths.ProjectsDir),
            new("Effect presets", "Presets saved from an effect's right-click menu.", AppPaths.PresetsDir),
            new("Templates", "Timelines saved as templates.", AppPaths.TemplatesDir),
            new("Settings", "Preferences, key bindings and the panel layout.", AppPaths.DataDir),
            new("Logs", "Error reports, for when something goes wrong.", AppPaths.LogDir),
        ];
        _ = MeasureAsync();
    }

    public override string Title => "Storage";
    public override string Icon => "hard-drive";
    public override bool CanReset => false;

    public IReadOnlyList<StorageRow> Folders { get; }

    [ObservableProperty] private string _cacheSize = "…";
    [ObservableProperty] private string? _cacheNote;

    protected override void Read(Preferences current) { }

    protected override void Reset(Preferences current) { }

    /// <summary>Every project's preview cache (thumbnails, filmstrips, waveforms, proxies).</summary>
    private static IEnumerable<string> CacheFolders()
        => Directory.Exists(AppPaths.ProjectsDir)
            ? Directory.EnumerateDirectories(AppPaths.ProjectsDir)
                .Select(project => System.IO.Path.Combine(project, "media.cache"))
                .Where(Directory.Exists)
            : [];

    private async Task MeasureAsync()
    {
        var (sizes, cache) = await Task.Run(() =>
            (Folders.Select(f => FolderBytes(f.Path)).ToList(), CacheFolders().Sum(FolderBytes)));
        Dispatcher.UIThread.Post(() =>
        {
            for (var i = 0; i < Folders.Count; i++)
                Folders[i].Size = FormatBytes(sizes[i]);
            CacheSize = FormatBytes(cache);
        });
    }

    /// <summary>
    /// Deletes the previews and proxies of every project that isn't open. They are rebuilt the next time a
    /// project is opened, so nothing is lost but the time to rebuild them.
    /// </summary>
    [RelayCommand]
    private async Task ClearCache()
    {
        var freed = await Task.Run(() =>
        {
            long total = 0;
            foreach (var folder in CacheFolders())
            {
                if (_openProjectCache is not null
                    && string.Equals(System.IO.Path.GetFullPath(folder), System.IO.Path.GetFullPath(_openProjectCache), StringComparison.Ordinal))
                    continue;
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var size = new FileInfo(file).Length;
                        File.Delete(file);
                        total += size;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return total;
        });
        CacheNote = _openProjectCache is null
            ? $"Freed {FormatBytes(freed)}."
            : $"Freed {FormatBytes(freed)}. The open project's previews were kept.";
        await MeasureAsync();
    }

    private static long FolderBytes(string path)
    {
        if (File.Exists(path))
            return new FileInfo(path).Length;
        if (!Directory.Exists(path))
            return 0;
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(file).Length; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return total;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
    };
}

