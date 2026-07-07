# LifeSim

An emergent evolutionary ML life simulation. See [`rules.md`](./rules.md) for a plain-language, high-level summary of the rules that govern the world.

## Architecture at a glance
- **`LifeSim.Core`** — deterministic C# simulation engine (class library). Authoritative; no UI dependency; trimming/AOT-friendly so it also runs under WebAssembly.
- **`LifeSim.Console`** — headless CLI (`sim run` / `sim serve`) for batch evolution, calibration, and tests.
- **`LifeSim.App`** — a single Avalonia app shipped to two targets: native desktop (Windows/Linux) and the browser (WASM, a constrained demo).

## Guidelines
- UI work: general Avalonia/Fluent standards live in the user-level `avalonia-ui` skill; LifeSim-specific UI guidance is in the project `lifesim-ui` skill (`.claude/skills/lifesim-ui/`), which builds on it. Read both before a UI task.
- Determinism is a hard contract: new randomness uses the correct named PRNG stream, simulation-sensitive iteration is sorted with explicit tie-breaking, and the two flagship determinism tests must stay green.
- Keep the startup **advanced config editor in sync with any new config**. It (`LifeSim.App/ViewModels/ConfigEditorViewModel.cs`) auto-generates fields from the config JSON tree, so a new numeric/boolean setting shows up for free — but when you add config: verify it appears and is sensibly grouped/labelled; if it's a new *headline toggle*, surface it as a dedicated setup checkbox and add its path to the editor's `Excluded` set; and make sure it round-trips through save/load of starting options.
