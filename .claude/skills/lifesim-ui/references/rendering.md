# Custom Simulation Rendering

Drawing the world on a canvas — the highest-risk UI area. See [`lifesim.md`](../../../../lifesim.md) §18, §2.

> **This is the Fluent exception.** The map viewport is full-bleed custom rendering and does **not** follow the card/surface/spacing rules of the general design system (`avalonia-ui` skill). The chrome *around* it (nav, inspector, control deck, legend, dialogs) does. Accessibility still applies: the map must not convey state by colour alone.

## Simulation palette (project tokens)
Separate from the app's chrome tokens (the general `avalonia-ui` skill's `theming.md`); defined by lifesim.md §18. Keep as its own token set so it can't drift from the sim spec:
- Biome base colours (grassland, desert, swamp, ice sheet) + ground-energy brightness ramp.
- Action palette (move / graze / predation / reproduce / idle) — organism outlines + the Action colour mode.
- Colour-mode palettes: Energy, Diet tendency, Stress fit, Lineage.
- Colour-vision-safe and never colour-alone (pair with shape/label); every colour has a legend entry.

## Rendering approach
<!-- TODO -->
- Custom-draw control / `DrawingContext` vs. composition; why.
- Camera: pan/zoom, viewport culling, coordinate mapping (world tiles ↔ screen).

## Biomes
<!-- TODO -->
- Reconstruct from world seed via the Core's Simplex (no separate implementation).
- Base colour + ground-energy brightness modulation + event tinting.

## Organisms
<!-- TODO -->
- Two channels: fill = active colour mode, outline/halo = last action.
- Marker radius ∝ Size; overlays for reproductive-ready / stress / predation flash.
- Selected-organism sensory footprint (`env_radius`/`org_radius`) highlight.

## Legend
<!-- TODO -->
- Always visible; updates with the active colour mode.

## NEAT brain graph
<!-- TODO -->
- Node/connection layout, live activation values, recurrent-vs-feedforward edge styling.

## Draw-loop discipline
<!-- TODO -->
- No allocations in the hot draw path; reuse pens/brushes/geometry.
- Batch by colour/mode; see performance.md for budgets.
