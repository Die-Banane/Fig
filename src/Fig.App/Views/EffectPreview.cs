using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Fig.Core.Media;

namespace Fig.App.Views;

/// <summary>A real processed frame, cached until the catalog card changes.</summary>
public sealed class EffectPreview : Control
{
    private const int W = 96, H = 64;
    public static readonly StyledProperty<string?> TypeIdProperty =
        AvaloniaProperty.Register<EffectPreview, string?>(nameof(TypeId));
    public string? TypeId { get => GetValue(TypeIdProperty); set => SetValue(TypeIdProperty, value); }
    private string? _renderedId;
    private WriteableBitmap? _bitmap;
    static EffectPreview() => AffectsRender<EffectPreview>(TypeIdProperty);
    protected override Size MeasureOverride(Size availableSize) => new(64, 44);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // _renderedId records the attempt, so an unknown or failing effect is not retried every render.
        if (_renderedId != TypeId)
            EnsureBitmap();
        if (_bitmap is not null) context.DrawImage(_bitmap, new Rect(Bounds.Size));
    }

    private void EnsureBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _renderedId = TypeId;
        var entry = TypeId is null ? null : EffectCatalog.Find(TypeId);
        var processor = TypeId is null ? null : EffectCatalog.Resolve(TypeId);
        if (entry is null || processor is null) return;
        var source = MakeFrame();
        DecodedFrame rendered;
        try { rendered = processor.Apply(source, entry.DefaultParams(), 0.5); }
        catch (Exception) { return; } // a broken processor leaves the card blank rather than failing the render pass
        try
        {
            _bitmap = new WriteableBitmap(new PixelSize(W, H), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using var buffer = _bitmap.Lock();
            Marshal.Copy(rendered.Pixels, 0, buffer.Address, W * H * 4);
        }
        finally
        {
            // Processors may return their input untouched; only pooled output goes back.
            if (!ReferenceEquals(rendered.Pixels, source.Pixels))
                FramePool.Return(rendered.Pixels);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private static DecodedFrame MakeFrame()
    {
        var pixels = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int i = (y * W + x) * 4;
            bool sun = (x - 70) * (x - 70) + (y - 16) * (y - 16) < 65;
            bool mountain = y > 33 + Math.Abs(x - 35) / 2;
            pixels[i] = (byte)(sun ? 190 : mountain ? 65 : 130 + y);
            pixels[i + 1] = (byte)(sun ? 225 : mountain ? 85 + x / 3 : 70 + y);
            pixels[i + 2] = (byte)(sun ? 255 : mountain ? 45 : 38 + x);
            pixels[i + 3] = 255;
        }
        return new DecodedFrame { Width = W, Height = H, Pixels = pixels };
    }
}
