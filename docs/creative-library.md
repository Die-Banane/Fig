# Creative effects and transitions

The library contains 30 video effects, 20 transitions, and 13 built-in presets.
Use the Effects categories or the Transitions category selector to narrow the list;
search works together with the selected category. Effect thumbnails use the real
processor, and transition thumbnails use the real blender.

## New effects

- Motion: Camera Shake and Pulse Zoom.
- Blur: Soft Blur, Directional Blur, and Zoom Blur.
- Light: Glow, Light Leak, and Strobe Flash.
- Retro: Film Grain and VHS Tape.
- Glitch: Digital Glitch.
- Distort: Lens Distortion and Mirror.
- Color: Duotone.

The inspector exposes each effect's parameters, including intensity, speed,
frequency, colors, or direction where applicable. Numeric parameters support
the existing keyframe controls. Pulse Zoom uses its Tempo parameter; it does not
automatically follow beat markers.

## New transitions

- Camera: Zoom In, Zoom Out, Whip Pan, Spin, and Card Flip.
- Blur: Blur Dissolve.
- Light: White Flash and Luma Dissolve.
- Glitch: Glitch Cut and Pixel Dissolve.
- Wipe: Clock Wipe and Venetian Blinds.

Apply them to a cut, then adjust duration and parameters in the inspector.
Card Flip is a flat image turning edge-on, rather than a depth-estimated 3D scene.

## Short presets for musical hits

Punch Zoom, Tilt Bounce, Whip Left, Whip Right, White Flash Hit, Glow Hit,
Glitch Burst, and Shake Hit join the five existing motion presets. Select a video
clip, position the playhead, and add a preset. The animation starts there, or is
shifted earlier if necessary to fit before the clip ends. These use ordinary
editable keyframes; adding one does not automatically populate every beat marker.

## Rendering

New effects and transitions run on the CPU. The preview uploads their rendered
frames to the GPU, preserving their appearance and stack order; export uses the
same processors. Multiple blur/warp effects at high resolutions can reduce preview
frame rate. Grain and glitch patterns are deterministic for a given clip time, so
scrubbing and export reproduce the same pattern.

These are original implementations inspired by common editing styles. CapCut's
public resources describe [camera shake](https://www.capcut.com/explore/camera-shake-effect),
[VHS looks](https://www.capcut.com/resource/vhs-overlay-effects), and
[video transitions](https://www.capcut.com/tools/video-transition); their proprietary
assets and implementation are not part of Fig.
