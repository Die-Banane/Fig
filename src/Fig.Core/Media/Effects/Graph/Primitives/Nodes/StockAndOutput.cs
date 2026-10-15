using System;
using System.Collections.Generic;
using Fig.Core.Timeline;
using static Fig.Core.Media.CreativeFrameOps;

namespace Fig.Core.Media
{
    [Primitive("effect", "Effect", Category = "Stock effects", Description = "Use any built-in effect as a node.")]
    [PrimitiveInput("image", "Image")]
    [PrimitiveOutput("image", "Image")]
    internal sealed class StockEffectPrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            var src = c.Input("image");
            var typeId = c.Node.EffectTypeId;
            if (typeId is null || EffectGraph.IsGraphTypeId(typeId))
                return src;
            var processor = EffectCatalog.Resolve(typeId);
            if (processor is null)
                return src;
            var parameters = new Dictionary<string, ParamValue>();
            if (EffectCatalog.Find(typeId) is { } entry)
                foreach (var (key, value) in entry.DefaultParams())
                    parameters[key] = value;
            foreach (var (key, value) in c.Params)
                parameters[key] = value;
            return processor.Apply(src, parameters, c.LocalT);
        }
    }

    [Primitive("output", "Output", Category = "Sources", Description = "The finished picture. Every effect has exactly one.")]
    [PrimitiveInput("image", "Image")]
    internal sealed class OutputPrimitive : IGraphPrimitive
    {
        // The executor returns this node's input directly; Evaluate exists only to satisfy the contract.
        public IReadOnlyDictionary<string, DecodedFrame> Evaluate(PrimitiveContext c)
            => new Dictionary<string, DecodedFrame>();
    }
}
