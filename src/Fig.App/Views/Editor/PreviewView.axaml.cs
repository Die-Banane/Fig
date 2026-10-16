using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Fig.App.ViewModels;

namespace Fig.App.Views;

public partial class PreviewView : UserControl
{
    private PreviewViewModel? _vm;

    public PreviewView()
    {
        InitializeComponent();
        Surface.GpuBackingDetected += OnGpuBackingDetected;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.FrameReady -= OnFrameReady;
            _vm.SceneReady -= OnSceneReady;
        }
        _vm = DataContext as PreviewViewModel;
        if (_vm is not null)
        {
            _vm.FrameReady += OnFrameReady;
            _vm.SceneReady += OnSceneReady;
        }
    }

    private void OnSceneReady(Fig.App.Rendering.GpuScene scene)
    {
        Surface.PresentScene(scene);
    }

    private void OnGpuBackingDetected(bool gpu) => _vm?.ReportCanvas(gpu);

    private void OnFrameReady(int width, int height, byte[] bgra)
    {
        Surface.Present(width, height, bgra);
    }
}
