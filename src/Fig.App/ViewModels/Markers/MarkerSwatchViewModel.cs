using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Fig.App.ViewModels;

/// <summary>One color choice in the marker inspector.</summary>
public sealed partial class MarkerSwatchViewModel : ObservableObject
{
    public string Hex { get; }
    public IRelayCommand PickCommand { get; }

    [ObservableProperty]
    private bool _isSelected;

    public MarkerSwatchViewModel(string hex, Action<string?> pick)
    {
        Hex = hex;
        PickCommand = new RelayCommand(() => pick(hex));
    }
}
