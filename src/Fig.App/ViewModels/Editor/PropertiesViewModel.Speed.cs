using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// The Speed tab: the clip's speed and its speed curve.
/// </summary>
public partial class PropertiesViewModel
{
    [RelayCommand]
    private void ResetSpeed() => SpeedValue = 1;

    [RelayCommand]
    private void AddSpeedCurve()
    {
        if (_clipId is not null)
            _editor.Editor.AddSpeedCurve(_clipId);
    }

    [RelayCommand]
    private void RemoveSpeedCurve()
    {
        if (_clipId is not null)
            _editor.Editor.RemoveSpeedCurve(_clipId);
    }

    partial void OnSpeedValueChanged(double value)
    {
        if (_suppressApply || _clipId is null || !Has(Facet.Retime))
            return;
        _editor.Editor.SetSpeed(_clipId, value);
        _editor.Preview.RefreshFrame();
    }
}
