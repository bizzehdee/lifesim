# LifeSim — Build Task Plan

This document turns the architectural blueprint in [`lifesim.md`](./lifesim.md) into an ordered, phased set of build tasks. Section references like **(Plan §4)** point at the corresponding section of `lifesim.md`, which remains the source of truth for *what* to build; this document describes *the order to build it in* and *how to know a piece is done*.

## How to use this document
- Phases are ordered by dependency. Later phases assume earlier ones are complete and green.
- `[ ]` items are individual tasks. Check them off as they land.
- Each phase lists its **Goal**, **Depends on**, tasks, and **Exit criteria** (the observable state that means the phase is done).
- **Guiding principle — determinism first:** the deterministic contract (Plan §9) is not a late feature. The PRNG, snapshot round-trip, and the two flagship regression tests (Plan §15) come online as early as possible and gate every subsequent phase.
- **Walking skeleton:** we deliberately stand up a minimal end-to-end headless run early (Phase 4) using a stubbed brain, so the tick loop and determinism tests exist before the complex subsystems (NEAT, events, UIs) are layered on.

---

## Phase 0 — Repository & Toolchain Scaffolding
**Goal:** A buildable multi-project solution with CI, matching the shared-core / multi-surface architecture (Plan §1).
**Depends on:** nothing.

- [x] Create the .NET solution with these projects: `LifeSim.Core` (class library — the engine), `LifeSim.Console` (headless CLI), `LifeSim.App` (a single Avalonia app configured for **two build targets: native desktop for Windows/Linux and browser/WebAssembly** — shared `LifeSim.App` + `.Desktop`/`.Browser` heads), and test projects `LifeSim.Core.Tests` / `LifeSim.Determinism.Tests`. (.NET 10, Avalonia 12, `.slnx`, central package management.)
- [x] `LifeSim.Core` must have **no UI dependency**, perform no I/O beyond snapshot serialization, and stay **trimming/AOT-friendly and free of desktop-only APIs** so it also runs under the Avalonia WASM target (Plan §1, §9). *(`IsAotCompatible=true` enables trim/AOT analyzers.)*
- [x] Add solution-wide formatting/analyzers; wire pre-commit or CI checks. *(`Directory.Build.props` analyzers + `.editorconfig`; CI runs `dotnet format --verify-no-changes`.)*
- [x] Set up CI that builds all projects, including **both** Avalonia targets (desktop and WASM), and runs the C# test suites. *(`.github/workflows/ci.yml`; publishes the WASM demo as an artifact.)*
- [~] **Render-performance spike (decision gate):** scaffolded at [`spikes/render-perf/`](./spikes/render-perf/) with acceptance thresholds and run instructions. **Still requires manual execution in a browser** — cannot be validated headlessly (Plan §1, §18).
- [x] Add `README` pointing at `lifesim.md` (spec) and this file (build plan).

**Exit criteria:** `dotnet build` succeeds for all projects ✅; the Avalonia app builds for both desktop and WASM targets ✅; test suites run green ✅ (2/2 passing). ⚠️ The render spike still needs a manual browser run to close the decision gate.

---

## Phase 1 — Deterministic Foundations (The Bridge)
**Goal:** Reproducible randomness, terrain noise, and a validated, versioned snapshot format — the pieces the whole determinism contract rests on (Plan §1, §9, §12).
**Depends on:** Phase 0.

