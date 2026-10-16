using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fig.Core.Media;
using Fig.Core.Timeline;
using SkiaSharp;

namespace Fig.App.Rendering;

/// <summary>
/// Draws text and still-image clips with Skia into transparent, frame-sized, premultiplied BGRA pictures.
/// Pictures are cached by what they show (text and style, or file, plus layout and size), so a layer that
/// isn't changing is drawn once, not every frame. Safe to call from the preview and export threads at once.
/// </summary>
public sealed class SkiaLayerRasterizer : ILayerRasterizer
{
    private const int MaxPictures = 24;
    private const int MaxImages = 8;

    private readonly Func<string, MediaAsset?> _findMedia;
    private readonly object _gate = new();
    private readonly Dictionary<object, DecodedFrame> _pictures = new();
    private readonly LinkedList<object> _pictureOrder = new();
    private readonly Dictionary<string, SKImage?> _images = new();
    private readonly LinkedList<string> _imageOrder = new();
    private readonly Dictionary<object, LayerSize?> _sizes = new();

    /// <param name="findMedia">Looks up a library file by id (an image clip's picture).</param>
    public SkiaLayerRasterizer(Func<string, MediaAsset?> findMedia) => _findMedia = findMedia;

    public DecodedFrame? Rasterize(Clip clip, int width, int height, double localT)
    {
        if (width <= 0 || height <= 0)
            return null;
        object? key = clip switch
        {
            TextClip text => (text.Text, text.Style, text.Layout, width, height),
            ImageClip image when _findMedia(image.SourceId) is { Url: { Length: > 0 } url } => (url, image.Layout, width, height),
            _ => null,
        };
        if (key is null)
            return null;

        lock (_gate)
        {
            if (_pictures.TryGetValue(key, out var cached))
                return cached;
        }

        var picture = clip switch
        {
            TextClip text => Draw(width, height, canvas => DrawText(canvas, text, width, height)),
            ImageClip image => LoadImage(_findMedia(image.SourceId)!.Url) is { } source
                ? Draw(width, height, canvas => DrawImage(canvas, source, image.Layout, width, height))
                : null,
            _ => null,
        };
        if (picture is null)
            return null;

        lock (_gate)
        {
            if (_pictures.TryAdd(key, picture))
            {
                _pictureOrder.AddLast(key);
                while (_pictureOrder.Count > MaxPictures)
                {
                    _pictures.Remove(_pictureOrder.First!.Value);
                    _pictureOrder.RemoveFirst();
                }
            }
            return _pictures[key];
        }
    }

    public LayerSize? Measure(Clip clip, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return null;
        object? key = clip switch
        {
            TextClip text => (text.Text, text.Style, width, height),
            ImageClip image when _findMedia(image.SourceId) is { Url: { Length: > 0 } url } => (url, width, height),
            _ => null,
        };
        if (key is null)
            return null;
        lock (_gate)
        {
            if (_sizes.TryGetValue(key, out var known))
                return known;
        }

        var (w, h) = clip switch
        {
            TextClip text => string.IsNullOrEmpty(text.Text) ? (0f, 0f) : TextBlock.Of(text.Style, text.Text, height).Bounds,
            ImageClip image => LoadImage(_findMedia(image.SourceId)!.Url) is { } source ? Fit(source, width, height) : (0f, 0f),
            _ => (0f, 0f),
        };
        LayerSize? size = w > 0 && h > 0 ? new LayerSize(w / (double)height, h / (double)height) : null;
        lock (_gate)
        {
            if (_sizes.Count > 256)
                _sizes.Clear();
            _sizes[key] = size;
        }
        return size;
    }

    /// <summary>A title laid out: its font, lines and block size (the box drawn behind it included).</summary>
    private sealed class TextBlock : IDisposable
    {
        public required SKTypeface Typeface { get; init; }
        public required SKFont Font { get; init; }
        public required SKFontMetrics Metrics { get; init; }
        public required string[] Lines { get; init; }
        public required float[] Widths { get; init; }
        public required float Size { get; init; }
        public float LineHeight => Metrics.Descent - Metrics.Ascent + Metrics.Leading;
        public float BlockWidth => Widths.Length == 0 ? 0 : Widths.Max();
        public float BlockHeight => LineHeight * Lines.Length;
        public float Pad => Size * 0.3f;
        public bool Background { get; init; }

        /// <summary>What the gizmo's box goes round: the text, or the box behind it when there is one.</summary>
        public (float Width, float Height) Bounds => Background
            ? (BlockWidth + Pad * 2, BlockHeight + Pad * 1.2f)
            : (BlockWidth, BlockHeight);

