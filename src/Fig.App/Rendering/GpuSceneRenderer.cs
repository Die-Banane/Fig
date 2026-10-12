using System;
using SkiaSharp;

namespace Fig.App.Rendering;

/// <summary>
/// Draws a <see cref="GpuScene"/> onto a Skia canvas. On a GPU-backed canvas every effect, crop,
/// transition and layer blend runs in the fragment shader; nothing is composited on the CPU.
/// The same code runs on a raster canvas, which is how the tests check it against the CPU kernels.
/// </summary>
public static class GpuSceneRenderer
{
    private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear);

    /// <summary>
    /// Draws the scene letterboxed into <paramref name="dest"/> (canvas units). The picture is cleared
    /// to black first, then layers are blended bottom-to-top by opacity.
    /// </summary>
    public static void Render(SKCanvas canvas, GpuScene scene, SKRect dest)
    {
        if (scene.Width <= 0 || scene.Height <= 0 || dest.Width <= 0 || dest.Height <= 0)
            return;

        using var scope = new ShaderScope();
        canvas.Save();
        canvas.ClipRect(dest);
        canvas.Translate(dest.Left, dest.Top);
        canvas.Scale(dest.Width / scene.Width, dest.Height / scene.Height);

        var full = new SKRect(0, 0, scene.Width, scene.Height);
        using (var black = new SKPaint { Color = SKColors.Black })
            canvas.DrawRect(full, black);

        using var paint = new SKPaint();
        foreach (var layer in scene.Layers)
        {
            if (layer.Opacity <= 0)
                continue;
            paint.Shader = BuildLayer(layer, scene.Width, scene.Height, scope);
            var alpha = (byte)Math.Clamp((int)Math.Round(layer.Opacity * 255), 0, 255);
            paint.Color = new SKColor(255, 255, 255, alpha);
            canvas.DrawRect(full, paint);
        }

        canvas.Restore();
    }

    /// <summary>Renders the scene into a new raster BGRA picture of the scene's own size (tests, thumbnails).</summary>
    public static byte[] RenderToBgra(GpuScene scene)
    {
        var info = new SKImageInfo(scene.Width, scene.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);
        Render(surface.Canvas, scene, new SKRect(0, 0, scene.Width, scene.Height));
        using var pixmap = surface.PeekPixels()
            ?? throw new InvalidOperationException("could not read the rendered scene back");
        return pixmap.GetPixelSpan().ToArray();
    }

    private static SKShader BuildLayer(GpuLayer layer, int width, int height, ShaderScope scope)
    {
        var a = BuildSource(layer.Source, width, height, scope);
        if (layer.Incoming is null || layer.TransitionTypeId is null)
            return a;
        var b = BuildSource(layer.Incoming, width, height, scope);
        return GpuEffects.Transition(a, b, layer.TransitionTypeId, layer.Progress,
            layer.TransitionParams ?? new System.Collections.Generic.Dictionary<string, Fig.Core.Timeline.ParamValue>(),
            width, height, scope);
    }

    private static SKShader BuildSource(GpuSource source, int width, int height, ShaderScope scope)
    {
        var shader = scope.Add(source.Image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, Linear));
        shader = GpuEffects.Opaque(shader, scope);
        // same order as the CPU path: the clip's effect stack first, then its crop
        foreach (var effect in source.Effects)
            shader = GpuEffects.Apply(shader, effect, width, height, scope);
        if (!source.Crop.IsNone)
            shader = GpuEffects.Crop(shader, source.Crop, width, height, scope);
        return shader;
    }
}
