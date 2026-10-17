using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Where the camera looks (0..1 from the top-left), how close it is, and its turn.</summary>
    public readonly record struct TransformView(double CenterX, double CenterY, double Zoom, double RotationDegrees)
    {
        public bool IsIdentity =>
            Zoom <= 1.0001 && Math.Abs(RotationDegrees) < 0.01
            && Math.Abs(CenterX - 0.5) < 1e-4 && Math.Abs(CenterY - 0.5) < 1e-4;
    }

    /// <summary>The Transform effect's camera maths, shared by rendering and the on-preview controls.</summary>
    public static class TransformMath
    {
        /// <summary>The camera described by a set of resolved parameters, kept inside the picture.</summary>
        public static TransformView ViewFrom(IReadOnlyDictionary<string, ParamValue> values)
            => ViewFrom(new ParamSet(ParamSchema.Of<EffectParamAttribute>(typeof(TransformEffect)), values));

        internal static TransformView ViewFrom(ParamSet p)
        {
            var zoom = p.Number("zoom");
            var half = 0.5 / zoom;
            return new TransformView(
                Math.Clamp(p.Number("centerX"), half, 1 - half),
                Math.Clamp(p.Number("centerY"), half, 1 - half),
                zoom,
                p.Number("rotation"));
        }

        /// <summary>The camera of a Transform effect at a clip-relative time, keyframes included.</summary>
        public static TransformView ViewAt(EffectInstance effect, double localT)
            => ViewFrom(EffectPipeline.ResolveParams(effect, localT));
    }
}
