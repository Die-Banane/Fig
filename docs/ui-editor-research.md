# Editor UI research and direction

## Research snapshot

Reviewed on 2026-08-16:

- CapCut's desktop editor groups creation tools in a stable library area and keeps the preview and timeline visible while users browse templates.
- Template browsing uses search, categories, thumbnail cards, duration, and slot counts. Applying a template leads into a replacement workflow instead of exposing the underlying timeline model first.
- CapCut's current help documentation says desktop template discovery and sharing are more limited than mobile. Fig can differentiate with first-class local template creation, inspectable template files, and desktop-native reuse.

Primary references:

- https://www.capcut.com/help/use-and-export-templates-in-capcut
- https://www.capcut.com/help/how-to-search-for-templates
- https://www.capcut.com/resource/capcut-template-videos

## Current Fig audit

The editor has the right three-part anatomy, but the behavior and visual hierarchy fight it:

1. The inspector column is removed whenever there is no selection. The canvas changes width as selection changes, which makes the workspace feel unstable.
2. The inspector title is presented as a floating card inside another panel. This adds elevation without adding hierarchy.
3. Six small tabs compete in one row. The selected state is a filled box, so the tab bar reads like a debug switcher.
4. Uppercase section labels, blue ticks, individual card borders, and persistent scrollbar chrome create visual noise.
5. Timing values are displayed as diagnostics rather than compact editable metadata.
6. The library is limited to media, transitions, and effects. There is no reusable starting-point workflow.

## UI direction

- Keep `Library | Canvas | Inspector` mounted at all times.
- Use one flat workspace surface with separators. Reserve cards for browseable assets such as media and templates.
- Give the inspector a quiet header, selection-aware content, and a designed empty state.
- Use an underline for inspector navigation and make horizontal overflow intentional.
- Group values into compact labeled rows. Keep sliders close to the value they control.
- Add Templates as a primary library destination beside Media, Transitions, and Effects.
- Separate Built-in and My templates through filters rather than a new window.
- Make template creation local-first: save the current timeline as a blueprint with typed replaceable media slots.

## Phased template scope

1. Foundation: template model, slot validation, JSON store, create-from-timeline, browser scaffold.
2. Replacement: explicit slot assignment UI, per-slot trim/fill behavior, safe apply into a new sequence.
3. Authoring: mark optional slots, expose text/color/font variables, choose a cover frame, preview before saving.
4. Distribution: portable template packages, dependency licensing metadata, import/export, optional community catalog.
