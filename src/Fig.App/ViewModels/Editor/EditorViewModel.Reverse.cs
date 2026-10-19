using Avalonia.Threading;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// Reversing clips. Footage plays backwards from a reversed copy of its file, made once (in the project cache, with
/// progress in the status line) the first time anything from that file is reversed; reversing it again goes back
/// to the original. Text and stills have their animation turned round instead.
/// </summary>
public partial class EditorViewModel
{
    private bool _reversing;

    /// <summary>Reverses the clips and the clips linked to them, as one undo step.</summary>
    public async Task ReverseClipsAsync(IReadOnlyList<string> clipIds)
    {
        if (ProjectManager is null || _reversing)
            return;
        clipIds = clipIds.ToList();   // the selection may change while a copy is being made
        _reversing = true;
        try
        {
            var missing = Editor.ReverseTargets(clipIds)
                .Where(c => c is VideoClip or AudioClip && c.MediaId is not null)
                .Select(c => FindAssetById(c.MediaId!)).OfType<MediaAsset>()
                .Where(a => ReverseCounterpart(a) is null).DistinctBy(a => a.Id).ToList();
            foreach (var asset in missing)
                if (!await MakeReversedCopyAsync(asset))
                    return;

            if (!Editor.Reverse(clipIds, ReverseCounterpart))
                Notify?.Invoke("Couldn't reverse: its footage is missing");
            StatusHint = "Reversed";
        }
        finally
        {
            _reversing = false;
        }
    }

    /// <summary>The same footage the other way round: a copy's original, or an original's copy (null if not made yet).</summary>
    private MediaAsset? ReverseCounterpart(MediaAsset asset)
        => asset.ReverseOf is { } original
            ? FindAssetById(original)
            : Media.FirstOrDefault(m => m.ReverseOf == asset.Id && !m.Offline && File.Exists(m.Url));

    private async Task<bool> MakeReversedCopyAsync(MediaAsset asset)
    {
        var name = asset.FileName;
        var path = Path.Combine(ProjectManager!.CacheDirectory, "reversed", asset.Hash + (asset.Kind == MediaKind.Audio ? ".m4a" : ".mp4"));
        StatusHint = $"Reversing {name}…";
        try
        {
            if (!File.Exists(path))
                await Task.Run(() => _mediaService.GenerateReversed(asset.Url, path,
                    p => Dispatcher.UIThread.Post(() => StatusHint = $"Reversing {name}… {p:P0}")));
        }
        catch (Exception e)
        {
            StatusHint = "";
            Notify?.Invoke($"Couldn't reverse {name}: {e.Message}");
            return false;
        }

        var copy = await ImportFileAsync(path);
        if (copy is null)
        {
            Notify?.Invoke($"Couldn't reverse {name}");
            return false;
        }
        copy.ReverseOf = asset.Id;
        RefreshVisibleMedia();
        return true;
    }
}
