Text size can use the same keyframe system as opacity. Opacity is already wired end to end, so you'd mirror it in five places:

1. Add a key
In src/Fig.Core/Timeline/Model/ClipAutomation.cs, add public const string TextSize = "textSize"; to AutomationKeys. The keyframes then live on the clip like opacity's, so these already work for free:
- saving,
- undo (SetClipKeyframeCommand),
- copying a clip,
- the diamonds on the timeline,
- cutting a clip in two (the split code I added divides every keyframe track).

2. Draw the text at the keyed size
In src/Fig.App/Rendering/SkiaLayerRasterizer.cs, Rasterize gets the clip's local time (localT) but draws text at a fixed Style.SizePercent. Work out the size for that moment and draw with it:
var size = ClipAutomation.Evaluate(text, AutomationKeys.TextSize, localT, text.Style.SizePercent);
var style = text.Style with { SizePercent = size };
Put size in the cache key as well. The key is currently (text.Text, text.Style, text.Layout, width, height), so every frame would hit the first cached picture and the text would never change size. Round it a little, e.g. Math.Round(size, 2), so the cache still helps.

3. Make the on-preview handles follow it
The move/scale handles and click-to-select measure the text box (Layers.Measure, used by LayerGizmoViewModel and TimelineEditor.LayerAt). Pass the playhead's local time into Measure and use the same Evaluate there. Otherwise the box keeps the base size while the text grows or shrinks.

4. Add the diamond button in the inspector
- View model: in PropertiesViewModel, copy what opacity does:
  - TextSizeAutomation = new AutomationButtonViewModel(this, AutomationKeys.TextSize, () => TextSizeValue);
  - call TextSizeAutomation.Refresh() where OpacityAutomation.Refresh() is called.
- Slider: in PropertiesViewModel.Layers.cs, OnTextSizeValueChanged, check how OnOpacityValueChanged handles a keyframed value. Once size has keyframes, moving the slider should set a keyframe at the playhead (SetClipKeyframe) instead of changing the style.
- Showing the value: have LoadLayer show the keyframed size at the playhead, not the base style value.
- XAML: in EditorView.axaml, add a kf-diamond ToggleButton next to the Size slider, bound to TextSizeAutomation, copied from the one after the Opacity slider.
