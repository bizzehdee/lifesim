# LifeSim App Architecture

How `LifeSim.App` is structured and how it targets desktop + browser. For general Avalonia project/styling and cross-platform (Windows + Linux) rules, see the user-level **`avalonia-ui`** skill (`practices.md`).

## Project & target setup
<!-- TODO -->
- Desktop target (Windows/Linux): <!-- head project, entry point, packaging -->
- Browser target (WASM): <!-- Avalonia.Browser host, bootstrap, COOP/COEP headers -->
- Shared assembly containing views/view-models used by both.

## Embedding the Core
<!-- TODO -->
- Desktop: embed `LifeSim.Core` in-process, run the engine on a background thread; UI reads live state.
- Browser: default to rendering streamed/loaded snapshots; optional small-world in-process mode via WASM.
- Rule: the Core is authoritative; the UI thread never blocks on ticks.

## App shell & navigation
<!-- TODO: window/page structure, control deck, panels (map, legend, inspector) -->
- Use the general design-system left-nav pattern (`avalonia-ui` skill) for the chrome.

## State sources & the snapshot boundary
<!-- TODO: how the UI consumes live state vs. deserialized snapshots identically -->
