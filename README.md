# The Fig Project

Fig is an open-source non-linear video editor built with C# and Avalonia. It is designed around a single idea: professional editing should not be locked behind paywalls or painfully slow exports.

![assets/preview.png](assets/preview2.png)


This is an early-stage project. The core engine (timeline, media pipeline, and playback) is being built first, with the interface growing around it.

## Status

Actively developed. Expect rough edges, incomplete features, and frequent changes.

## Features

- **Timeline editing**: drag, move, and resize clips; trim from either edge; split at the playhead; ripple delete with a fully undoable command history.
- **Linked audio**: dropping a video that contains audio automatically creates a linked audio clip on an audio track. Video and its audio move, trim, cut, and ripple together.
- **Media previews**: aspect-correct filmstrips tiled across video clips and sample-accurate waveforms rendered in real time from decoded audio peaks.
- **Synchronous playback**: audio is the master clock. A background producer decodes and mixes the audible clips into a ring buffer while the device drains it, so the timeline position and video preview follow the audio in real time.
- **Keyboard-first controls**: a data-driven gesture system that maps input combinations to timeline actions. Mappings are stored as editable configuration, not hardcoded.
- **Project validation**: opening a project checks every asset, detects offline sources, and regenerates stale or missing previews, with visible progress and a summary of what was repaired.
- **Resizable workspace**: media library, preview, inspector and timeline are separated by draggable splitters, scale with the window, collapse out of the way, and remember their sizes. Focus the preview with one click.
- **Markers**: bookmarks and range markers on the sequence, a Markers panel in the sidebar, snapping to markers, and "Export marked ranges" to save each range as its own video.
- **Effects and keyframes**: effects apply to the whole clip and can be animated with keyframes (a stopwatch switches animating on; values hold before the first and after the last keyframe). Each effect gets a row under its clip with its keyframes, and a keyframe ruler in the inspector; an effect can optionally be limited to part of the clip. Transform zooms, pans and rotates with keyframes (zoom in, hold and zoom back out are just keyframes), with an on-preview frame, easing, and reusable presets; the Swirl transition has draggable center, radius and rotation handles.

## Requirements

- .NET SDK 10.0 or later
- A desktop platform supported by Avalonia (Windows, macOS, Linux)
- An audio output device for playback (PulseAudio, PipeWire, or ALSA on Linux)

## Building and running

```bash
dotnet build
dotnet run --project src/Fig.App
```

## Tests

The test suite covers the timeline model, edit operations, gesture parsing, media probing and generation, audio mixing, project persistence, and validation.

```bash
dotnet test
```

## Project layout

```
src/
  Fig.Core/    Domain engine: timeline, media pipeline, audio mixing, gestures, project storage
  Fig.App/     Avalonia desktop application: views, view models, SVG icons, playback device
tests/
  Fig.Core.Tests/
```

## Architecture notes

- `Fig.Core` has no UI dependency. Everything about how the timeline behaves, how media is decoded, and how audio is mixed lives here and is covered by unit tests.
- The playback engine treats the audio device as the authoritative clock. The video preview and timeline playhead follow it, which is what keeps audio and picture in sync.
- Media artifacts (thumbnails, filmstrips, waveforms) are generated once, cached on disk per project, and repaired automatically when a project is opened.

## Goals

Tracked against the current build.

**Legend:** X Complete · O Partial / in progress · - Not started

| Status | Goal | Notes |
| :---: | :--- | :--- |
| X | Free video transitions | Cross-dissolve, wipe, slide, push, fade-to-black, iris, and curtain transitions ship with timeline apply/select/resize/remove controls and visual catalog previews. |
| X | Fast export | Rendering should take seconds to minutes for short projects. Full timeline → MP4 (H.264/AAC) export works; encoder threading, quality presets, and hardware encoding are still to come. |
| X | Clean, modern, intuitive interface | Keyboard-first Avalonia workspace that's easy to navigate. Functional, but panels and polish are still evolving. |
| - | Smart editing aids | Automatic cuts when voice isn't detected, and AI transcription. |
| O | Quick start for creators | Local slot-based templates can be browsed, applied to an empty timeline, and created from the current edit; guided slot replacement and portable sharing are next. |

## Development Philosophy

Extending Fig goes like this:
1. Create a UML spec (this may or may not be required for certain things). PlantUML is used here for convenience, and UML helps me understand the architecture of the project and where things stand. AI can be used for assistance here, and this is actually where it properly follows instructions.
2. Create the implementation code for the systems.
3. Tests first, UI last. After the backend/headless infrastructure works, the UI can easily be built on top of it.

This is the design philosophy I use for other projects, and it keeps things maintainable and **extensible**.

## Contributing

AI generated code is allowed as long as you mention:

1. What model was used
2. You can extensively describe what you added, how it works, and **why** you added it

Being able to understand what you added or built is of utmost importance. 

AI generated output will not be accepted if it's generated slop. I couldn't care less if you use AI as long as the code is good and you understand it, but I have a vision for this project, and feature bloat isn't one of them.

## License

Not yet licensed.
