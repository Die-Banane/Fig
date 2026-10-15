using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Fig.Core.Media;

namespace Fig.App.Views;

/// <summary>Cached catalog thumbnail rendered by the transition's real midpoint blender.</summary>
public sealed class TransitionPreview : Control
{
    private const int PreviewWidth = 112;
    private const int PreviewHeight = 56;

    public static readonly StyledProperty<string?> TypeIdProperty =
        AvaloniaProperty.Register<TransitionPreview, string?>(nameof(TypeId));

    private WriteableBitmap? _bitmap;
    private string? _renderedTypeId;

    static TransitionPreview()
    {
        AffectsRender<TransitionPreview>(TypeIdProperty);
    }

    public string? TypeId
    {
        get => GetValue(TypeIdProperty);
        set => SetValue(TypeIdProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(PreviewWidth, PreviewHeight);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureBitmap();
        if (_bitmap is not null)
            context.DrawImage(_bitmap, new Rect(Bounds.Size));
    }

    private void EnsureBitmap()
    {
        if (_bitmap is not null && _renderedTypeId == TypeId)
            return;
        _bitmap?.Dispose();
        _bitmap = null;
        _renderedTypeId = TypeId;

        var entry = TypeId is null ? null : TransitionCatalog.Find(TypeId);
        var blender = TypeId is null ? null : TransitionCatalog.Resolve(TypeId);
        if (entry is null || blender is null)
            return;

        var outgoing = MakeFrame(incoming: false);
        var incoming = MakeFrame(incoming: true);
        DecodedFrame blended;
        try { blended = blender.Blend(outgoing, incoming, entry.PreviewProgress, entry.DefaultParams()); }
        catch (Exception) { return; } // a broken blender leaves the card blank rather than failing the render pass
        try
        {
            _bitmap = new WriteableBitmap(
                new PixelSize(PreviewWidth, PreviewHeight),
                new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using var framebuffer = _bitmap.Lock();
            Marshal.Copy(blended.Pixels, 0, framebuffer.Address, PreviewWidth * PreviewHeight * 4);
        }
        finally
        {
            FramePool.Return(blended.Pixels);
        }
    }

    private static DecodedFrame MakeFrame(bool incoming)
    {
        var pixels = new byte[PreviewWidth * PreviewHeight * 4];
        for (var y = 0; y < PreviewHeight; y++)
        {
            for (var x = 0; x < PreviewWidth; x++)
            {
                var i = (y * PreviewWidth + x) * 4;
                var stripe = ((x / 14) + (y / 14)) % 2 == 0 ? 18 : 0;
                pixels[i] = (byte)(incoming ? 62 + stripe : 42 + stripe);
                pixels[i + 1] = (byte)(incoming ? 112 + stripe : 54 + stripe);
                pixels[i + 2] = (byte)(incoming ? 205 + stripe : 76 + stripe);
                pixels[i + 3] = 255;
            }
        }
        return new DecodedFrame { Width = PreviewWidth, Height = PreviewHeight, Pixels = pixels };
    }
}
