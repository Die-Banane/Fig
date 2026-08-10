## Creating a new Effect

Register the effect string in EffectCatalog.cs

Create the metadata and params 

for Example

```cs
[Effect(EffectCatalog.CropWithPadding, "Crop with Padding", Icon = "crop",
        Description = "Crop the edges and pad the freed space with a solid color.")]
    [EffectParam("left", "Crop left", Default = 0, Min = 0, Max = 0.5)]
    [EffectParam("right", "Crop right", Default = 0, Min = 0, Max = 0.5)]
    [EffectParam("top", "Crop top", Default = 0, Min = 0, Max = 0.5)]
    [EffectParam("bottom", "Crop bottom", Default = 0, Min = 0, Max = 0.5)]
    [EffectParam("color", "Padding color", Kind = ParamKind.Color, Default = 0xFF000000)]

    internal sealed class CropWithPadding : IEffectProcessor
    {
        ...
    }
```

Get the width and height of the current frame in the Apply() override

```cs
var w = frame.Width; // ...
var h = frame.Height; // ...
```

Then fetch your parameters with `parameters.TryGetValue("")`

obtain a frame using `FramePool.Rent(w*h*4)`

Then apply whatever pixelOps you want.


## Transitions

Creating transitions is a bit different, because the function signature differs, though the semantics are the same.

` public DecodedFrame Blend(DecodedFrame outgoing, DecodedFrame incoming, double t01, IReadOnlyDictionary<string, ParamValue> parameters)`

Here, you have the incoming decoded frame, and a param t01

The effect of the transition is applied depending on the value of t01. t01 changes depending on the progress/length of the transition from 0.0 to 1.0

This can be used for cool effects between frames.
