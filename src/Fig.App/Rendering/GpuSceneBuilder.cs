using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;
using Fig.Core.Timeline;
using SkiaSharp;

namespace Fig.App.Rendering;

/// <summary>Turns decoded frames and clip state into the pieces of a <see cref="GpuScene"/>.</summary>
public static class GpuSceneBuilder
{
    /// <summary>Copies a decoded BGRA frame into an image the GPU can sample. The frame's buffer may be reused afterwards.</summary>
    public static SKImage ToImage(DecodedFrame frame)
        => SKImage.FromPixelCopy(
            new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque),
            frame.Pixels.AsSpan(0, frame.Width * frame.Height * 4));

    /// <summary>
    /// The clip's enabled effects in stack order that are in force at <paramref name="localT"/>, with keyframes
    /// sampled. Mirrors <see cref="EffectPipeline.ApplyStack(DecodedFrame, IReadOnlyList{EffectInstance}, double, List{byte[]}?)"/>.
    /// </summary>
    public static IReadOnlyList<EffectCall> EffectsAt(IReadOnlyList<EffectInstance> effects, double localT)
        => EffectPipeline.ResolveStack(effects, localT);

    /// <summary>Applies a track stack to its finished layer, including both sides of a transition.</summary>
    public static GpuLayer WithTrackEffects(GpuLayer layer, Track track, double timelineSec, int width, int height)
    {
        var calls = EffectsAt(track.Effects, timelineSec);
        if (calls.Count == 0) return layer;
        if (calls.All(call => GpuEffects.Supports(call.TypeId)))
            return new GpuLayer
            {
                Source = layer.Source, Incoming = layer.Incoming, TransitionTypeId = layer.TransitionTypeId,
                Progress = layer.Progress, TransitionParams = layer.TransitionParams, Opacity = layer.Opacity,
                Effects = calls,
            };

        // A CPU-only effect must see the completed track image, not its individual transition inputs.
        using var scene = new GpuScene
        {
            Width = width, Height = height,
            Layers = new[] { new GpuLayer
            {
                Source = layer.Source, Incoming = layer.Incoming, TransitionTypeId = layer.TransitionTypeId,
                Progress = layer.Progress, TransitionParams = layer.TransitionParams,
            } },
        };
        var frame = new DecodedFrame { Width = width, Height = height, Pixels = GpuSceneRenderer.RenderToBgra(scene) };
        var processed = EffectPipeline.ApplyStack(frame, track.Effects, timelineSec);
        return new GpuLayer { Source = new GpuSource { Image = ToImage(processed) }, Opacity = layer.Opacity };
    }

    /// <summary>The clip's crop at a clip-relative time (automation sampled), or none.</summary>
    public static CropInsets CropAt(VideoClip clip, double localT)
    {
        var (l, t, r, b) = ClipAutomation.SampleCrop(clip, Math.Clamp(localT, 0, Math.Max(0, clip.DurSec - 1e-4)));
        return new CropInsets(l, t, r, b);
    }

    /// <summary>
    /// A picture with the clip's effects and crop at <paramref name="localT"/>. Effects run as shaders;
    /// if the stack holds an effect that has no shader yet, the whole stack runs on the CPU first so the
    /// effect is never silently skipped (and order is preserved).
    /// </summary>
    public static GpuSource SourceFor(VideoClip clip, DecodedFrame frame, double localT)
    {
        var calls = EffectsAt(clip.Effects, localT);
        var crop = CropAt(clip, localT);
        if (calls.Any(c => !GpuEffects.Supports(c.TypeId)))
        {
            var rented = new List<byte[]>();
            try
            {
                var processed = EffectPipeline.ApplyStack(frame, clip.Effects, localT, rented);
                return new GpuSource { Image = ToImage(processed), Crop = crop };
            }
            finally
            {
                foreach (var buffer in rented)
                    FramePool.Return(buffer);
            }
        }

        return new GpuSource { Image = ToImage(frame), Effects = calls, Crop = crop };
    }

    /// <summary>
    /// Turns an export frame into a GPU scene: each decoded picture is copied into an image (so the decoders
    /// may move on) and the resolved effects stay as shader calls. A layer that holds anything without a
    /// shader (a future effect or transition) is drawn on the CPU first, so it is never dropped.
    /// </summary>
    public static GpuScene FromScene(RenderScene scene)
    {
        var layers = new List<GpuLayer>(scene.Layers.Count);
        try
        {
            foreach (var layer in scene.Layers)
                layers.Add(CanRunOnGpu(layer) ? ToGpuLayer(layer) : BakeOnCpu(layer, scene.Width, scene.Height));
        }
        catch
        {
            foreach (var built in layers)
            {
                built.Source.Image.Dispose();
                built.Incoming?.Image.Dispose();
            }
            throw;
        }
        return new GpuScene { Width = scene.Width, Height = scene.Height, Layers = layers };
    }

    private static bool CanRunOnGpu(RenderLayer layer)
        => layer.Source.Effects.All(c => GpuEffects.Supports(c.TypeId))
           && (layer.Incoming?.Effects.All(c => GpuEffects.Supports(c.TypeId)) ?? true)
           && layer.Effects.All(c => GpuEffects.Supports(c.TypeId))
           && (layer.TransitionTypeId is null || GpuEffects.SupportsTransition(layer.TransitionTypeId));

    private static GpuLayer ToGpuLayer(RenderLayer layer) => new()
    {
        Source = ToGpuSource(layer.Source),
        Incoming = layer.Incoming is null ? null : ToGpuSource(layer.Incoming),
        TransitionTypeId = layer.Incoming is null ? null : layer.TransitionTypeId,
        Progress = layer.Progress,
        TransitionParams = layer.TransitionParams,
        Effects = layer.Effects,
        Opacity = layer.Opacity,
    };

    private static GpuSource ToGpuSource(RenderSource source) => new()
    {
        Image = ToImage(source.Frame),
        Effects = source.Effects,
        Crop = new CropInsets(source.Crop.Left, source.Crop.Top, source.Crop.Right, source.Crop.Bottom),
    };

    /// <summary>Draws one layer with the CPU kernels and uploads the finished picture.</summary>
    private static GpuLayer BakeOnCpu(RenderLayer layer, int width, int height)
    {
        var single = new RenderScene
        {
            Width = width, Height = height,
            Layers = [new RenderLayer
            {
                Source = layer.Source, Incoming = layer.Incoming, TransitionTypeId = layer.TransitionTypeId,
                Progress = layer.Progress, TransitionParams = layer.TransitionParams, Effects = layer.Effects, Opacity = 1,
            }],
        };
        var pixels = new byte[width * height * 4];
        new CpuSceneRenderer().Render(single, pixels);
        return new GpuLayer
        {
            Source = new GpuSource { Image = ToImage(new DecodedFrame { Width = width, Height = height, Pixels = pixels }) },
            Opacity = layer.Opacity,
        };
    }
}