- [x] Implement the custom deterministic PRNG with **separable named streams**: genesis, behavior (softmax + combat rolls), mutation, environmental events, sensory noise (Plan §9). *(`Determinism/Prng.cs` — xoshiro256**/SplitMix64; `Determinism/PrngStream.cs`, `PrngStreams.cs`.)*
- [x] Make full PRNG stream state serializable/deserializable (not just the seed) (Plan §9, §12). *(`PrngStreams.CaptureState`/`FromState`; round-trip covered by `FoundationDeterminismTests`.)*
- [x] Implement 2D Simplex noise in C# with a `noise_config` (frequency/octaves/thresholds); it runs on every surface including the Avalonia WASM target, so no second-language port or cross-language parity test is needed (Plan §1, §2, Appendix A). *(`World/SimplexNoise.cs`, `World/NoiseConfig.cs` — transcendental-free, bit-deterministic.)*
- [x] Define the snapshot schema objects and a JSON Schema file covering all required blocks: `schema_version`, `config_version`, `simulation_version`, `tick`, `world`, `configuration`, `prng_streams`, `evolution_bookkeeping`, `environment_modifiers`, `organisms`, `lineages`, `metrics`, `edit_log` (Plan §12). *(`Snapshot/Snapshot.cs`, `Snapshot/lifesim-snapshot.schema.json`; later-phase blocks carried as raw JSON until their types exist.)*
- [x] Implement snapshot serialize/deserialize with JSON Schema validation on import and **semver version gating** (hard-reject on major mismatch for both `schema_version` and `config_version`) (Plan §12). *(`Snapshot/SnapshotSerializer.cs`, `SnapshotSchema.cs`, `SnapshotVersion.cs`.)*
- [x] Implement the `configuration` block as a typed, versioned object seeded from the defaults in Plan Appendix A. *(`Configuration/SimulationConfig.cs` — metabolism, movement/combat, biomes, reproduction, mutation, trait bounds, events, naming.)*

**Exit criteria:** A snapshot can be written and re-read losslessly ✅; PRNG stream state round-trips exactly ✅; the Simplex implementation is transcendental-free and bit-deterministic for same input ✅ (`SimplexNoiseTests`, `FoundationDeterminismTests`) — ⚠️ actual desktop/WASM cross-build parity has not been manually re-verified since the crash. Schema validation rejects a malformed / wrong-major-version file ✅ (`SnapshotSerializerTests`). All 22 unit/determinism tests pass (`dotnet test`).

---

## Phase 2 — Environment & Biome Engine
**Goal:** An implicit, seed-reconstructed world of biomes and ground energy (Plan §2).
**Depends on:** Phase 1.

