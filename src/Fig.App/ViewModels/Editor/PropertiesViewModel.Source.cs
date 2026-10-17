using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// The File tab: the source media's details and its proxy.
/// </summary>
public partial class PropertiesViewModel
{
    private void ApplyMediaFields(MediaAsset asset)
    {
        FileName = asset.FileName;
        KindLabel = asset.Kind.ToString();
        ResolutionLabel = asset.Width > 0 && asset.Height > 0
            ? $"{asset.Width}×{asset.Height}"
            : null;
        DurationLabel = FormatTime(asset.DurationSec);
        ProxyStatusLabel = asset.Kind == MediaKind.Video
            ? ProxyLabel(asset)
            : null;
        ProxyDetail = asset.Kind != MediaKind.Video
            ? null
            : asset.ProxyStatus switch
            {
                ProxyStatus.Ready => "Preview uses the proxy; export still uses the original.",
                ProxyStatus.Pending => "Encoding in background…",
                ProxyStatus.Failed => "Last attempt failed. You can retry.",
                ProxyStatus.None when MediaService.ShouldGenerateProxy(asset.Width, asset.Height)
                    => "No proxy yet. Generate one for smoother scrubbing.",
                _ => "Source is already small — proxy not needed.",
            };
    }

    private void UpdateProxyAction(MediaAsset asset)
    {
        if (asset.Kind != MediaKind.Video)
        {
            CanGenerateProxy = false;
            return;
        }

        var needs = MediaService.ShouldGenerateProxy(asset.Width, asset.Height);
        CanGenerateProxy = needs && !IsProxyBusy;
        ProxyActionLabel = asset.ProxyStatus switch
        {
            ProxyStatus.Ready => "Regenerate Proxy",
            ProxyStatus.Failed => "Retry Proxy",
            ProxyStatus.Pending => "Generating…",
            _ => "Generate Proxy",
        };
    }

    private static string ProxyLabel(MediaAsset asset) => asset.ProxyStatus switch
    {
        ProxyStatus.Ready => "Ready",
        ProxyStatus.Pending => "Pending",
        ProxyStatus.Failed => "Failed",
        _ when MediaService.ShouldGenerateProxy(asset.Width, asset.Height) => "Not generated",
        _ => "Not needed",
    };

    [RelayCommand]
    private async Task GenerateProxyAsync()
    {
        var asset = _proxyTarget;
        var manager = _editor.ProjectManager;
        if (asset is null || manager is null || IsProxyBusy)
            return;

        var force = asset.ProxyStatus is ProxyStatus.Ready or ProxyStatus.Failed;
        IsProxyBusy = true;
        CanGenerateProxy = false;
        ProxyActionLabel = "Generating…";
        ProxyStatusLabel = "Pending";
        ProxyDetail = "Encoding in background…";
        asset.ProxyStatus = ProxyStatus.Pending;

        try
        {
            await Task.Run(() => manager.RequestProxy(asset, force));
        }
        finally
        {
            IsProxyBusy = false;
            _editor.Preview.InvalidateSources();
            _editor.NotifyMediaArtifactsChanged();
            Refresh();
        }
    }
}
