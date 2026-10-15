using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Fig.Core.Media;

namespace Fig.App.Services;

/// <summary>A fixed, recognisable test picture for the graph editor's preview, plus bitmap helpers.</summary>
public static class GraphPreviewSource
{
    /// <summary>Sky, a sun, hills and high-contrast bars, so blur, shifts and color changes all show.</summary>
    public static DecodedFrame Create(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var u = (double)x / width;
            var v = (double)y / height;
            double r, g, b;
            // sky gradient
            r = 60 + 70 * v; g = 110 + 70 * v; b = 170 + 40 * v;
            // hills
            var hill = 0.66 + 0.06 * Math.Sin(u * 9) + 0.04 * Math.Sin(u * 21 + 1);
            if (v > hill) { r = 30; g = 70 + 30 * (v - hill) * 6; b = 55; }
            // sun
            var dx = (u - 0.72) * width;
            var dy = (v - 0.28) * height;
            if (dx * dx + dy * dy < Math.Pow(height * 0.14, 2)) { r = 250; g = 226; b = 150; }
            // bars along the bottom edge
            if (v > 0.9) { var on = ((int)(u * 24)) % 2 == 0; r = g = b = on ? 235 : 25; }
            var i = (y * width + x) * 4;
            pixels[i] = (byte)Math.Clamp(b, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(g, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(r, 0, 255);
            pixels[i + 3] = 255;
        }
        return new DecodedFrame { Width = width, Height = height, Pixels = pixels };
    }

    public static WriteableBitmap ToBitmap(DecodedFrame frame)
    {
        var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);
        CopyInto(bitmap, frame);
        return bitmap;
    }

    public static void CopyInto(WriteableBitmap bitmap, DecodedFrame frame)
    {
        using var buffer = bitmap.Lock();
        var size = Math.Min(frame.Width * frame.Height * 4, frame.Pixels.Length);
        Marshal.Copy(frame.Pixels, 0, buffer.Address, size);
    }
}
