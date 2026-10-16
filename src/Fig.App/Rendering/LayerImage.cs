using System;
using System.IO;
using SkiaSharp;

namespace Fig.App.Rendering;

/// <summary>
/// The picture behind an image clip: one frame for a still, every frame for an animated GIF or WebP, each with
/// its transparency, already composed onto the frames it builds on. An animation loops for as long as the clip lasts.
/// </summary>
internal sealed class LayerImage
{
    /// <summary>
    /// Frames shown for less than this (GIFs often say 0 ms) last <see cref="QuickFrameMs"/> instead, as in browsers;
    /// otherwise such a GIF would flash through every frame at once.
    /// </summary>
    private const int MinFrameMs = 20;
    private const int QuickFrameMs = 100;

    private readonly SKImage[] _frames;
    // when each frame ends, in milliseconds from the start of a loop
    private readonly double[] _ends;

    private LayerImage(SKImage[] frames, double[] ends)
    {
        _frames = frames;
        _ends = ends;
    }

    public int Width => _frames[0].Width;
    public int Height => _frames[0].Height;
    public int FrameCount => _frames.Length;

    public SKImage this[int index] => _frames[index];

    /// <summary>The frame showing <paramref name="localT"/> seconds into the clip (looping), as an index.</summary>
    public int FrameAt(double localT)
    {
        if (_frames.Length == 1)
            return 0;
        var loop = _ends[^1];
        var ms = (Math.Max(0, localT) * 1000) % loop;
        var index = Array.BinarySearch(_ends, ms);
        index = index >= 0 ? index + 1 : ~index;     // a frame ending exactly now has given way to the next
        return Math.Min(index, _frames.Length - 1);
    }

    /// <summary>Decodes a picture file, or null when it can't be read.</summary>
    public static LayerImage? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var codec = SKCodec.Create(path);
            if (codec is null)
                return null;
            return codec.FrameCount > 1 ? Animated(codec) : Still(codec);
        }
        catch (Exception)
        {
            return null;    // an unreadable file just draws nothing
        }
    }

    private static LayerImage? Still(SKCodec codec)
    {
        using var bitmap = SKBitmap.Decode(codec);
        return bitmap is null ? null : new LayerImage([SKImage.FromBitmap(bitmap)], [QuickFrameMs]);
    }

    /// <summary>
    /// Every frame, in order. A frame may only paint what changed: the codec says which earlier frame it is drawn
    /// over, so that frame's finished picture goes in the buffer first.
    /// </summary>
    private static LayerImage Animated(SKCodec codec)
    {
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var frameInfo = codec.FrameInfo;
        var frames = new SKImage[frameInfo.Length];
        var ends = new double[frameInfo.Length];
        using var buffer = new SKBitmap(info);
        var elapsed = 0.0;
        for (var i = 0; i < frameInfo.Length; i++)
        {
            var under = frameInfo[i].RequiredFrame;
            if (under >= 0 && under < i)
                frames[under].ReadPixels(info, buffer.GetPixels(), info.RowBytes, 0, 0);
            else
                buffer.Erase(SKColors.Transparent);
            codec.GetPixels(info, buffer.GetPixels(), new SKCodecOptions(i, under < i ? under : -1));
            frames[i] = SKImage.FromPixelCopy(info, buffer.GetPixels(), info.RowBytes);

            var duration = frameInfo[i].Duration;
            elapsed += duration < MinFrameMs ? QuickFrameMs : duration;
            ends[i] = elapsed;
        }
        return new LayerImage(frames, ends);
    }
}
