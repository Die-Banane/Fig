using System;
using System.Collections.Generic;
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

    public PreferencesViewModel(PreferenceService service, Action close)
    {
        _close = close;
        Pages =
        [
            new GeneralPageViewModel(service),
            new EditingPageViewModel(service),
            new PlaybackPageViewModel(service),
            new ExportPageViewModel(service),
        ];
        _selectedPage = Pages[0];
    }

    public IReadOnlyList<PreferencePageViewModel> Pages { get; }

    [ObservableProperty]
    private PreferencePageViewModel _selectedPage;

    [RelayCommand]
    private void Close() => _close();
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
