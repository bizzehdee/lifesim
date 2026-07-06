---
name: lifesim-ui
description: Use when writing or reviewing UI code for LifeSim (the LifeSim.App Avalonia project) — the simulation canvas, inspector, control deck, the desktop/WASM targets, or embedding LifeSim.Core. Builds on the general, user-level `avalonia-ui` skill.
---

# LifeSim UI (Avalonia)

Project-specific UI guidance for `LifeSim.App`. **For all standard Avalonia/Fluent design, theming, components, accessibility, and styling practices, follow the general user-level `avalonia-ui` skill** — this skill only adds what is specific to LifeSim. See [`lifesim.md`](../../../lifesim.md) §1 (architecture), §18 (visualization), and [`tasks.md`](../../../tasks.md) Phases 13–15.

## Non-negotiables (LifeSim-specific)
- **One codebase, two targets** (desktop + browser/WASM); views render identically and are pure functions of state (lifesim.md §12, §18).
- **No engine logic in the UI.** `LifeSim.App` references `LifeSim.Core` and never reimplements ticking, PRNG, or noise; the Core stays trimming/AOT-friendly so it also runs under WASM (lifesim.md §1, §9).
- **Every colour has a legend entry** (lifesim.md §18).
- **The simulation map canvas is a deliberate exception** to the general card/Fluent surface rules — it is full-bleed custom rendering per lifesim.md §18. The *chrome* around it (nav, panels, control deck, legend, dialogs) follows the general house rules.
- **The browser (WASM) target is a constrained demo** — design against its scale / bundle / threading limits.

## What NOT to pull from the general skill here
Avatars, photographic imagery/illustrations, dashboard card-grids, and form-heavy / progressive-disclosure layouts are largely **N/A for a real-time simulation viewer**. Use them only if a genuine screen (e.g. a metrics panel) actually calls for it.

## Reference files (load as needed)
| File | Read when |
| :--- | :--- |
| `references/architecture.md` | project setup, the desktop/WASM targets, embedding the Core, app shell |
| `references/mvvm.md` | view-models, live-vs-snapshot data flow, the edit flow |
| `references/rendering.md` | the custom simulation canvas — biomes, organisms, colour modes, legend, NEAT graph, simulation palette |
| `references/performance.md` | frame budget and the WASM demo limits |
