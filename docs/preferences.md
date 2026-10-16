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

* A preference is **declared once** (`PreferenceDef`: key, section, label, description, kind, default, range or choices, scope, when it applies). The screen, the file, validation and the reset arrows are all made from the declaration, the same way an effect's controls are made from its `[EffectParam]`s. The existing `ParamDef` and param editors are reused for the rows.
* Code reads settings through an injected `IPreferences`, never a static, so tests can pass their own. `PreferenceService` implements it; `PreferenceLoader` only reads and writes the file; `PreferenceCatalog` is the list of `PreferenceDef`s. All three live in `Fig.App/Services/Config` and use no Avalonia types, so they can move to `Fig.Core` unchanged.
* **Two scopes.** *User* preferences live in `~/.fig/config/preferences.json`. A few (export defaults) can be overridden *per project* and are then stored in the project file. Resolution order: default, user file, project, environment variable.
* **Remembered state is not a preference.** Panel sizes, the open library tab, zoom and similar stay automatic and out of the screen.
* **The file is safe to lose or break.** Saves are atomic (temp file, then rename). A damaged file is kept as `preferences.json.bad` and defaults are used with a notice. Unknown keys are kept, so an older build does not erase a newer one's settings. The file carries a schema version.
* **Every preference says when it applies**: `Live`, `NextProject` or `Restart`, and the row says so.

## The screen

An in-window page (the window has its own chrome, so no separate OS window), opened from File, Preferences or Ctrl+Comma. Categories on the left as a soft rail, rows on the right: the label and its one-line description on the left, the control on the right. A search box filters every row across every category. A modified row shows a dot and a reset arrow; each category and the page have "Reset to defaults". Rows that need a restart say so.

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