        public static TextBlock Of(TextStyle style, string text, int height)
        {
            var typeface = SKTypeface.FromFamilyName(style.FontFamily,
                               style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                               SKFontStyleWidth.Normal,
                               style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright)
                           ?? SKTypeface.Default;
            var size = (float)Math.Max(1, style.SizePercent / 100 * height);
            var font = new SKFont(typeface, size);
            font.GetFontMetrics(out var metrics);
            var lines = text.Replace("\r\n", "\n").Split('\n');
            return new TextBlock
            {
                Typeface = typeface,
                Font = font,
                Metrics = metrics,
                Lines = lines,
                Widths = Array.ConvertAll(lines, line => font.MeasureText(line)),
                Size = size,
                Background = style.Background,
            };
        }

        public void Dispose()
        {
            Font.Dispose();
            Typeface.Dispose();
        }
    }

    /// <summary>A picture's size fitted inside the frame (letterboxed).</summary>
    private static (float Width, float Height) Fit(SKImage image, int width, int height)
    {
        var fit = Math.Min(width / (double)image.Width, height / (double)image.Height);
        return ((float)(image.Width * fit), (float)(image.Height * fit));
    }

    private static DecodedFrame Draw(int width, int height, Action<SKCanvas> draw)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        draw(surface.Canvas);
        var pixels = new byte[width * height * 4];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { surface.ReadPixels(info, pin.AddrOfPinnedObject(), width * 4, 0, 0); }
        finally { pin.Free(); }
        return new DecodedFrame { Width = width, Height = height, Pixels = pixels };
    }

    /// <summary>Moves the canvas to the layer's centre, then rotates and scales around it.</summary>
    private static void Place(SKCanvas canvas, LayerTransform layout, int width, int height)
    {
        canvas.Translate((float)(layout.CenterX * width), (float)(layout.CenterY * height));
        canvas.RotateDegrees((float)layout.Rotation);
        canvas.Scale((float)Math.Max(0.01, layout.Scale));
    }

    private static void DrawText(SKCanvas canvas, TextClip clip, int width, int height)
    {
        if (string.IsNullOrEmpty(clip.Text))
            return;
        var style = clip.Style;
        using var block = TextBlock.Of(style, clip.Text, height);
        var (blockWidth, blockHeight, size) = (block.BlockWidth, block.BlockHeight, block.Size);

        canvas.Save();
        Place(canvas, clip.Layout, width, height);

        if (style.Background)
        {
            var pad = block.Pad;
            using var back = new SKPaint { IsAntialias = true, Color = new SKColor(style.BackgroundColor) };
            canvas.DrawRoundRect(SKRect.Create(-blockWidth / 2 - pad, -blockHeight / 2 - pad * 0.6f,
                blockWidth + pad * 2, blockHeight + pad * 1.2f), size * 0.2f, size * 0.2f, back);
        }

        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(style.Color) };
        if (style.Shadow)
            paint.ImageFilter = SKImageFilter.CreateDropShadow(0, size * 0.04f, size * 0.06f, size * 0.06f, new SKColor(0, 0, 0, 170));

        for (var i = 0; i < block.Lines.Length; i++)
        {
            var x = style.Align switch
            {
                TextAlign.Left => -blockWidth / 2,
                TextAlign.Right => blockWidth / 2 - block.Widths[i],
                _ => -block.Widths[i] / 2,
            };
            var y = -blockHeight / 2 + i * block.LineHeight - block.Metrics.Ascent;
            canvas.DrawText(block.Lines[i], x, y, SKTextAlign.Left, block.Font, paint);
        }
        canvas.Restore();
    }

    /// <summary>The picture fitted inside the frame (letterboxed), then placed by the layer's layout.</summary>
    private static void DrawImage(SKCanvas canvas, SKImage image, LayerTransform layout, int width, int height)
    {
        var (w, h) = Fit(image, width, height);
        canvas.Save();
        Place(canvas, layout, width, height);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, SKRect.Create(-w / 2, -h / 2, w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        canvas.Restore();
    }

    private SKImage? LoadImage(string path)
    {
        lock (_gate)
        {
            if (_images.TryGetValue(path, out var cached))
                return cached;
        }
        SKImage? image = null;
        try
        {
            if (File.Exists(path))
            {
                using var data = SKData.Create(path);
                image = data is null ? null : SKImage.FromEncodedData(data)?.ToRasterImage();
            }
        }
        catch (Exception)
        {
            image = null;   // an unreadable file just draws nothing
        }
        lock (_gate)
        {
            if (_images.TryAdd(path, image))
            {
                _imageOrder.AddLast(path);
                while (_imageOrder.Count > MaxImages)
                {
                    // not disposed: another thread may still be drawing it; the collector frees it
                    _images.Remove(_imageOrder.First!.Value);
                    _imageOrder.RemoveFirst();
                }
            }
            return _images[path];
        }
    }
}
