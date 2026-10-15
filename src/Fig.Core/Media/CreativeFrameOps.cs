using Fig.Core.Timeline;

namespace Fig.Core.Media;

/// <summary>Resolution-independent, stateless sampling shared by creative effects and transitions.</summary>
internal static class CreativeFrameOps
{
    internal static double Number(IReadOnlyDictionary<string, ParamValue> p, string key, double fallback,
        double min = 0, double max = 1)
    {
        double value = p.TryGetValue(key, out var v) ? v.AsNumber : fallback;
        return double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    }

    internal static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    internal static DecodedFrame New(DecodedFrame source) => new()
    {
        Width = source.Width, Height = source.Height,
        Pixels = FramePool.Rent(source.Width * source.Height * 4)
    };

    internal static double Sample(DecodedFrame f, double x, double y, int channel)
    {
        x = Math.Clamp(x, 0, f.Width - 1);
        y = Math.Clamp(y, 0, f.Height - 1);
        int x0 = (int)x, y0 = (int)y;
        int x1 = Math.Min(x0 + 1, f.Width - 1), y1 = Math.Min(y0 + 1, f.Height - 1);
        double fx = x - x0, fy = y - y0;
        double top = f.Pixels[(y0 * f.Width + x0) * 4 + channel] * (1 - fx)
            + f.Pixels[(y0 * f.Width + x1) * 4 + channel] * fx;
        double bottom = f.Pixels[(y1 * f.Width + x0) * 4 + channel] * (1 - fx)
            + f.Pixels[(y1 * f.Width + x1) * 4 + channel] * fx;
        return top * (1 - fy) + bottom * fy;
    }

    /// <summary>Adds a bilinear sample of all three color channels, scaled by <paramref name="weight"/>, to <paramref name="acc"/>.</summary>
    internal static void SampleRgb(DecodedFrame f, double x, double y, double weight, Span<double> acc)
    {
        x = Math.Clamp(x, 0, f.Width - 1);
        y = Math.Clamp(y, 0, f.Height - 1);
        int x0 = (int)x, y0 = (int)y;
        int x1 = Math.Min(x0 + 1, f.Width - 1), y1 = Math.Min(y0 + 1, f.Height - 1);
        double fx = x - x0, fy = y - y0;
        int i00 = (y0 * f.Width + x0) * 4, i10 = (y0 * f.Width + x1) * 4;
        int i01 = (y1 * f.Width + x0) * 4, i11 = (y1 * f.Width + x1) * 4;
        double w00 = (1 - fx) * (1 - fy) * weight, w10 = fx * (1 - fy) * weight;
        double w01 = (1 - fx) * fy * weight, w11 = fx * fy * weight;
        for (int c = 0; c < 3; c++)
            acc[c] += f.Pixels[i00 + c] * w00 + f.Pixels[i10 + c] * w10
                + f.Pixels[i01 + c] * w01 + f.Pixels[i11 + c] * w11;
    }

    internal static DecodedFrame Warp(DecodedFrame source, Func<double, double, (double X, double Y)> map)
    {
        var output = New(source);
        PixelOps.Rows(source.Height, y =>
        {
            for (int x = 0; x < source.Width; x++)
            {
                var (u, v) = map((x + 0.5) / source.Width, (y + 0.5) / source.Height);
                int index = (y * source.Width + x) * 4;
                for (int c = 0; c < 4; c++)
                    output.Pixels[index + c] = Byte(Sample(source, u * source.Width - 0.5, v * source.Height - 0.5, c));
            }
        });
        return output;
    }

    internal static DecodedFrame Rgb(DecodedFrame source, Func<int, int, int, double> channel)
    {
        var output = New(source);
        PixelOps.Rows(source.Height, y =>
        {
            for (int x = 0; x < source.Width; x++)
            {
                int index = (y * source.Width + x) * 4;
                for (int c = 0; c < 3; c++)
                    output.Pixels[index + c] = Byte(channel(x, y, c));
                output.Pixels[index + 3] = source.Pixels[index + 3];
            }
        });
        return output;
    }

    internal delegate void RgbShader(int x, int y, Span<double> rgb);

    /// <summary>
    /// Like <see cref="Rgb"/> but evaluates the shader once per pixel and lets it fill all three
    /// channels, so work shared between channels is not repeated.
    /// </summary>
    internal static DecodedFrame Shade(DecodedFrame source, RgbShader shader)
    {
        var output = New(source);
        PixelOps.Rows(source.Height, y =>
        {
            Span<double> rgb = stackalloc double[3];
            for (int x = 0; x < source.Width; x++)
            {
                rgb.Clear();
                shader(x, y, rgb);
                int index = (y * source.Width + x) * 4;
                for (int c = 0; c < 3; c++)
                    output.Pixels[index + c] = Byte(rgb[c]);
                output.Pixels[index + 3] = source.Pixels[index + 3];
            }
        });
        return output;
    }

    internal static double Noise(int x, int y, int seed)
    {
        uint hash = unchecked((uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u);
        hash = (hash ^ (hash >> 13)) * 1274126177u;
        return (hash ^ (hash >> 16)) / (double)uint.MaxValue;
    }

    // Sliding-window separable box blur: work per pixel is independent of radius.
    internal static DecodedFrame Blur(DecodedFrame source, int radius)
    {
        if (radius <= 0)
            return source;
        radius = Math.Min(radius, 128);
        var horizontal = New(source);
        var output = New(source);
        int w = source.Width, h = source.Height, count = radius * 2 + 1;
        try
        {
            PixelOps.Rows(h, y =>
            {
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (int k = -radius; k <= radius; k++)
                        sum += source.Pixels[(y * w + Math.Clamp(k, 0, w - 1)) * 4 + c];
                    for (int x = 0; x < w; x++)
                    {
                        horizontal.Pixels[(y * w + x) * 4 + c] = (byte)(sum / count);
                        sum -= source.Pixels[(y * w + Math.Clamp(x - radius, 0, w - 1)) * 4 + c];
                        sum += source.Pixels[(y * w + Math.Clamp(x + radius + 1, 0, w - 1)) * 4 + c];
                    }
                }
            });
            PixelOps.Rows(w, x =>
            {
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (int k = -radius; k <= radius; k++)
                        sum += horizontal.Pixels[(Math.Clamp(k, 0, h - 1) * w + x) * 4 + c];
                    for (int y = 0; y < h; y++)
                    {
                        output.Pixels[(y * w + x) * 4 + c] = (byte)(sum / count);
                        sum -= horizontal.Pixels[(Math.Clamp(y - radius, 0, h - 1) * w + x) * 4 + c];
                        sum += horizontal.Pixels[(Math.Clamp(y + radius + 1, 0, h - 1) * w + x) * 4 + c];
                    }
                }
            });
            return output;
        }
        catch { FramePool.Return(output.Pixels); throw; }
        finally { FramePool.Return(horizontal.Pixels); }
    }

    internal static DecodedFrame Mix(DecodedFrame a, DecodedFrame b, Func<int, int, double> weight)
    {
        var output = New(a);
        PixelOps.Rows(a.Height, y =>
        {
            for (int x = 0; x < a.Width; x++)
            {
                double t = Math.Clamp(weight(x, y), 0, 1);
                int i = (y * a.Width + x) * 4;
                for (int c = 0; c < 4; c++)
                    output.Pixels[i + c] = Byte(a.Pixels[i + c] * (1 - t) + b.Pixels[i + c] * t);
            }
        });
        return output;
    }
}
