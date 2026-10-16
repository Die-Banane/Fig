using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// One effect of a stack with everything resolved for a single frame: keyframes sampled, and the time the
    /// effect sees (clip-relative for clip effects, timeline time for track effects). The CPU and GPU
    /// renderers both draw from these, so they cannot disagree about what the effect should do.
    /// </summary>
    public sealed record EffectCall(string TypeId, IReadOnlyDictionary<string, ParamValue> Params, double LocalT = 0);

    /// <summary>Crop insets from each edge as fractions of the frame (0..1).</summary>
    public readonly record struct SceneCrop(double Left, double Top, double Right, double Bottom)
    {
        public static readonly SceneCrop None = new(0, 0, 0, 0);

        public bool IsNone => Left <= 1e-6 && Top <= 1e-6 && Right <= 1e-6 && Bottom <= 1e-6;
    }

    /// <summary>One decoded picture, canvas-sized, with the effects and crop that apply to it.</summary>
    public sealed class RenderSource
    {
        public required DecodedFrame Frame { get; init; }
        public IReadOnlyList<EffectCall> Effects { get; init; } = Array.Empty<EffectCall>();
        public SceneCrop Crop { get; init; } = SceneCrop.None;
    }

    /// <summary>One layer: a single source, or a transition between two. Opacity already includes fades.</summary>
    public sealed class RenderLayer
    {
        public required RenderSource Source { get; init; }
        public RenderSource? Incoming { get; init; }
        public string? TransitionTypeId { get; init; }
        public double Progress { get; init; }
        public IReadOnlyDictionary<string, ParamValue>? TransitionParams { get; init; }

        /// <summary>Track effects, applied after the crop and any transition.</summary>
        public IReadOnlyList<EffectCall> Effects { get; init; } = Array.Empty<EffectCall>();

        public double Opacity { get; init; } = 1.0;
    }

    /// <summary>Everything needed to draw one finished frame: the layers, bottom of the stack first.</summary>
    public sealed class RenderScene
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required IReadOnlyList<RenderLayer> Layers { get; init; }
    }

    /// <summary>Draws a <see cref="RenderScene"/> into a BGRA canvas. Implemented on the CPU here and on the GPU in the app.</summary>
    public interface ISceneRenderer
    {
        /// <summary>How many frames the exporter may have in flight at once (1 for a synchronous renderer).</summary>
        int MaxInFlight { get; }

        /// <summary>
        /// Starts drawing <paramref name="scene"/> into <paramref name="canvas"/> (width*height*4 BGRA bytes).
        /// The renderer has finished reading the scene's frames when this method returns, so the caller may
        /// reuse their buffers at once; the returned task completes when <paramref name="canvas"/> is filled.
        /// </summary>
        Task RenderAsync(RenderScene scene, byte[] canvas);
    }
}
