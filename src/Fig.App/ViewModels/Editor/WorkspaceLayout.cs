using System;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.App.Services;

namespace Fig.App.ViewModels;

/// <summary>
/// The editor's panel arrangement: how wide the side panels are (as fractions of the
/// window, so the layout scales with it), which are collapsed, and whether the preview
/// is focused. Remembered between sessions.
/// </summary>
public sealed partial class WorkspaceLayout : ObservableObject
{
    public const double DefaultLeftFraction = 0.20;
    public const double DefaultRightFraction = 0.22;
    public const double DefaultTimelineFraction = 0.44;

    public const double MinFraction = 0.12;
    public const double MaxSideFraction = 0.40;

    private readonly string? _path;

    /// <summary>Width of the library panel as a fraction of the workspace width.</summary>
    public double LeftFraction { get; private set; } = DefaultLeftFraction;

    /// <summary>Width of the inspector as a fraction of the workspace width.</summary>
    public double RightFraction { get; private set; } = DefaultRightFraction;

    /// <summary>Height of the timeline as a fraction of the workspace height.</summary>
    public double TimelineFraction { get; private set; } = DefaultTimelineFraction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLeftVisible))]
    [NotifyPropertyChangedFor(nameof(IsDefault))]
    private bool _isLeftCollapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRightVisible))]
    [NotifyPropertyChangedFor(nameof(IsDefault))]
    private bool _isRightCollapsed;

    /// <summary>Preview takes the stage: both side panels tuck away until restored.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLeftVisible))]
    [NotifyPropertyChangedFor(nameof(IsRightVisible))]
    [NotifyPropertyChangedFor(nameof(IsDefault))]
    [NotifyPropertyChangedFor(nameof(FocusMenuText))]
    private bool _isFocusPreview;

    public bool IsLeftVisible => !IsLeftCollapsed && !IsFocusPreview;
    public bool IsRightVisible => !IsRightCollapsed && !IsFocusPreview;
    public bool IsDefault => !IsLeftCollapsed && !IsRightCollapsed && !IsFocusPreview;
    public string FocusMenuText => IsFocusPreview ? "Restore layout" : "Focus preview";

    /// <summary>Raised whenever the arrangement changes so the view can re-apply it.</summary>
    public event Action? Changed;

    public WorkspaceLayout(string? path = null)
    {
        _path = path;
        _quiet = true;
        Load();
        _quiet = false;
    }

    partial void OnIsLeftCollapsedChanged(bool value) => Commit();
    partial void OnIsRightCollapsedChanged(bool value) => Commit();
    partial void OnIsFocusPreviewChanged(bool value) => Commit();

    /// <summary>Stores new sizes after a splitter drag (clamped to sensible ranges).</summary>
    public void SetFractions(double? left, double? right, double? timeline)
    {
        if (left is { } l && l > 0) LeftFraction = Math.Clamp(l, MinFraction, MaxSideFraction);
        if (right is { } r && r > 0) RightFraction = Math.Clamp(r, MinFraction, MaxSideFraction);
        if (timeline is { } t && t > 0) TimelineFraction = Math.Clamp(t, 0.2, 0.75);
        Commit();
    }

    [RelayCommand]
    private void ToggleLeft()
    {
        if (IsFocusPreview)
        {
            IsFocusPreview = false;
            IsRightCollapsed = true;
            return;
        }
        IsLeftCollapsed = !IsLeftCollapsed;
    }

    [RelayCommand]
    private void ToggleRight()
    {
        if (IsFocusPreview)
        {
            IsFocusPreview = false;
            IsLeftCollapsed = true;
            return;
        }
        IsRightCollapsed = !IsRightCollapsed;
    }

    /// <summary>Opens the library panel if it is tucked away (used when the user picks a rail tab).</summary>
    public void RevealLeft()
    {
        if (IsFocusPreview)
            IsFocusPreview = false;
        IsLeftCollapsed = false;
    }

    public void RevealRight()
    {
        if (IsFocusPreview)
            IsFocusPreview = false;
        IsRightCollapsed = false;
    }

    [RelayCommand]
    private void ToggleFocus() => IsFocusPreview = !IsFocusPreview;

    /// <summary>Brings back every panel at its default size.</summary>
    [RelayCommand]
    private void Reset()
    {
        _quiet = true;
        LeftFraction = DefaultLeftFraction;
        RightFraction = DefaultRightFraction;
        TimelineFraction = DefaultTimelineFraction;
        IsLeftCollapsed = false;
        IsRightCollapsed = false;
        IsFocusPreview = false;
        _quiet = false;
        Commit();
    }

    private bool _quiet;

    private void Commit()
    {
        if (_quiet)
            return;
        Changed?.Invoke();
        Save();
    }

    private sealed record Stored(double Left, double Right, double Timeline, bool LeftCollapsed, bool RightCollapsed);

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
            return;
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path));
            if (stored is null)
                return;
            LeftFraction = Math.Clamp(stored.Left, MinFraction, MaxSideFraction);
            RightFraction = Math.Clamp(stored.Right, MinFraction, MaxSideFraction);
            TimelineFraction = Math.Clamp(stored.Timeline, 0.2, 0.75);
            IsLeftCollapsed = stored.LeftCollapsed;
            IsRightCollapsed = stored.RightCollapsed;
        }
        catch
        {
            // a damaged layout file just means defaults
        }
    }

    private void Save()
    {
        if (_path is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new Stored(
                LeftFraction, RightFraction, TimelineFraction, IsLeftCollapsed, IsRightCollapsed)));
        }
        catch
        {
            // not being able to remember the layout must never break editing
        }
    }
}
