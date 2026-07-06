# Spike: Avalonia WASM Render Performance (Decision Gate)

**Status: scaffolded, awaiting manual execution.** This spike is the go/no-go for using Avalonia-for-web as the browser demo surface (tasks.md Phase 0; lifesim.md §1). It cannot be validated in a headless CI/agent environment — it requires a human running a browser and observing framerate, bundle size, and cold start.

## Question
Can the Avalonia **browser (WASM)** target render the intended organism count as a *constrained demo* at an acceptable framerate and download size? If not, the fallback is a bespoke web renderer (the snapshot format stays the integration seam, so the fallback is localized).

## What to prototype
A throwaway scene (not production code) that draws N moving markers on a custom-drawn Avalonia control, mirroring the eventual organism map (lifesim.md §18):
- Custom `Control` with `DrawingContext` rendering; no per-frame allocations.
- Viewport with pan/zoom and culling.
- A slider/param for N (organism count) and a live FPS counter.

## Acceptance thresholds (fill in your real targets)
| Metric | Desktop target | Browser (WASM) demo target |
| :--- | :--- | :--- |
| Sustained framerate at demo N | <!-- e.g. 60 fps --> | <!-- e.g. ≥ 30 fps --> |
| Demo organism count N | <!-- e.g. 50k --> | <!-- e.g. 2–5k --> |
| WASM bundle (gzip/br) | n/a | <!-- e.g. ≤ ~15 MB --> |
| Cold start (cached / uncached) | n/a | <!-- e.g. ≤ 3s / ≤ 10s --> |

## How to run
- Desktop baseline: `dotnet run --project src/LifeSim.App.Desktop -c Release`
- Browser: `dotnet publish src/LifeSim.App.Browser -c Release -o artifacts/wasm`, then serve `artifacts/wasm/wwwroot` over HTTP (WASM threading needs COOP/COEP headers — see lifesim.md §1) and open in a browser.

## Result
- Date / hardware / browser: <!-- TODO -->
- Verdict (PASS = proceed with Avalonia-for-web / FAIL = plan bespoke web renderer): **PENDING**
- Notes: <!-- TODO -->
