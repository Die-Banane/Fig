using System;
using System.Collections.Generic;
using System.Threading;
using Fig.Core.Media;
using Fig.Core.Timeline;
using SkiaSharp;

namespace Fig.App.Rendering;

/// <summary>Normalized crop insets from each edge (0..1).</summary>
public readonly record struct CropInsets(double Left, double Top, double Right, double Bottom)
{
    public static readonly CropInsets None = new(0, 0, 0, 0);

    public bool IsNone => Left <= 1e-6 && Top <= 1e-6 && Right <= 1e-6 && Bottom <= 1e-6;
}

/// <summary>One decoded picture, canvas-sized, with the effect stack and crop that apply to it.</summary>
public sealed class GpuSource
{
    public required SKImage Image { get; init; }
    public IReadOnlyList<EffectCall> Effects { get; init; } = Array.Empty<EffectCall>();
    public CropInsets Crop { get; init; } = CropInsets.None;
}

/// <summary>
/// One layer of the scene: a single source, or a transition between two sources. Opacity already
/// includes fades and the transition's cross-fade of the two clips' opacities.
/// </summary>
public sealed class GpuLayer
{
    public required GpuSource Source { get; init; }
    public GpuSource? Incoming { get; init; }
    /// <summary>Track effects applied after crop and any transition.</summary>
    public IReadOnlyList<EffectCall> Effects { get; init; } = Array.Empty<EffectCall>();
    public string? TransitionTypeId { get; init; }
    public double Progress { get; init; }
    public IReadOnlyDictionary<string, ParamValue>? TransitionParams { get; init; }
    public double Opacity { get; init; } = 1.0;
}

/// <summary>
/// Everything needed to draw one preview frame on the GPU: the layers bottom-to-top and their decoded
/// pictures. Reference counted so the UI thread and the render thread can each hold it safely; the
/// pictures are released when the last holder lets go.
/// </summary>
public sealed class GpuScene : IDisposable
{
    private int _refs = 1;

    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Layers in paint order: the first is the bottom of the stack.</summary>
    public required IReadOnlyList<GpuLayer> Layers { get; init; }

    /// <summary>Adds a holder. Each <see cref="Retain"/> must be balanced by one <see cref="Dispose"/>.</summary>
    public GpuScene Retain()
    {
        Interlocked.Increment(ref _refs);
        return this;
    }

    public void Dispose()
    {
        if (Interlocked.Decrement(ref _refs) != 0)
            return;
        foreach (var layer in Layers)
        {
            layer.Source.Image.Dispose();
            layer.Incoming?.Image.Dispose();
        }
    }
}
