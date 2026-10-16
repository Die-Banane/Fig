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

        using var scope = new ShaderScope(canvas.Context);
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

    /// <summary>
    /// Renders the scene into a BGRA picture of the scene's own size and reads it back. On a raster surface
    /// by default (tests, thumbnails); pass a GPU context to render on the GPU, as export does.
    /// </summary>
    public static byte[] RenderToBgra(GpuScene scene, GRContext? context = null, byte[]? into = null)
    {
        var info = new SKImageInfo(scene.Width, scene.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var surface = (context is not null ? SKSurface.Create(context, false, info) : null) ?? SKSurface.Create(info);
        Render(surface.Canvas, scene, new SKRect(0, 0, scene.Width, scene.Height));
        var size = scene.Width * scene.Height * 4;
        var bytes = into is { } buffer && buffer.Length >= size ? buffer : new byte[size];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            if (!surface.ReadPixels(info, pin.AddrOfPinnedObject(), scene.Width * 4, 0, 0))
                throw new InvalidOperationException("could not read the rendered scene back");
        }
        finally { pin.Free(); }
        return bytes;
    }

    private static SKShader BuildLayer(GpuLayer layer, int width, int height, ShaderScope scope)
    {
        var a = BuildSource(layer.Source, width, height, scope);
        if (layer.Incoming is not null && layer.TransitionTypeId is not null)
        {
            var b = BuildSource(layer.Incoming, width, height, scope);
            a = GpuEffects.Transition(a, b, layer.TransitionTypeId, layer.Progress,
                layer.TransitionParams ?? new System.Collections.Generic.Dictionary<string, Fig.Core.Timeline.ParamValue>(),
                width, height, scope);
        }
        foreach (var effect in layer.Effects)
            a = GpuEffects.Apply(a, effect, width, height, scope);
        return a;
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
