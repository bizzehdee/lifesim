# LifeSim

An emergent evolutionary ML life simulation: organisms with NEAT brains evolve under environmental
pressure in a procedurally generated world. The engine is deterministic and headless; the UI visualizes,
inspects, and edits worlds.

- **Rules (high-level):** [`rules.md`](./rules.md)
- **UI guidelines:** the user-level `avalonia-ui` skill (general) + project `lifesim-ui` skill (`.claude/skills/lifesim-ui/`)

## Architecture

A single deterministic **C# Simulation Core** consumed by two surfaces:

| Project | Role |
| :--- | :--- |
| `src/LifeSim.Core` | Deterministic engine (class library). Authoritative; no UI deps; trim/AOT-friendly so it runs under WASM. |
| `src/LifeSim.Console` | Headless CLI (`sim`) — batch evolution, calibration, tests, snapshot I/O. |
| `src/LifeSim.App` | Shared Avalonia app (views/view-models). |
| `src/LifeSim.App.Desktop` | Desktop head (Windows/Linux) — embeds the Core, live. |
| `src/LifeSim.App.Browser` | Browser/WASM head — a constrained demo; renders snapshots/streams. |
| `tests/LifeSim.Core.Tests` | Engine unit tests. |
| `tests/LifeSim.Determinism.Tests` | Flagship determinism tests (Seed Replay, Save/Reload — arrive in Phase 4). |

The "single Avalonia app" is `LifeSim.App` plus the two thin platform heads (`.Desktop`, `.Browser`).

## Prerequisites
- .NET SDK **10.0.201+** (pinned in [`global.json`](./global.json)).
- The browser target builds with the SDK's bundled wasm capability. If a machine lacks it:
  `dotnet workload restore src/LifeSim.App.Browser`.

## Common commands
```bash
# Build / test everything
dotnet build LifeSim.slnx -c Release
dotnet test  LifeSim.slnx -c Release

# Run the desktop app
dotnet run --project src/LifeSim.App.Desktop

# Publish the browser (WASM) demo (serve artifacts/wasm/wwwroot over HTTP with COOP/COEP headers)
dotnet publish src/LifeSim.App.Browser -c Release -o artifacts/wasm

# Formatting (CI enforces this)
dotnet format LifeSim.slnx --verify-no-changes
```

## Conventions
- **Central package management:** versions live in [`Directory.Packages.props`](./Directory.Packages.props).
- **Solution-wide build settings & analyzers:** [`Directory.Build.props`](./Directory.Build.props); style in [`.editorconfig`](./.editorconfig).
- **Determinism is a hard contract:** named PRNG streams, sorted iteration with explicit
  tie-breaking, and the flagship determinism tests must stay green.

## Status
The render-performance spike ([`spikes/render-perf`](./spikes/render-perf/)) is a manual decision gate for
the browser demo that still needs to be run in a browser.
