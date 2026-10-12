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


## Effects, keyframes and easing

An effect applies to the **whole clip**. Any numeric parameter can be animated with keyframes (`EffectInstance.Keyframes`, one track per parameter, times in seconds from the clip start). A parameter holds its first keyframe's value before it and its last keyframe's value after it, as in other editors, so zooming in and staying zoomed is just keyframes. Each `KeyframePoint` has an `Easing` (linear, ease in/out, hold) that shapes the movement to the next keyframe (`KeyframeEasingMath`).

Animating is an explicit switch (the stopwatch): `TimelineEditor.SetEffectAnimated` turns it on (one keyframe holding the current look) or off (keyframes removed, the look at the playhead kept). While animated, changes at the playhead become keyframes. Editing goes through `SetEffectKeyframe` / `AddEffectKeyframe` / `MoveEffectKeyframe` / `DuplicateEffectKeyframe` / `RemoveEffectKeyframe` / `SetEffectKeyframeEasing`, which key every parameter of a keyframe together and undo as one step.

`EffectInstance.StartSec` / `DurSec` are an **optional limit** to part of the clip (an effect zone). They are independent of keyframes and off by default.

## Transform and presets

Zoom, pan and rotate are one primitive, the **Transform** effect (`centerX`, `centerY`, `zoom`, `rotation`). Named behaviours ("Zoom in", "Pan", …) are **presets**: plain data (`EffectPreset`) that `EffectPresetInstantiator.Create` turns into a Transform with keyframes. Built-ins live in `BuiltInEffectPresets`; users save their own from a bar's right-click menu (JSON in `~/.fig/presets`, see `EffectPresetStore`). To add a built-in behaviour, add an entry to `BuiltInEffectPresets` — no new effect code is needed.

## The GPU preview canvas

The preview is drawn by the GPU. The decode worker no longer composites a picture: it decodes each layer's
frame (still on the CPU, at the preview resolution) and describes how to draw them as a `GpuScene`
(`src/Fig.App/Rendering`): the frames, each clip's resolved effect stack and crop, the active transition
and its progress, and layer opacity. `PreviewSurface` hands the scene to Skia's GPU canvas through a custom
draw operation, and every effect, crop, transition and layer blend runs in a fragment shader (SkSL).
Nothing is composited or copied on the CPU, so a transition no longer competes with the decoders for cores.

* **Shaders live in `GpuEffects.cs`** and are one-to-one ports of the CPU processors and blenders in
  `Fig.Core`, which export still uses. Preview and export must agree, so `GpuRenderingTests` renders every
  effect and transition both ways and fails when they drift. Match the CPU maths, including its rounding
  and integer pixel mapping, rather than "improving" one side.
* **Adding an effect needs no GPU work to be correct.** An effect without a shader is not skipped: the
  scene builder runs that clip's whole stack on the CPU before upload (`GpuEffects.Supports`). Add an SkSL
  port and list its id in `GpuEffectIds` to move it onto the GPU; the catalog-wide test checks either way.
* **Frames are top-down BGRA, canvas-sized, with alpha 0 in letterbox bars.** The CPU compositor ignores
  per-pixel alpha, so the shaders force sources opaque. Crop insets are top-down too (a top inset removes
  the top of the picture).
* **CPU fallback.** The surface reports whether Skia is really GPU-backed after the first frame. On a
  software canvas (and in headless tests) the view model switches to the SIMD CPU compositor, which is
  faster there. `PreviewViewModel.SetGpuCanvas` flips the mode; the status bar shows `GPU` in scene mode.
* Export is unchanged and still composites on the CPU (`TimelineRender`).

## Playback, scrubbing and proxies

**Scrubbing the picture** is two passes (`PreviewViewModel.DecodeWorkerAsync`). A fast pass shows the nearest keyframe at or before the playhead (`PreviewDecodeMode.Scrub`; frames from it report `LastFrameApproximate` and are never cached). When the playhead stops, an exact pass settles on the real frame, and gives way (`AbortRequested`) the moment the playhead moves again. Small steps forward just keep decoding.

**Proxies** are all-keyframe H.264 (`ProjectManager`, `_proxy_intra.mp4`), so any frame decodes alone and scrubbing costs a few ms whatever the source's GOP. Older long-GOP proxies are regenerated. Exports always read the original file.

**Scrub audio** (`PlaybackEngine.Scrub.cs`, `ScrubSynth`) plays the audio under the playhead at the playhead's speed and direction, like a jog wheel (resampled, so pitch follows speed; backwards works). A single step plays a short burst at normal speed. It runs on its own queue and never touches the 1x playback clock. Toggle with Shift+S (`EditorViewModel.AudioScrub`).

**Keyboard transport** (`EditorViewModel.Shuttle.cs`): Space plays at 1x; L / J shuttle forward / backward at 1x, 2x, 4x, 8x (press again to go faster); K stops; Left / Right step a frame (Shift: a second). Any knob or jog controller (TourBox, Stream Deck, ...) can be mapped to these keys, or to Left / Right for continuous jogging, and gets scrub audio for free.

**Ruler and grid**: `RulerCalculator.PickScale` chooses the labelled ticks and the small ticks between them. The small ticks are the snap grid, so what you read is what snaps. The grid only shows through the lanes while dragging.

**Transitions** (`Media/Transitions/Blenders`) are CPU pixel kernels, not GPU. Everything between decode and display (effects, transitions, compositing) runs in managed code with AVX2 where it pays; Avalonia/Skia only draws the finished bitmap. To keep a transition cheap: use `PixelOps.LerpBytes` (SIMD blend of a byte range), `PixelOps.ColumnBlend` (same weights on every row: wipe, curtains), and plain `Buffer.BlockCopy` for the parts of a row that are entirely one picture or the other. Only compute per pixel where the picture really changes (the soft band of a wipe, the inside of a swirl).

## UI smoke tests and screenshots

`tests/Fig.App.Tests/UiSmokeTests.cs` loads each view in a headless Avalonia window, so XAML and binding mistakes fail `dotnet test` instead of crashing the app. Set `FIG_SHOT_DIR` to also write a PNG of each screen, which is handy for comparing against the Penpot designs:

```bash
FIG_SHOT_DIR=/tmp/fig-shots dotnet test tests/Fig.App.Tests
```
