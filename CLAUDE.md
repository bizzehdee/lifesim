# LifeSim

An emergent evolutionary ML life simulation. See [`lifesim.md`](./lifesim.md) for the architectural blueprint (source of truth) and [`tasks.md`](./tasks.md) for the phased build plan.

## Architecture at a glance
- **`LifeSim.Core`** — deterministic C# simulation engine (class library). Authoritative; no UI dependency; trimming/AOT-friendly so it also runs under WebAssembly.
- **`LifeSim.Console`** — headless CLI (`sim run` / `sim serve`) for batch evolution, calibration, and tests.
- **`LifeSim.App`** — a single Avalonia app shipped to two targets: native desktop (Windows/Linux) and the browser (WASM, a constrained demo).

## Guidelines
- UI work: general Avalonia/Fluent standards live in the user-level `avalonia-ui` skill; LifeSim-specific UI guidance is in the project `lifesim-ui` skill (`.claude/skills/lifesim-ui/`), which builds on it. Read both before a UI task.
- Determinism is a hard contract (`lifesim.md` §9): new randomness uses the correct named PRNG stream, simulation-sensitive iteration is sorted with explicit tie-breaking, and the two flagship determinism tests (`lifesim.md` §15) must stay green.
