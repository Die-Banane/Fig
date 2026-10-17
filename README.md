<p align="center">
  <img src="assets/icon/fig.svg" width="128" height="128" alt="Fig icon">
</p>

# The Fig Project

Fig is an open-source non-linear video editor built with C# and Avalonia. It is designed around the single goal of simple basic video editing, much like capcut. Fig isn't trying to be Premiere or Davinci, but rather a simple tool for editing clips, be it long or short, and without hassle.

![assets/preview.png](assets/preview.png)

Fig is expected to become stable soon. Currently, there will be a feature freeze until current features and UI are polished enough.

## Status

Actively developed. Expect rough edges, incomplete features, and frequent changes.

## Features

| Feature | What you get |
| :--- | :--- |
| **Timeline editing** | Drag, move and resize clips, trim either edge, split at the playhead, ripple delete. Every edit can be undone. |
| **Video with its sound** | A video's audio is part of its clip, shown as a waveform strip under the picture. Picture and sound fade independently; right-click → *Detach audio* gives the sound its own clip. |
| **Media previews** | Filmstrips across video clips and waveforms drawn from the decoded audio. |
| **In-sync playback** | Audio drives the clock, so the playhead and preview stay in step with what you hear. |
| **Keyboard-first** | Shortcuts are editable configuration, not hard-coded. |
| **Project checks** | Opening a project finds missing files and rebuilds stale previews, with progress and a summary. |
| **Flexible workspace** | Library, preview, inspector and timeline resize, collapse, and remember their sizes. |
| **Markers** | Bookmarks and ranges on the sequence, a Markers panel, snapping, and *Export marked ranges* to save each range as its own video. |
| **Effects and keyframes** | Effects cover the whole clip or part of it, and any value can be keyframed with easing. Transform zooms, pans and rotates with an on-preview frame and reusable presets. |
| **Creative library** | 30 effects, 20 transitions and 13 presets (camera shake, glow, glitch, VHS, blur, zoom transitions, musical hits), with search and live thumbnails. See the [creative library guide](docs/creative-library.md). |

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
- UML is enforced so heavily because it's the only way to understand how the codebase works without having to scour through several thousand lines of code.

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
