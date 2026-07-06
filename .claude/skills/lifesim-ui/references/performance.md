# Performance & the WASM Demo Constraints

The browser target is a **constrained demo**, not a peer of desktop. Design against its limits from the start. See [`lifesim.md`](../../../../lifesim.md) §1, §9.

## Frame budget
<!-- TODO -->
- Target framerate and the per-frame draw budget.
- Viewport culling, level-of-detail at zoom-out, capping drawn organism count.

## Browser (WASM) limits — design against all three
<!-- TODO -->
- **Scale:** small worlds only in-browser; large worlds are *streamed*, never simulated in the browser. Define the demo's max organism/world size.
- **Bundle & startup:** multi-MB .NET+Avalonia WASM download — trimming/AOT, compression, caching strategy; acceptable cold-start budget.
- **Threading & memory:** limited WASM threading (SharedArrayBuffer + COOP/COEP headers) and bounded memory; how the live small-world mode degrades gracefully.

## Desktop
<!-- TODO -->
- Engine on a background thread; UI-thread marshalling cadence; avoid stalling on ticks.

## Verification
<!-- TODO -->
- The Phase 0 render-performance spike thresholds (tasks.md Phase 0) that gate Avalonia-for-web.