- [x] Implement the continuous 2D tile grid with implicit (non-serialized) terrain reconstructed from seed + `noise_config` (Plan §2, §12). *(`World/TerrainSampler.cs` — moisture/temperature noise layers derived from the world seed via a fixed, non-PRNG-consuming mix.)*
- [x] Derive biome per tile from Moisture × Temperature noise per the biome matrix: Grassland, Desert, Swamp, Ice Sheet (Plan §2). *(`World/BiomeClassifier.cs`, matrix-driven by `Configuration/BiomeThresholds`; `TerrainSampler.BiomeAt`.)*
- [x] Implement per-biome characteristics from config: temperature, movement friction, ambient regen rate, and energy cap (Plan §2, Appendix A). *(Already modelled in `Configuration/SimulationConfig.cs`'s `BiomesConfig`/`BiomeSettings` from Phase 1; wired to `TerrainSampler`/`GroundEnergyGrid` here.)*
- [x] Implement the ground energy pool per tile with per-tick regen toward the biome cap (Plan §2). *(`World/GroundEnergyGrid.cs` — sparse override map; untouched tiles are implicitly at cap. Serialized as `ground_energy` in the snapshot.)*
- [x] Add an optional debug snapshot mode that caches tile data for inspection (Plan §12). *(`TerrainSampler.CaptureDebugGrid`, `WorldSnapshot.DebugTerrain` — nullable, omitted from output unless explicitly populated.)*

**Exit criteria:** Given a seed, the Core reconstructs byte-identical biome maps ✅ (`FoundationDeterminismTests.BiomeMap_sameSeed_isByteIdentical`, `TerrainSamplerTests`) — ⚠️ desktop/WASM cross-build parity still not manually re-verified in a browser (same caveat as Phase 1's simplex noise). Ground energy regenerates to cap and no further ✅ (`GroundEnergyGridTests`, `FoundationDeterminismTests.GroundEnergy_regeneratesToCap_andNeverExceedsIt`). All 41 tests pass (`dotnet test`); `dotnet format --verify-no-changes` is clean.

---

## Phase 3 — Organism Model, Genome & Energy Economy
**Goal:** Organisms as genome-driven state machines with a working energy budget — but no brain yet (Plan §3, §11, §19).
**Depends on:** Phase 2.

- [x] Define the organism record and inheritable genome traits: Size, Speed Capacity, Thermal Envelope, `env_radius`, `org_radius`, Sensory Acuity — each with hard bounds from `trait_bounds` (Plan §3, §8). *(`Organisms/Genome.cs` — Thermal Envelope modelled as center ± half-width; `Genome.Clamped`/`MidRange` against `TraitBounds`.)*
- [x] Implement stable, never-reused `organism_id` allocation via `evolution_bookkeeping.next_organism_id` (Plan §9, §12). *(`Organisms/OrganismIdAllocator.cs`, seeded from the bookkeeping counter on resume.)*
- [x] Implement the energy budget (0.0–100.0, clamped ceiling) and the metabolism equation: base + thermal stress + sensory tax (Plan §3, §11). *(`Organisms/Organism.cs` — `EnergyCeiling`, `AddEnergy`/`SpendEnergy`; `Organisms/Metabolism.cs` — `BaseMetabolism`, `ThermalStress`, `Total`.)*
- [x] Implement the sensory tax (linear `env_radius`, quadratic `org_radius`, acuity term) and locomotion tax (distance × velocity² × friction) (Plan §3). *(`Metabolism.SensoryTax`, `Metabolism.LocomotionTax`.)*
- [x] Implement **organism naming**: deterministic `name = f(organism_id, wordlist_version)` (hash split into two adjective indices + one noun index); ship default word lists and store the resolved `name` on the record (Plan §19). *(`Naming/OrganismNamer.cs`, `Naming/WordList.cs`, `Organisms/OrganismFactory.cs`. Shipped lists are 389 adjectives × 411 nouns — smaller than the spec's suggested 1,000×1,000 but curated for quality; namespace ≈62M names, ample for any near-term run. Sized up later by extending `Naming/adjectives-1.txt`/`nouns-1.txt` or shipping a new versioned list.)*
- [x] Add unit tests for the naming namespace/collision expectations and for energy-transaction math. *(`WordListTests`, `OrganismNamerTests`, `GenomeTests`, `MetabolismTests`, `OrganismTests`, `OrganismIdAllocatorTests`.)*

**Exit criteria:** An organism can be constructed, named deterministically, accrue/pay energy, and be removed at zero energy ✅ (`OrganismTests`); names reproduce identically from id + word-list version and consume no PRNG draw ✅ (`OrganismNamerTests`, `FoundationDeterminismTests.OrganismNaming_isPureFunctionOfIdAndWordListVersion_consumesNoPrngDraw`). All 67 tests pass (`dotnet test`); `dotnet format --verify-no-changes` is clean.

---

## Phase 4 — Tick Loop & Walking Skeleton (Determinism Gate)
**Goal:** A complete phased tick loop running a headless world with a **stubbed random-action brain**, plus the two flagship determinism tests. This is the risk-reduction milestone.
**Depends on:** Phase 3.

- [ ] Implement the phased tick model in authoritative order: Environment → Sensing → Decision → Intent Resolution → Metabolism → Death & Transfer → Resource Regen → Mutation & Birth Commit → Metrics & Snapshot (Plan §7).
- [ ] Implement deterministic ordering (ascending `organism_id`) and explicit tie-breaking for all simulation-sensitive iteration (Plan §7, §9).
- [ ] Implement **fixed-order floating-point reductions** for cross-organism aggregations; restrict parallelism to per-organism-independent work only (Plan §9).
- [ ] Implement the one-organism-per-tile spatial model and movement resolution, including **multi-tile path-checking** that stops at the first blocked/occupied tile and pays only distance travelled (Plan §10, §17).
- [ ] Stub the decision phase with a behavior-PRNG random action pick (no NEAT yet) so the loop is exercisable end-to-end.
- [ ] Implement genesis/initialization: config-driven `initial_population` scattered in Grassland via the genesis PRNG stream (Plan §17).
- [ ] Implement full-extinction handling: halt, set `metrics.extinct = true`, write final snapshot, no auto-reseed (Plan §17).
- [ ] **Flagship test — Seed Replay:** same seed, N ticks, twice → byte-identical snapshots (Plan §15).
- [ ] **Flagship test — Save/Reload Equivalence:** 100 ticks straight == 50 + serialize + reload + 50 (Plan §15).

**Exit criteria:** A headless world runs for N ticks and both flagship determinism tests pass. These two tests now gate all later merges.

---

## Phase 5 — NEAT Brain (Recurrent)
**Goal:** Replace the stubbed brain with the evolvable recurrent NEAT network (Plan §4).
**Depends on:** Phase 4.

- [ ] Implement the NEAT genome: node genes (`id`, `type`, `activation` default `tanh`, `state`) and connection genes (`innovation_id`, `from`, `to`, `weight`, `enabled`) with `network_type: "recurrent"` (Plan §4, §12).
- [ ] Implement the global innovation counter (`next_innovation_id`), advanced only in the Birth Commit phase in sorted id order, serialized in snapshots (Plan §4, §9).
- [ ] Implement **recurrent evaluation via a single synchronous update per tick** (nodes read inputs' previous-tick values, commit together); persist per-node `state` across ticks and zero it at birth (Plan §4, §7).
- [ ] Implement the 11 action outputs → softmax → weighted PRNG selection: Move N/S/E/W, Harvest-Self, Harvest-N/S/E/W, Idle, Reproduce (Plan §4, §17).
- [ ] Verify node `state` round-trips through serialization (extend save/reload test to include a recurrent brain).

**Exit criteria:** Organisms act via their networks; determinism tests still pass with recurrent state included in the snapshot.

---

## Phase 6 — Sensing & Observation Inputs
**Goal:** The fixed-size, normalized sensory vector feeding the brain (Plan §13, §4).
**Depends on:** Phase 5.

- [ ] Implement the Sensing phase snapshot: inputs cached from start-of-tick world state, unaffected by same-tick decisions (Plan §7).
- [ ] Build the full authoritative fixed input vector from Plan §13 (energy, age, tile temp, biome/friction, richest-tile vector, closest-organism vector + size delta, smaller/larger neighbor counts, local density, last action result, reproductive readiness, global stress).
- [ ] Normalize all inputs into stable ranges before the network (Plan §13).
- [ ] Inject Gaussian noise scaled by `sensory_acuity` after normalization, using the sensory-noise PRNG stream (Plan §4, §13).

**Exit criteria:** Brains receive a fixed-width normalized vector; low-acuity organisms measurably receive noisier inputs; determinism holds.

---

## Phase 7 — Interactions: Harvest, Combat, Reproduction & Lineage
**Goal:** The universal Harvest action (grazing/predation), reproduction, and lineage tracking (Plan §5, §8, §10, §11).
**Depends on:** Phase 6.

- [ ] Implement directional Harvest targeting (`Harvest-Self` + `Harvest-N/S/E/W`); off-grid/empty targets resolve as a no-op paying base costs (Plan §10, §5).
- [ ] Implement ambient grazing: drain ground energy from the targeted tile into the organism, clamped to the energy ceiling (Plan §5, §11).
- [ ] Implement probabilistic predatory combat: `P(kill) = Size_a / (Size_a + Size_v)` rolled against the behavior/combat stream; transfer on success, retaliation penalty on failure (Plan §5, §11).
- [ ] Implement asexual reproduction gating: `Reproduce` selected, energy ≥ `reproduction_base_cost × size`, a valid adjacent tile, and **off cooldown / one birth per tick** (`reproduction_cooldown_ticks`, `last_birth_tick`) (Plan §8, §17).
- [ ] Implement offspring creation: parent pays cost, `offspring_energy_fraction` becomes starting energy (remainder lost), placed in a deterministic adjacent tile, inherits genome + brain (Plan §8, §11).
- [ ] Implement death & corpse energy: predation transfer, and non-predation deaths depositing `corpse_energy_fraction` into the tile pool (capped) — no distinct corpse entity (Plan §11, §17).
- [ ] Implement lineage records: `organism_id`, `parent_id`, `lineage_id`, `birth_tick`, `death_tick`, `generation_depth`, birth/death trait summaries (Plan §8, §14).

**Exit criteria:** Predators can gain energy from kills at a cost; prey can reproduce in bursts across ticks; energy never exceeds the ceiling; lineage trees are reconstructable; determinism holds.

---

## Phase 8 — Mutation
**Goal:** Inheritable variation in traits and brain topology (Plan §8, §4).
**Depends on:** Phase 7.

- [ ] Implement trait mutation: bounded deltas within `trait_bounds`, using the mutation PRNG stream (Plan §8).
- [ ] Implement weight mutation (perturbation) with `weight_mutation_rate` / `weight_mutation_power` (Plan §8).
- [ ] Implement connection mutation (new link between unconnected nodes), assigning a fresh innovation id (Plan §4, §8).
- [ ] Implement node mutation (split an existing connection, insert a hidden node), assigning innovation ids (Plan §4, §8).
- [ ] Confirm recurrent (cycle-creating) connections are permitted, not rejected (Plan §4).

**Exit criteria:** Over a long run, trait distributions drift and brain node/connection counts grow; determinism tests still pass.

---

## Phase 9 — Environmental Stochasticity & Events
**Goal:** Global shocks that punish over-specialization (Plan §6).
**Depends on:** Phase 7 (uses metabolism/energy and density).

- [ ] Implement the per-tick event occurrence roll against the events PRNG stream, updating `environment_modifiers` (Plan §6).
- [ ] Implement Resource Blight (halts biome regen for a duration) (Plan §6).
- [ ] Implement Density-Dependent Plague (energy drain in crowded sub-regions above `plague_density_threshold`) (Plan §6).
- [ ] Implement Climatic Anomaly (±temperature shift, `temperature_anomaly_magnitude`, distorting biome lines) (Plan §6).
- [ ] Age active modifiers and expire them in the Environment phase; feed the Global Stress Level sensor input (Plan §6, §7, §13).

**Exit criteria:** Events trigger deterministically, apply their effects for their duration, and expire; the stress sensor reflects active events; determinism holds.

---

## Phase 10 — Metrics & Analysis Output
**Goal:** Analytics as first-class output (Plan §14).
**Depends on:** Phases 7–9.

- [ ] Track core metrics: population, births/deaths per window, energy min/avg/max, trait averages + histograms, population by biome, grazing/predation success/failure counts, reproduction rate by lineage, extinction events, active events (Plan §14).
- [ ] Record metrics in the Metrics & Snapshot phase (Plan §7, §14).
- [ ] Implement export formats: JSON snapshots plus CSV / newline-delimited JSON for batch analysis/plotting (Plan §14).

**Exit criteria:** A run emits a metrics stream that can be plotted externally; metric values match hand-checked expectations on a tiny fixed-seed world.

---

## Phase 11 — Console App & Transport
**Goal:** The headless CLI surface and its serve/stream modes (Plan §1).
**Depends on:** Phase 10.

- [ ] Implement `sim run --in state.json --out state.json --ticks N` (Plan §1).
- [ ] Implement `sim run ... --out-dir ./frames --stream K` (periodic snapshot frames) (Plan §1).
- [ ] Implement `sim serve --in state.json --port P` exposing snapshots over local HTTP/WebSocket and accepting edited snapshots back (Plan §1).
- [ ] Add a `sim new`/genesis command to create an initial world from config + seed.
- [ ] Ensure the console app is the harness used by the determinism and calibration test suites.

**Exit criteria:** A world can be created, advanced, streamed, and resumed entirely from the CLI.

---

## Phase 12 — Calibration Scenarios
**Goal:** Fixed-seed scenario tests that catch runaway dynamics and broken mechanics (Plan §15).
**Depends on:** Phase 11.

- [ ] Grassland Survival (no early extinction) (Plan §15).
- [ ] Desert Stress (measurable energy pressure on heat-intolerant organisms) (Plan §15).
- [ ] Swamp Movement Cost (abundant energy, visibly higher movement cost) (Plan §15).
- [ ] Predator/Prey Transfer (predation viable but costly/risky) (Plan §15).
- [ ] Overcrowding Plague (dense clones penalized) (Plan §15).
- [ ] Blight Recovery (buffered/flexible populations recover more often) (Plan §15).
- [ ] Assert the calibration goals: no early extinction, no unbounded growth, no single trait dominating every biome, no constraint-free reproduction loops, no cost-free predators (Plan §15).

**Exit criteria:** All calibration scenarios pass at their pinned seeds with the default configuration.

---

## Phase 13 — Shared Avalonia UI Components
**Goal:** Build the visualization and inspection views once, as reusable Avalonia components consumed unchanged by both build targets (Plan §18).
**Depends on:** Phase 11 (needs streamable/loadable snapshots).

- [ ] Implement biome rendering: distinct base colours per biome + ground-energy brightness modulation + event tinting, reconstructed from seed via the Core's Simplex (Plan §2, §18).
- [ ] Implement the two organism channels: fill = colour mode, outline/halo = last action; marker radius ∝ Size; overlays for reproductive-ready / stress / predation flash (Plan §18).
- [ ] Implement the colour modes: Action, Energy, Diet tendency, Stress fit, Lineage (Plan §18).
- [ ] Implement the always-visible, colour-vision-safe legend that updates with the active mode (Plan §18).
- [ ] Implement the organism inspector view: identity/name, physical state, genome vs bounds, per-tick economy breakdown, behaviour + softmax distribution, NEAT graph with live activations, ancestry link (Plan §18, §19).
- [ ] Drive every view from snapshot/state fields only (§12) so it renders identically whether fed a live in-process Core or a deserialized snapshot.

**Exit criteria:** The shared views render a world correctly in a host harness, from both a live Core instance and a loaded snapshot, with no target-specific code.

---

## Phase 14 — Avalonia App: Desktop + Browser (WASM) Targets
**Goal:** Ship the single Avalonia app to native desktop (Win/Linux, full) and the browser (a constrained demo), reusing the Phase 13 views (Plan §1, §16, §18).
**Depends on:** Phases 12, 13.

- [ ] Assemble the app shell hosting the shared views: control deck (pause, frame-step, speed control), colour-mode selector, legend, inspector panel (Plan §1, §18).
- [ ] **Desktop target:** embed `LifeSim.Core` in-process, run the engine on a background thread, render live; verify on Windows and Linux (Plan §1).
- [ ] **Browser target (WASM) — constrained demo:** load snapshots from file and from the `sim serve` stream and render them; do not advance canonical time in stream mode (Plan §1, §9).
- [ ] **Browser target — optional small-world live mode:** embed the Core via WASM to tick *small* worlds with the same engine code, degrading gracefully against the browser's scale/threading/memory limits (Plan §1, §9).
- [ ] Implement editing on both targets that appends explicit `edit_log` entries and (browser stream mode) posts edited snapshots back (Plan §16).
- [ ] Implement snapshot save/load and world exchange with the console app using the shared format (Plan §1, §12).
- [ ] Highlight a selected organism's `env_radius`/`org_radius` footprint (Plan §18, §17).
- [ ] Confirm the WASM bundle size, startup, and framerate meet the Phase 0 spike's accepted demo thresholds; large worlds are viewed via streaming, not simulated in-browser.

**Exit criteria:** One codebase runs as a native desktop app (Win/Linux, live) and as a constrained browser demo (streamed, with optional small-world live mode); a world looks and behaves identically across targets and round-trips through the console app.

---

## Phase 15 — Editing, Branching & Provenance
**Goal:** Make interventions explicit, replayable, and comparable (Plan §16).
**Depends on:** Phase 14.

- [ ] Ensure every UI edit (either Avalonia target) appends a structured `edit_log` entry (Plan §16).
- [ ] Treat an imported edited snapshot as a new deterministic starting point (full state + PRNG streams captured) (Plan §16).
- [ ] Implement `branch_id` / `parent_snapshot_id` so interventions form comparable timelines without overwriting the original run (Plan §16).
- [ ] Add a test: edited-then-resumed run is itself deterministically replayable.

**Exit criteria:** An edited world replays deterministically from the point of edit; branches are traceable to their parent snapshot.

---

## Cross-Cutting / Definition of Done
These hold at every phase, not just one:

- [ ] The two flagship determinism tests (Plan §15) stay green on every merge from Phase 4 onward.
- [ ] Any new randomness draws from the correct named PRNG stream and never from wall-clock/ambient entropy (Plan §9).
- [ ] Any new unordered iteration over simulation-sensitive collections is sorted with explicit tie-breaking (Plan §9).
- [ ] New snapshot fields are added to the JSON Schema and covered by the save/reload test (Plan §12).
- [ ] New config constants live in the `configuration` block (Plan Appendix A), not in source.
- [ ] Anything rendered by a GUI is derived from snapshot/state fields only and has a corresponding legend entry (Plan §18).

---

## Deferred (explicitly out of scope for v1)
Tracked here so they are not mistaken for gaps (Plan §4, §8, §9):
- Sexual reproduction & NEAT crossover (innovation ids are already recorded to enable it later) (Plan §4, §8).
- Fixed-point numeric migration (only if cross-platform replay drift appears) (Plan §9).
- Senescence / lifespan (config knob exists, default off) (Plan §17).
- Active name-collision avoidance via registry/Bloom filter (Plan §19).
