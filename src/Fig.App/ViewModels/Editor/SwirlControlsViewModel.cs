using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// The selected Swirl transition's center, radius and rotation, shared by the inspector and the
/// handles drawn on the preview (dragging either updates the same transition parameters).
/// </summary>
public sealed partial class SwirlControlsViewModel : ViewModelBase, Fig.App.Controls.IPreviewOverlayModel
{
    private readonly PropertiesViewModel _host;
    private readonly EditorViewModel _editor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOverlay))]
    private bool _isActive;

    [ObservableProperty] private double _centerX = 0.5;
    [ObservableProperty] private double _centerY = 0.5;
    [ObservableProperty] private double _radius = 0.42;
    [ObservableProperty] private double _rotationDegrees = 90;
    [ObservableProperty] private bool _clockwise = true;
    [ObservableProperty] private double _frameAspect = 16.0 / 9.0;

    public bool ShowOverlay => IsActive && _host.ShowPreviewControls;

    public SwirlControlsViewModel(PropertiesViewModel host, EditorViewModel editor)
    {
        _host = host;
        _editor = editor;
    }

    public void NotifyControlsVisibilityChanged() => OnPropertyChanged(nameof(ShowOverlay));

    public void Refresh()
    {
        var transition = _host.CurrentTransition();
        var active = transition is { TypeId: TransitionCatalog.Swirl };
        IsActive = active;
        if (transition is null || !active)
            return;

        double Num(string key, double fallback)
            => ReadParam(transition, key) is { } v ? v.AsNumber : fallback;
        CenterX = Num("centerX", 0.5);
        CenterY = Num("centerY", 0.5);
        Radius = Num("radius", 0.42);
        RotationDegrees = Num("rotation", 90);
        Clockwise = !(ReadParam(transition, "direction") is { } d && d.AsChoice == 1);
        FrameAspect = _editor.Preview.CanvasAspect;
    }

    private static ParamValue? ReadParam(CutTransition transition, string key)
    {
        if (transition.Left.TransitionOut?.Params.TryGetValue(key, out var a) == true)
            return a;
        if (transition.Right.TransitionIn?.Params.TryGetValue(key, out var b) == true)
            return b;
        return null;
    }

    /// <summary>Writes any of the controls (null = leave alone), clamped to sensible limits.</summary>
    public void Set(double? centerX = null, double? centerY = null, double? radius = null, double? rotation = null)
    {
        if (centerX is { } cx) _host.WriteTransitionParam("centerX", ParamValue.OfDouble(Math.Clamp(cx, 0, 1)));
        if (centerY is { } cy) _host.WriteTransitionParam("centerY", ParamValue.OfDouble(Math.Clamp(cy, 0, 1)));
        if (radius is { } r) _host.WriteTransitionParam("radius", ParamValue.OfDouble(Math.Clamp(r, 0.05, 1)));
        if (rotation is { } rot) _host.WriteTransitionParam("rotation", ParamValue.OfDouble(Math.Clamp(rot, 0, 720)));
    }

    [RelayCommand]
    private void SetClockwise() => _host.WriteTransitionParam("direction", ParamValue.OfChoice(0));

    [RelayCommand]
    private void SetCounterclockwise() => _host.WriteTransitionParam("direction", ParamValue.OfChoice(1));
}
