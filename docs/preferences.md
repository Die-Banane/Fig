# Preferences (draft)

Status: **drafted, not built.** Design: `uml/persistence/preferences-schema.puml`, `uml/persistence/preferences-sequence.puml`. Requirements: FR-210 to FR-219 in `software_requirements.md`.

## What is configurable today

| What | Where it lives now | Problem |
| --- | --- | --- |
| Key bindings | `~/.fig/config/gestures.json`, `GestureRegistry` | file only; no screen, no conflict check |
| Panel sizes, collapsed panels | `~/.fig/config/workspace.json`, `WorkspaceLayout` | fine: remembered automatically, not a preference |
| Effect presets, templates, projects | `~/.fig/presets`, `templates`, `projects` | fixed location |
| Export size, fps, codec | `ExportSettings` in each project | per project only; no user default |
| FFmpeg location | `FFMPEG_ROOT` environment variable | invisible |
| Audio scrubbing | View menu switch | forgotten on restart |
| Snap, link | toolbar toggles | forgotten on restart |
| Preview quality, monitor volume | preview controls | forgotten on restart |
| Autosave delay (800 ms), undo depth (100), snap reach (9 px), proxy threshold (720 p) | constants in code | cannot be changed |
| GPU or CPU rendering | decided by whether the canvas is GPU-backed | no way to force either |

## Shape

This follows the approach in Avalonia's [How to: Save and Load Application Settings](https://docs.avaloniaui.net/docs/how-to/data-persistence-how-to): a typed settings class, one JSON file, one shared service.

* **`Preferences`** (`Fig.App/Services/Config/Preferences.cs`) is a plain class, grouped into sections (`General`, `Editing`, `Playback`, `Export`). A property's initial value is its default. Adding a setting is adding a property. `Normalize()` clamps values back into range after loading, so a hand-edited file can't break the app. `[JsonExtensionData] Unknown` keeps keys a newer build wrote, so an older build doesn't erase them.
* **`PreferenceLoader`** reads and writes `~/.fig/preferences.json` (`AppPaths.PreferencesPath`). A missing file gives defaults; a damaged one is kept as `preferences.json.bad` and defaults are used; a save goes to a temp file and is renamed over the old one, so a crash never leaves half a file.
* **`PreferenceService`** is created once in `App.axaml.cs` and passed to whatever needs it. Read `service.Current.Editing.SnapReachPx`; change a value, then call `Save()`, which normalizes, writes, and raises `Changed`.
* **Remembered state is not a preference.** Panel sizes, the open library tab, zoom and similar stay in `workspace.json` and out of the screen.
* **Keyboard shortcuts** stay in `GestureRegistry` and `gestures.json`; the Keyboard page edits that directly.
* **Not yet:** per-project overrides, environment-variable overrides, and debouncing saves while a slider is dragged. Add them when something needs them.

## The screen

An in-window page (the window has its own chrome, so no separate OS window), opened from File, Preferences or Ctrl+Comma. Categories on the left as a soft rail, one hand-written page per section on the right, bound to that section's properties through a view model; each change updates `Current` and calls `Save()`. Each page has "Reset to defaults" (a new instance of that section).

## First set of preferences

**General**
* Autosave delay (seconds, default 0.8)
* Ask before closing a project with unsaved changes (default on)
* Projects folder (default `~/.fig/projects`) *(restart)*
* Reopen the last project on start (default off)

**Editing**
* Snap on by default (on); snap reach in pixels (9)
* Link audio and video by default (on)
* Audio scrubbing (on)
* Undo steps kept (100)
* Default length of a still image (5 s) and of a text clip (3 s) *(for the text and image layers)*
* Default transition length (0.5 s)

**Playback and preview**
* Preview quality (default 360 p)
* Loop playback (off)
* Output audio device (system default) *(restart)*
* Generate proxies for sources taller than (720 p, or never)

**Rendering**
* Renderer: Automatic, GPU, or CPU *(applies to the preview and to export; Automatic uses the GPU when the canvas is GPU-backed)*
* Status line: "GPU-backed: yes" or "software"

**Export defaults** (a project may override)
* Resolution, frame rate, codec, quality (CRF), output folder, file name pattern

**Keyboard**
* The existing key bindings, as a list: search, record a new key, conflicts shown, reset one or all. Backed by `gestures.json`, so hand-edited files keep working.

**Appearance**
* Interface scale (100 %); reduce motion (off). Theme is dark only for now.

**Storage**
* Cache size with a Clear button; open the data, projects, presets and logs folders

**Advanced**
* FFmpeg folder (replaces `FFMPEG_ROOT` when set), hardware decode (off until it exists), log detail

## Out of scope

Accounts, sync between machines, per-track or per-clip defaults (those are the project), plugin settings, and a theme editor.
