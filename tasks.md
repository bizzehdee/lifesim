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

- [x] Implement the phased tick model in authoritative order: Environment → Sensing → Decision → Intent Resolution → Metabolism → Death & Transfer → Resource Regen → Mutation & Birth Commit → Metrics & Snapshot (Plan §7). *(`Simulation/SimulationWorld.cs` — `Advance()`. Environment/Sensing/Mutation & Birth Commit are documented no-op stubs until the phases that give them content (5-9); Harvest/Reproduce intents likewise stub to 0-cost no-ops until Phase 7/8.)*
- [x] Implement deterministic ordering (ascending `organism_id`) and explicit tie-breaking for all simulation-sensitive iteration (Plan §7, §9). *(Organisms are held in a `SortedDictionary<long, Organism>`; every phase iterates `.Keys` in ascending id order, and movement conflicts resolve implicitly by mutating occupancy as each organism is processed.)*
- [x] Implement **fixed-order floating-point reductions** for cross-organism aggregations; restrict parallelism to per-organism-independent work only (Plan §9). *(No cross-organism reduction exists yet — Phase 4 only sums a plain `Count` for `population`; real aggregations (energy sums, density) arrive with Phase 6/10 and will follow this same ascending-id, single-threaded rule. The decision phase is documented as parallelizable once real per-organism brain inference lands in Phase 5 — the Phase 4 stub itself stays sequential since it draws from one shared PRNG stream.)*
- [x] Implement the one-organism-per-tile spatial model and movement resolution, including **multi-tile path-checking** that stops at the first blocked/occupied tile and pays only distance travelled (Plan §10, §17). *(`SimulationWorld.ResolveIntent` — steps tile-by-tile up to `floor(SpeedCapacity)` tiles, stopping at the first off-grid or occupied tile; `Metabolism.LocomotionTax` is charged only for tiles actually crossed.)*
- [x] Stub the decision phase with a behavior-PRNG random action pick (no NEAT yet) so the loop is exercisable end-to-end. *(`Organisms/StubBrain.cs`, `Organisms/OrganismAction.cs` — uniform pick over the 11 action outputs via the behavior stream.)*
- [x] Implement genesis/initialization: config-driven `initial_population` scattered in Grassland via the genesis PRNG stream (Plan §17). *(`SimulationWorld.CreateGenesis`/`ScatterGenesisPopulation` — mid-range genome, full starting energy; rejection-samples tiles via the genesis stream.)*
- [x] Implement full-extinction handling: halt, set `metrics.extinct = true`, write final snapshot, no auto-reseed (Plan §17). *(`SimulationWorld.Extinct`/`RefreshExtinction`; `Advance()` throws if called again once extinct. `Snapshot/SimulationMetrics.cs` carries `population`/`extinct` — the rest of §14's metrics arrive in Phase 10.)*
- [x] **Flagship test — Seed Replay:** same seed, N ticks, twice → byte-identical snapshots (Plan §15). *(`FlagshipDeterminismTests.SeedReplay_sameSeed_producesByteIdenticalSnapshots`.)*
- [x] **Flagship test — Save/Reload Equivalence:** 100 ticks straight == 50 + serialize + reload + 50 (Plan §15). *(`FlagshipDeterminismTests.SaveReloadEquivalence_matchesAnUninterruptedRun`.)*

**Exit criteria:** A headless world runs for N ticks ✅ (`SimulationWorldTests`, including a real starvation-to-extinction run since Harvest is still stubbed) and both flagship determinism tests pass ✅ (`FlagshipDeterminismTests`, 100 ticks). These two tests now gate all later merges. All 79 tests pass (`dotnet test`); `dotnet format --verify-no-changes` is clean.

**Note:** the organism snapshot block, previously a raw-JSON placeholder, is now a typed `OrganismSnapshot`/`GenomeSnapshot` (id, name, x/y, energy, age, genome, last_action) since the flagship tests need real organism round-tripping; the NEAT brain block (§12) is still to come in Phase 5. `WorldSnapshot.Metrics` is likewise now the typed `SimulationMetrics` (population, extinct) rather than raw JSON, extended with the rest of §14 in Phase 10.

---

## Phase 5 — NEAT Brain (Recurrent)
**Goal:** Replace the stubbed brain with the evolvable recurrent NEAT network (Plan §4).
**Depends on:** Phase 4.

- [x] Implement the NEAT genome: node genes (`id`, `type`, `activation` default `tanh`, `state`) and connection genes (`innovation_id`, `from`, `to`, `weight`, `enabled`) with `network_type: "recurrent"` (Plan §4, §12). *(`Neat/NodeGene.cs`, `Neat/ConnectionGene.cs`, `Neat/NeatGenome.cs`, `Neat/NodeType.cs`.)*
- [x] Implement the global innovation counter (`next_innovation_id`), advanced only in the Birth Commit phase in sorted id order, serialized in snapshots (Plan §4, §9). *(`Neat/NeatTopology.cs` — input/output node ids and the full genesis input→output connectivity are canonical, deterministic constants (id = f(index)), not draws from a mutable allocator, since they're shared baseline structure identical across every genesis organism, not novel mutations. `NextInnovationId` is seeded to `NeatTopology.ReservedInnovationIdCount` so Phase 8's actual structural mutations — the first thing to ever advance this counter — never collide with the reserved range.)*
- [x] Implement **recurrent evaluation via a single synchronous update per tick** (nodes read inputs' previous-tick values, commit together); persist per-node `state` across ticks and zero it at birth (Plan §4, §7). *(`Neat/NeatBrain.cs` — reads a `previousState` snapshot for every computation and only commits the new `NeatGenome` at the end, so node-processing order can't affect the result even with recurrent cycles. Input nodes carry the current tick's sensory reading directly; hidden/output nodes sum incoming connections in ascending-innovation-id order for a fixed floating-point reduction order (Plan §9). `CreateMinimalFullyConnected` initializes every node's `state` to 0.)*
- [x] Implement the 11 action outputs → softmax → weighted PRNG selection: Move N/S/E/W, Harvest-Self, Harvest-N/S/E/W, Idle, Reproduce (Plan §4, §17). *(`NeatBrain.Evaluate` — numerically-stable softmax over the tanh-squashed output activations, then a cumulative-probability roll against the behavior PRNG stream.)*
- [x] Verify node `state` round-trips through serialization (extend save/reload test to include a recurrent brain). *(`FlagshipDeterminismTests.RecurrentNodeState_isNonZeroAfterTicking_andRoundTripsExactly`; the existing Seed Replay/Save-Reload flagship tests also now exercise real (non-zero) brain state throughout.)*

**Exit criteria:** Organisms act via their networks ✅ — `SimulationWorld`'s Decision Phase now calls `NeatBrain.Evaluate` instead of the retired `StubBrain`. Determinism tests still pass with recurrent state included in the snapshot ✅ (both flagship tests, `NeatBrainTests`, `NeatGenomeFactoryTests`). All 92 tests pass (`dotnet test`); `dotnet format --verify-no-changes` is clean.

**Judgment calls worth flagging:**
- **Input width is a placeholder.** Phase 5's task list doesn't own the sensory vector (that's explicitly Phase 6's job), so genesis brains use 4 placeholder inputs (`energy`, `age`, `tile temperature`, `biome friction` — see `SimulationWorld.BuildPlaceholderInputs`) rather than guessing at §13's full vector width. `NeatTopology.InputCount` and the genesis wiring change together when Phase 6 lands; there's no persisted production data yet, so this is a cheap swap, not a migration.
- **Genesis connection weights** are drawn from the **genesis** PRNG stream (not explicitly named in §9 for this purpose, but consistent with genesis already owning organism-creation randomness).

---

## Phase 6 — Sensing & Observation Inputs
**Goal:** The fixed-size, normalized sensory vector feeding the brain (Plan §13, §4).
**Depends on:** Phase 5.

- [x] Implement the Sensing phase snapshot: inputs cached from start-of-tick world state, unaffected by same-tick decisions (Plan §7). *(`SimulationWorld.Advance()` now has an explicit Sensing Phase step that builds every organism's vector — via `SensoryInputBuilder.Build` — fully before the Decision Phase loop runs, so no organism's movement this tick can leak into another's inputs.)*
- [x] Build the full authoritative fixed input vector from Plan §13 (energy, age, tile temp, biome/friction, richest-tile vector, closest-organism vector + size delta, smaller/larger neighbor counts, local density, last action result, reproductive readiness, global stress). *(`Sensing/SensoryInputBuilder.cs`, indexed by the named `Sensing/SensoryField.cs` enum (17 fields) — this finalizes and replaces Phase 5's 4-input placeholder; `NeatTopology.InputCount` is now 17.)*
- [x] Normalize all inputs into stable ranges before the network (Plan §13). *(Each field has its own normalization — see judgment calls below — landing everything in roughly [-1, 1] before noise.)*
- [x] Inject Gaussian noise scaled by `sensory_acuity` after normalization, using the sensory-noise PRNG stream (Plan §4, §13). *(`SensoryInputBuilder.InjectNoise` — stdDev = `(1 - sensory_acuity) * 0.3`, applied uniformly across all 17 fields, clamped to [-2, 2]; consumes `PrngStream.SensoryNoise`.)*

**Exit criteria:** Brains receive a fixed-width normalized vector ✅ (17 inputs, `SensoryInputBuilderTests.Build_returnsExactlyOneValuePerSensoryField`); low-acuity organisms measurably receive noisier inputs ✅ (`Build_lowAcuity_measurablyVariesAcrossNoiseDraws` vs `Build_maxAcuity_injectsNoNoise_regardlessOfNoiseStreamState`); determinism holds ✅ (both flagship tests still pass, now exercising the real sensing pipeline throughout). 99 Core tests + 10 determinism tests pass (`dotnet test`); `dotnet format --verify-no-changes` is clean.

**Judgment calls worth flagging (§13 prescribes *what*, not the exact encoding):**
- **Direction vectors** are unit (dx, dy) components (2 values each) rather than a single angle, for smoothness/no wraparound — richest-tile and closest-organism each cost 2 input slots for this.
- **Distance normalization** divides by the organism's own `env_radius`/`org_radius` (not a fixed world constant), so the signal stays meaningful as `trait_bounds` are retuned; "nothing found / target is self" resolves to distance 0, direction (0, 0) rather than a sentinel far value.
- **`last_action_result` is a new organism field** (`Organisms/ActionResult.cs`: `None`/`Success`/`Blocked`/`NoOp`) since the concept didn't exist before Phase 6. Move now has a real result (`Success` if it travelled, `Blocked` if immediately blocked); Idle is always `Success`; Harvest-\*/Reproduce are `NoOp` until Phase 7/8 give them real outcomes.
- **Reproductive readiness** is just the energy-vs-cost check (`energy >= reproduction_base_cost * size`) — cooldown gating (`last_birth_tick`) doesn't exist yet since nothing has ever reproduced (Phase 7/8). **Global stress level** is a fixed 0.0 until Phase 9 ships events.
- **Noise applies to all 17 fields uniformly**, including internal telemetry (energy, age) — §13 doesn't carve out an exception, so this is the simplest literal reading rather than a special-cased "external senses only" scheme.

---

## Phase 7 — Interactions: Harvest, Combat, Reproduction & Lineage
**Goal:** The universal Harvest action (grazing/predation), reproduction, and lineage tracking (Plan §5, §8, §10, §11).
**Depends on:** Phase 6.

- [x] Implement directional Harvest targeting (`Harvest-Self` + `Harvest-N/S/E/W`); off-grid/empty targets resolve as a no-op paying base costs (Plan §10, §5). *(`Simulation/SimulationWorld.cs` — `ResolveHarvest`; off-grid → `ActionResult.NoOp`, on-grid-but-empty → ambient grazing (possibly zero gain) → `Success`.)*
- [x] Implement ambient grazing: drain ground energy from the targeted tile into the organism, clamped to the energy ceiling (Plan §5, §11). *(`ResolveHarvest`'s grazing branch drains the tile's *entire* current energy via `GroundEnergyGrid.Drain`, then `Organism.AddEnergy` clamps at the ceiling — any surplus is lost, not banked, matching §11.)*
- [x] Implement probabilistic predatory combat: `P(kill) = Size_a / (Size_a + Size_v)` rolled against the behavior/combat stream; transfer on success, retaliation penalty on failure (Plan §5, §11). *(`Organisms/Combat.cs` — `KillProbability`, extracted as a pure function for direct testing; rolled against the same `Prng` instance already used for softmax action selection this tick, i.e. `PrngStream.Behavior`. Success zeros the victim and transfers `predation_transfer_fraction` of its pre-kill energy to the attacker; failure charges the attacker `failed_combat_penalty`.)*
- [x] Implement asexual reproduction gating: `Reproduce` selected, energy ≥ `reproduction_base_cost × size`, a valid adjacent tile, and **off cooldown / one birth per tick** (`reproduction_cooldown_ticks`, `last_birth_tick`) (Plan §8, §17). *(`ResolveReproduce`; `Organism.LastBirthTick`/`RecordBirth` — a genuinely new field, since nothing had ever reproduced before this phase. "One birth per tick" falls out for free: an organism can only take one action per tick.)*
- [x] Implement offspring creation: parent pays cost, `offspring_energy_fraction` becomes starting energy (remainder lost), placed in a deterministic adjacent tile, inherits genome + brain (Plan §8, §11). *(Offspring are **reserved** during Intent Resolution — id allocated, tile locked in `_occupancy` — but only **materialized** into the live organism index during the Mutation & Birth Commit phase, per §7's phase semantics. Brain is an exact copy of the parent's with node `state` reset to 0 (`ResetBrainState`); no mutation yet (Phase 8).)*
- [x] Implement death & corpse energy: predation transfer, and non-predation deaths depositing `corpse_energy_fraction` into the tile pool (capped) — no distinct corpse entity (Plan §11, §17). *(Death & Transfer phase deposits `corpse_energy_fraction * energyBeforeMetabolism` — the energy an organism had **before** that tick's fatal Metabolism deduction, captured in the Metabolism phase. This is what makes corpse energy correctly zero for predation deaths (already zeroed pre-Metabolism) without any special-casing, and correctly nonzero for starvation deaths.)*
- [x] Implement lineage records: `organism_id`, `parent_id`, `lineage_id`, `birth_tick`, `death_tick`, `generation_depth`, birth/death trait summaries (Plan §8, §14). *(`Organisms/LineageEntry.cs` (mutable, opened at birth/closed at death) + `Snapshot/LineageSnapshot.cs`. Genesis organisms found their own lineage (`lineage_id = organism_id`); offspring inherit the parent's `lineage_id` unchanged (asexual, no crossover).)*

**Exit criteria:** Predators can gain energy from kills at a cost ✅ (`CombatTests`); prey can reproduce in bursts across ticks ✅ (`Advance_populationGrows_viaReproduction_whenEnergyIsAbundant`); energy never exceeds the ceiling ✅ (checked every tick in the same test); lineage trees are reconstructable ✅ (parent/generation-depth consistency checked across all records); determinism holds ✅ (both flagship tests pass with the full Phase 7 pipeline live, including real grazing/combat/reproduction every tick). 123 tests pass (`dotnet test`); `dotnet format --verify-no-changes` is clean.

**Judgment calls worth flagging:**
- **Grazing drains the whole tile in one action** (not a config-defined per-harvest rate — no such field exists in Appendix A). Combined with slow biome regen, one graze can be a large windfall relative to metabolism cost; if that proves unbalanced, Phase 12 calibration is where it gets tuned, not this phase.
- **Reproduction adjacent-tile priority is fixed N, S, E, W** (matching the `Move` action's direction convention) — §8 requires "a deterministic valid adjacent tile" but doesn't specify which one when multiple are free.
- **A killed organism doesn't get to act later in the same tick's Intent Resolution pass** even if its own turn (by ascending id) comes after its killer's — dead organisms are skipped. A related edge case is accepted as-is: a corpse can still be "attacked" again by a later-processed organism this same tick (occupancy isn't cleared until Death & Transfer), which is a harmless no-op (nothing left to transfer).
- **`ActionResult.Blocked` and `ActionResult.Failed`** (new: failed combat, failed reproduction) map to the same sensory value (-1.0) as an "attempt didn't work" signal — kept as distinct enum values for clarity in code/logs, merged for the brain's purposes.

---

## Phase 8 — Mutation
**Goal:** Inheritable variation in traits and brain topology (Plan §8, §4).
**Depends on:** Phase 7.

- [x] Implement trait mutation: bounded deltas within `trait_bounds`, using the mutation PRNG stream (Plan §8). *(`Organisms/GenomeMutator.cs` — each trait mutates independently at `trait_mutation_rate`; the perturbation is a uniform delta scaled to `trait_mutation_delta` × the trait's own bound span, then hard-clamped via `Genome.Clamped`.)*
- [x] Implement weight mutation (perturbation) with `weight_mutation_rate` / `weight_mutation_power` (Plan §8). *(`Neat/NeatMutator.MutateWeights` — per-connection Gaussian perturbation `weight += N(0,1) * weight_mutation_power` at `weight_mutation_rate`; draws ordered by ascending innovation id for a storage-order-independent sequence (Plan §9).)*
- [x] Implement connection mutation (new link between unconnected nodes), assigning a fresh innovation id (Plan §4, §8). *(`Neat/NeatMutator.MaybeAddConnection` — enumerates unconnected (from, to) candidate pairs in ascending order (input nodes are never a target), picks one via the mutation stream, and allocates a fresh innovation id from `Neat/InnovationIdAllocator.cs`.)*
- [x] Implement node mutation (split an existing connection, insert a hidden node), assigning innovation ids (Plan §4, §8). *(`Neat/NeatMutator.MaybeAddNode` — disables a chosen enabled connection and re-bridges it through a new hidden node with the canonical `from→new` weight 1.0 / `new→to` inherited-weight split; the new node id and both replacement innovation ids draw from the shared counter.)*
- [x] Confirm recurrent (cycle-creating) connections are permitted, not rejected (Plan §4). *(No acyclicity check — self-loops and back-edges are valid connection candidates; `NeatMutatorTests.Mutate_permitsRecurrentConnections_includingSelfLoops`.)*

**Exit criteria:** Over a long run, trait distributions drift and brain node/connection counts grow ✅ (`SimulationWorldTests.Advance_overALongRun_driftsTraitsAndGrowsBrainTopology_viaMutation` — asserts the innovation counter advances past the reserved genesis range, at least one brain grows a hidden node, and birth-trait `Size` values diverge across lineages); determinism tests still pass ✅ (both flagship tests, now exercising live mutation every birth). 135 tests pass (`dotnet test`); `dotnet format --verify-no-changes` is clean.

**Judgment calls worth flagging (§8 prescribes *what*, not the exact encoding):**
- **Trait delta scales with each trait's bound span**, so the single `trait_mutation_delta` config value (0.05) drifts every trait by the same *relative* step (≤5% of its range) regardless of absolute magnitude — otherwise a fixed absolute delta would be negligible for wide-range traits (`thermal_center`) and drastic for narrow ones (`sensory_acuity`). Traits use a *uniform* bounded delta (matching §8's "small bounded deltas"); weights use a *Gaussian* perturbation (canonical NEAT, consistent with `weight_mutation_power` as a std-dev).
- **Node ids and connection innovation ids share one monotonic counter** (`next_innovation_id`) — a node's `id` *is* an innovation number in this model (§4/§12) — so a node split allocates three consecutive ids (node, inbound conn, outbound conn) in that fixed order.
- **Mutation runs in the Mutation & Birth Commit phase** (§7), in ascending offspring-id order, so both the mutation stream and the innovation counter advance deterministically. Within one offspring the order is fixed: trait mutation, then weight → connection → node brain mutation.
- **Draw counts stay stable across the roll even when a structural mutation can't apply** (fully-connected network, no enabled connections): the gating probability roll is still consumed, only the follow-up draws are skipped, so an unlucky world layout can't desync the stream.
- **Each mutation event takes the next global innovation id** rather than deduplicating structurally-identical mutations within a tick to a shared id (a simplification the spec's "assign a fresh innovation id" permits) — homologous-gene alignment for future crossover (§8, deferred) reads the recorded ids as-is.

---

## Phase 9 — Environmental Stochasticity & Events
**Goal:** Global shocks that punish over-specialization (Plan §6).
**Depends on:** Phase 7 (uses metabolism/energy and density).

- [x] Implement the per-tick event occurrence roll against the events PRNG stream, updating `environment_modifiers` (Plan §6). *(`Events/EnvironmentState.cs` — `RunEnvironmentPhase` rolls blight → plague → anomaly in fixed order against `PrngStream.Events`; the roll is always consumed even when a modifier of that type is already active, so draw counts stay history-independent. Modifiers are now a typed `Events/EnvironmentModifier.cs` block (`Events/EventType.cs`), replacing the raw-JSON placeholder in the snapshot.)*
- [x] Implement Resource Blight (halts biome regen for a duration) (Plan §6). *(`SimulationWorld.Advance` skips `GroundEnergyGrid.RegenerateTick` entirely while `EnvironmentState.BlightActive`; `SimulationWorldTests.Advance_resourceBlight_suspendsGroundEnergyRegeneration`.)*
- [x] Implement Density-Dependent Plague (energy drain in crowded sub-regions above `plague_density_threshold`) (Plan §6). *(Metabolism phase adds `PlagueEnergyDrainPerTick` to any organism whose 3×3 occupancy count — `SimulationWorld.LocalOrganismDensity` — meets `PlagueDensityThreshold`; `Advance_densityPlague_drainsCrowdedOrganismsByTheConfiguredAmount`.)*
- [x] Implement Climatic Anomaly (±temperature shift, `temperature_anomaly_magnitude`, distorting biome lines) (Plan §6). *(An active anomaly carries a signed ±`temperature_anomaly_magnitude` (heatwave/ice-age, sign from the events stream); `EnvironmentState.TemperatureOffset` is added to the °C tile temperature fed to both thermal-stress metabolism and the sensory tile-temp input; `Advance_climaticAnomaly_raisesMetabolicCostViaThermalStress`.)*
  - **Temperature model resolved to °C alongside this phase** (was a latent seam): `TerrainSampler.TemperatureCelsiusAt` = biome baseline °C + temperature-noise × `temperature_variation` (new config, default 5°C). Thermal-stress metabolism and the temperature sensor now read this instead of the raw [-1,1] noise field (which still drives biome *classification*), so an organism's °C `thermal_center` is compared against a °C tile temperature — Desert (45°C) genuinely stresses cold-adapted organisms, Ice Sheet (−15°C) warm-adapted ones, and the previously-dead biome °C config values are now live. Unblocks Phase 12's Desert Stress scenario. (`TerrainSamplerTests.TemperatureCelsiusAt_*`.)
  - **Incidental Phase 7 fix:** `ResolveHarvest` no longer throws when an organism harvests toward a tile reserved for an offspring not yet materialized (reservation lives in occupancy but not the organism index until Birth Commit) — that case now falls through to ambient grazing instead of a phantom combat lookup.
- [x] Age active modifiers and expire them in the Environment phase; feed the Global Stress Level sensor input (Plan §6, §7, §13). *(`EnvironmentState.RunEnvironmentPhase` ages/expires first, then rolls; `EnvironmentState.GlobalStress` (graded by active-event-type count, saturating at 1.0) is passed into `SensoryInputBuilder.Build` and lands in `SensoryField.GlobalStressLevel`, replacing Phase 6's fixed 0.0.)*

**Exit criteria:** Events trigger deterministically, apply their effects for their duration, and expire ✅ (`EnvironmentStateTests` — certain-blight activates then expires after exactly its duration; anomaly sign/magnitude; graded stress); the stress sensor reflects active events ✅ (`SensoryInputBuilderTests.Build_globalStressLevel_reflectsThePassedEnvironmentStress`); determinism holds ✅ (`FlagshipDeterminismTests.Determinism_holds_whileEventsFireFrequently` — seed-replay + save/reload equivalence with all three events firing throughout, plus both original flagship tests). 136 Core + 11 determinism tests pass (`dotnet test`); `dotnet format --verify-no-changes` is clean.

**Judgment calls worth flagging:**
- **Resource Blight is global in v1, not per-biome-targeted.** §6 describes blight halting regen "across entire targeted biomes"; the simplest faithful reading that still forces the intended pivot to predation is to suspend *all* ground regen while any blight is active. Per-biome targeting is a calibration-phase refinement, not core mechanics.
- **Climatic Anomaly shifts the temperature fed to metabolism/sensing, not the biome classifier.** The mechanically important consequence — "turning rich territories lethal overnight" — is thermal-stress metabolism, which this delivers. The literal re-drawing of biome *lines* is a rendering concern (§18 already specifies a warm/cool event overlay reconstructed at draw time), so the Core leaves biome classification (noise-space) untouched and applies the °C offset to physical temperature instead. (The prior noise-vs-°C temperature-units seam was resolved this phase — see the Climatic Anomaly task note above.)
- **Density = organisms in the 3×3 (Chebyshev-1) block, counted from settled post-movement occupancy** (an integer, so order-independent per §9). §6 says "sharing tiles or immediate neighbors"; with one-organism-per-tile, that's exactly the Moore neighborhood.
- **New config constant `plague_energy_drain_per_tick`** (default 2.0) — Appendix A defines `plague_density_threshold` but no drain magnitude, so this adds the missing knob to the versioned config block rather than hard-coding it (per the cross-cutting config rule).
- **Global Stress Level is graded by active-event-type count / 3** (0, 0.33, 0.66, 1.0) rather than a binary flag, giving brains a signal that distinguishes a single shock from a compounding multi-disaster. At most one modifier per type is ever active.
- **At most one modifier of each type is active at a time** (a re-roll while active is ignored, though still consumed) — keeps stacked offsets/drains bounded and the serialized block small.

---

## Phase 10 — Metrics & Analysis Output
**Goal:** Analytics as first-class output (Plan §14).
**Depends on:** Phases 7–9.

- [x] Track core metrics: population, births/deaths per window, energy min/avg/max, trait averages + histograms, population by biome, grazing/predation success/failure counts, reproduction rate by lineage, extinction events, active events (Plan §14). *(`Snapshot/SimulationMetrics.cs` expanded from the Phase 4 population/extinct stub to the full §14 set, with supporting records `TraitAverages`, `TraitHistogram`, `BiomePopulation`, `LineageReproduction`. Flow counters (births/deaths, grazing/predation success/failure) are tallied during the tick via a private `TickCounters`; distributions/averages/histograms/biome+lineage breakdowns are computed from settled state in `SimulationWorld.BuildMetrics`, iterating organisms in ascending-id order for fixed-order float reductions (Plan §9). Extinction is the existing `extinct` flag; active events come from the Phase 9 `EnvironmentState`.)*
- [x] Record metrics in the Metrics & Snapshot phase (Plan §7, §14). *(`Advance()` phase 9 calls `BuildMetrics` into `_metrics`, surfaced in every `ToSnapshot()`; genesis and `FromSnapshot` seed it too — saved metrics are restored on reload so an immediate re-snapshot round-trips.)*
- [x] Implement export formats: JSON snapshots plus CSV / newline-delimited JSON for batch analysis/plotting (Plan §14). *(`Metrics/MetricsExporter.cs` — `CsvHeader`/`CsvRow` emit the flat scalar time series (invariant-culture numbers), `NdjsonLine` emits the full per-tick record including nested histograms/lineage breakdowns via a dedicated compact source-gen context `Metrics/MetricsJsonContext.cs` + `Metrics/MetricsSample.cs`. JSON snapshots already carry the metrics block.)*

**Exit criteria:** A run emits a metrics stream that can be plotted externally ✅ (`MetricsExporterTests` — matching CSV header/row column counts, invariant-culture formatting, single-line NDJSON that parses back); metric values match hand-checked expectations on a fixed-seed world ✅ (`MetricsTests` — histogram buckets & per-biome counts partition the population, energy/trait averages match a direct recompute, and cumulative births/deaths counters equal the lineage-record totals over a 60-tick run; a single-starvation tick reports exactly one death; active events mirror the environment). 146 Core + 11 determinism tests pass (`dotnet test`); metrics determinism is gated by the flagship tests, which compare full snapshot JSON (now including the metrics block). `dotnet format --verify-no-changes` is clean.

**Judgment calls worth flagging:**
- **"Per window" = per tick.** The metrics block records the flow counters for the single tick that produced it; any wider window is an aggregation the downstream stream/analysis does, not the engine.
- **CSV is the flat scalar time series; NDJSON is the full record.** Histograms and per-lineage reproduction don't fit a flat table, so CSV omits them (documented) while NDJSON carries everything. Numbers use invariant culture + round-trip (`"R"`) formatting so files are locale-portable.
- **Reproduction-by-lineage is scoped to currently-living lineages** (cumulative births per lineage that still has members), so the list stays proportional to the live population rather than growing with all-time lineage history.
- **Trait histograms use 10 fixed bins across each trait's hard bounds** (`trait_bounds`), so bins are stable across ticks and comparable run-to-run; bin count is an engine constant, not yet a config knob.
- **Enums serialize as their PascalCase member names** in the metrics block (biome, active events), matching the existing snapshot convention (`last_action` etc.) rather than snake_case.
- **`SimulationMetrics`/`TraitHistogram` get value equality** (sequence-equal on their list members), mirroring `NeatGenome`, so metrics are directly comparable in tests and round-trip assertions.

---

## Phase 11 — Console App & Transport
**Goal:** The headless CLI surface and its serve/stream modes (Plan §1).
**Depends on:** Phase 10.

- [x] Implement `sim run --in state.json --out state.json --ticks N` (Plan §1). *(`Cli/RunCommand.cs` — loads the snapshot, advances N ticks (halting early on extinction), writes the final snapshot. Optional `--metrics FILE --metrics-format csv|ndjson` streams the Phase 10 metrics per tick via `MetricsExporter`.)*
- [x] Implement `sim run ... --out-dir ./frames --stream K` (periodic snapshot frames) (Plan §1). *(`--out-dir DIR --stream K` writes `frame_<tick8>.json` every K ticks a UI can poll.)*
- [x] Implement `sim serve --in state.json --port P` exposing snapshots over local HTTP/WebSocket and accepting edited snapshots back (Plan §1). *(`Cli/ServeCommand.cs` + `Serve/SnapshotService.cs` (thread-safe world holder) + `Serve/SimHttpServer.cs` (BCL `HttpListener`, no ASP.NET): `GET /snapshot`, `POST /snapshot` (import edited state, 400 on invalid), `GET /metrics`, `GET /health`, and a `GET /stream` WebSocket that pushes a frame whenever the tick advances. A background loop advances at `--tps` ticks/s until Ctrl+C / extinction / `--max-ticks`.)*
- [x] Add a `sim new`/genesis command to create an initial world from config + seed. *(`Cli/NewCommand.cs` — `--seed/--width/--height/--population` flags plus optional `--config FILE` (a standalone `SimulationConfig` block, (de)serialized via new `SnapshotSerializer.SaveConfig`/`LoadConfig` so it matches the snapshot encoding).)*
- [x] Ensure the console app is the harness used by the determinism and calibration test suites. *(CLI logic lives in a unit-testable `Cli/SimCli.cs` dispatcher (thin `Program.cs`); `LifeSim.Console.Tests` drives the engine entirely through the CLI, including a **seed-replay** determinism check and a **split-run == uninterrupted-run** equivalence check performed via `sim new`/`sim run`. Phase 12's calibration scenarios build on this harness.)*

**Exit criteria:** A world can be created, advanced, streamed, and resumed entirely from the CLI ✅ — verified both by real-process smoke runs (`sim new` → `sim run --stream --metrics` → `sim serve` over HTTP) and by the `LifeSim.Console.Tests` suite (16 tests: `new`/`run`/streaming/metrics-CSV+NDJSON/resume-equivalence/seed-replay, `SnapshotService` get/import/advance, and a real-socket `SimHttpServer` GET/POST round-trip). 146 Core + 11 determinism + 16 console tests pass (`dotnet test`); `dotnet format --verify-no-changes` is clean.

**Judgment calls worth flagging:**
- **Serve uses BCL `HttpListener`, not ASP.NET.** Keeps the console lightweight and dependency-free (matching the Core's minimalism); the endpoint set is small and demo-oriented, since the browser target (Phase 14) is its main consumer.
- **The WebSocket `/stream` pushes on tick-change via a 100 ms poll of the shared service**, rather than being event-driven off the engine loop — simpler and decoupled, adequate for a demo stream.
- **`sim new` config precedence:** `--config` file (or built-in defaults) first, then `--population` overrides it; world dimensions/seed always come from flags. A full config file is the escape hatch for tuning anything else.
- **Metrics stream granularity is per tick** (one CSV row / NDJSON line per advanced tick); CSV carries the flat scalar columns (Phase 10), NDJSON the full record.
- **`serve --max-ticks` stops the *engine* but keeps *serving*** the final snapshot (the world is still inspectable after it stops advancing); the process exits on Ctrl+C.
- **New `LifeSim.Console.Tests` project** added to the solution as the CLI/determinism harness home.

---

## Phase 12 — Calibration Scenarios
**Goal:** Fixed-seed scenario tests that catch runaway dynamics and broken mechanics (Plan §15).
**Depends on:** Phase 11.

- [x] Grassland Survival (no early extinction) (Plan §15). *(`BaselineScenarioTests.GrasslandSurvival_*` — genesis pop 40 on a 64×64 grassland-adequate world, 150 ticks via the CLI: never extinct mid-run, healthy final population, bounded max.)*
- [x] Desert Stress (measurable energy pressure on heat-intolerant organisms) (Plan §15). *(`DesertStress_*` — same heat-intolerant genome on a desert vs a grassland tile (drained, stationary) loses strictly more energy in one tick, from the °C thermal-stress model calibrated in Phase 9.)*
- [x] Swamp Movement Cost (abundant energy, visibly higher movement cost) (Plan §15). *(`SwampMovementCost_*` — swamp energy cap & regen exceed grassland (abundant), while identical travel costs more under swamp friction via `Metabolism.LocomotionTax`.)*
- [x] Predator/Prey Transfer (predation viable but costly/risky) (Plan §15). *(`PredatorPreyTransfer_*` — `Combat.KillProbability(8,1)∈(0.5,1)` and `(1,1)=0.5`; failure penalty >0, transfer fraction ∈(0,1), bigger bodies cost more upkeep; plus a live 30-tick arena where a large predator ringed by prey lands energy-transferring kills (`successful_predation>0`).)*
- [x] Overcrowding Plague (dense clones penalized) (Plan §15). *(`OvercrowdingPlague_*` — under a certain plague, a diagonally-crowded pair (density 2, no orthogonal interaction) is drained below an isolated organism (density 1), isolating density-dependence with a thermal-neutral genome.)*
- [x] Blight Recovery (buffered/flexible populations recover more often) (Plan §15). *(`BlightRecovery_*` — during a pre-loaded blight window (regen halted, tiles drained), the buffered founder's lineage rides out the collapse and reproduces while the reserve-less founder starves before it can, so its lineage dies out; the blight then expires (collapse is temporary).)*
- [x] Assert the calibration goals: no early extinction, no unbounded growth, no single trait dominating every biome, no constraint-free reproduction loops, no cost-free predators (Plan §15). *(`CalibrationGoalsTests` — a 200-tick default run stays non-extinct and bounded (≤6× start); trait mutation spreads Size across survivors (no single value dominates); config guarantees predation is penalised & size-scaled and reproduction is a net energy sink (`offspring_energy_fraction < 1`).)*

**Calibration tuning (Plan §15, Appendix A defaults were illustrative):** with random genesis brains the Appendix-A defaults bled every seed toward extinction (reproduction unaffordable, org-radius sensory tax dominating metabolism). Tuned three defaults so grassland populations self-sustain without exploding: `reproduction_base_cost 10 → 3`, `sensory_tax_c2 (org-radius quadratic) 0.01 → 0.002`, grassland `regen_rate 0.5 → 0.8`. Verified across 8 seeds (500-tick runs: no extinction, growth bounded ≈2× start).

**Exit criteria:** All calibration scenarios pass at their pinned seeds with the (tuned) default configuration ✅. New `LifeSim.Calibration.Tests` project (9 tests) drives every scenario through the `sim` CLI harness (Phase 11). 146 Core + 11 determinism + 16 console + 9 calibration tests pass (`dotnet test`, Debug & Release); `dotnet format --verify-no-changes` is clean.

**Judgment calls worth flagging:**
- **Calibration tuned the defaults rather than weakening the tests.** The Appendix-A values are explicitly illustrative; a default that reliably dwindles to extinction is the "broken dynamics" this phase exists to catch, so the fix is the config, not the thresholds.
- **The `Advance_populationCanStillGoExtinct` unit test now forces extinction with an explicitly harsh metabolism config** (base cost ×size cranked up), since the tuned default is deliberately survivable — it tests the extinction *mechanism*, not an unsustainable default.
- **Mechanic-heavy scenarios (Desert/Plague/Blight) use hand-placed, thermal-neutral, stationary genomes** to isolate the one pressure under test from movement/sensing/thermal confounds; Predator/Prey and Swamp lean on direct `Combat`/`Metabolism`/config assertions plus (for predation) one live run, because stochastic combat makes a pure end-state assertion flaky.
- **Blight Recovery asserts at the lineage level, not a specific organism id** — genesis brains are random (not truly inert), so the buffered founder reproduces and its *lineage* is what recovers; the reserve-less founder's lineage dies out.
- **Scenarios run through the CLI** (`sim new`/`sim run` with the metrics stream) to honour "the console app is the harness used by the calibration test suite" (Phase 11).

---

## Phase 13 — Shared Avalonia UI Components
**Goal:** Build the visualization and inspection views once, as reusable Avalonia components consumed unchanged by both build targets (Plan §18).
**Depends on:** Phase 11 (needs streamable/loadable snapshots).

- [x] Implement biome rendering: distinct base colours per biome + ground-energy brightness modulation + event tinting, reconstructed from seed via the Core's Simplex (Plan §2, §18). *(`Presentation/SimulationPalette.cs` biome tokens + `ModulateByEnergy`; `Presentation/BiomeColours.cs` composes biome × energy-brightness × event tint (blight desaturates, anomaly warms/cools); `WorldScene.TileColour` reconstructs biomes via the Core's `TerrainSampler` + `GroundEnergyGrid`.)*
- [x] Implement the two organism channels: fill = colour mode, outline/halo = last action; marker radius ∝ Size; overlays for reproductive-ready / stress / predation flash (Plan §18). *(`Presentation/OrganismColours.cs` (`Fill`/`Outline`/`Radius`) + `Presentation/OrganismView.cs`; the canvas draws overlays. Needed a small Core fix — new `ActionResult.Killed` — so a successful predation is distinguishable from a graze in the snapshot (it reads as success to the brain).)*
- [x] Implement the colour modes: Action, Energy, Diet tendency, Stress fit, Lineage (Plan §18). *(`Presentation/ColourMode.cs` + `OrganismColours.Fill` per mode; `LineageColour.cs` hashes `lineage_id` → stable hue.)*
- [x] Implement the always-visible, colour-vision-safe legend that updates with the active mode (Plan §18). *(`Presentation/Legend.cs` — `LegendBuilder` emits the active fill mode's entries plus the always-on channels (action outlines, overlay glyphs, biome swatches); every colour has an entry and each carries a glyph so the key never relies on colour alone.)*
- [x] Implement the organism inspector view: identity/name, physical state, genome vs bounds, per-tick economy breakdown, behaviour + softmax distribution, NEAT graph with live activations, ancestry link (Plan §18, §19). *(`ViewModels/OrganismInspectorViewModel.cs` computes every block from the snapshot via Core (`Metabolism` breakdown, new `NeatBrain.ActionProbabilities` for the softmax, `NeatGraphLayout` for the graph); `Views/OrganismInspectorView.axaml` + `Views/NeatGraphControl.cs` (activation-coloured nodes, weighted edges, recurrent links dashed).)*
- [x] Drive every view from snapshot/state fields only (§12) so it renders identically whether fed a live in-process Core or a deserialized snapshot. *(Everything renders from a `WorldSnapshot`; a live Core supplies one via `ToSnapshot()`, a file via `SnapshotSerializer.Load`. `WorldViewModel.LoadSnapshot` is source-agnostic. Verified: `WorldViewTests.WorldScene_isIdenticalFromALiveSnapshotAndAReloadedOne` asserts byte-for-byte-equivalent scenes across all five colour modes.)*

**Exit criteria:** The shared views render a world from live and loaded snapshots with no target-specific code ✅. All views live in `LifeSim.App` and compile unchanged for both the desktop and browser heads (both build green). New `LifeSim.App.Tests` (19 tests) covers the pure render-data producers — palette ramps, biome/event tint, per-mode fills, action outlines (incl. predation-vs-graze), marker radius, legend completeness, NEAT layout + recurrence flagging, the full inspector projection, `WorldViewModel` reactions, and the live-vs-loaded scene-identity guarantee. 202 tests total pass (`dotnet test`, Debug & Release); `dotnet format --verify-no-changes` clean.

**Judgment calls / notes worth flagging:**
- **Rendering is verified by build + pure-logic tests, not pixels.** All colour/economy/graph computation lives in pure, unit-tested classes; the custom `Control`s (`SimulationCanvas`, `NeatGraphControl`) are thin `DrawingContext` wrappers over them. A visual/headless-render smoke test belongs with Phase 14 (which actually launches the app per target).
- **New Core `ActionResult.Killed`** distinguishes a successful predation from a graze — §18 requires the predation outline/flash, but Phase 7 had merged both under `Success`. It maps to the same brain input as `Success`, so dynamics are unchanged; determinism snapshots regenerate.
- **Diet tendency reads the *current* `last_action`, not a history** — the v1 snapshot stores no action history, and deriving diet from accumulated live frames would make live and loaded frames diverge (breaking the identical-rendering contract). A true history-based diet needs a stored history field.
- **Inspector "last movement cost" is indicative** (a 1-tile move under the current tile's friction when the last action was a Move) — the snapshot doesn't store the actual distance travelled; the other economy terms (base/thermal/sensory) are exact.
- **The map canvas fits-to-view with click-to-select; pan/zoom + viewport culling/LOD are deferred to Phase 14** (per the perf reference), acceptable for the moderate host-harness worlds here.
- **`MainViewModel` is a Phase 13 host stand-in** — it seeds a small genesis world and advances a few ticks so the views render real Core-produced state on launch; Phase 14 replaces it with per-target live-engine / snapshot-loading wiring.

---

## Phase 14 — Avalonia App: Desktop + Browser (WASM) Targets
**Goal:** Ship the single Avalonia app to native desktop (Win/Linux, full) and the browser (a constrained demo), reusing the Phase 13 views (Plan §1, §16, §18).
**Depends on:** Phases 12, 13.

- [x] Assemble the app shell hosting the shared views: control deck (pause, frame-step, speed control), colour-mode selector, legend, inspector panel (Plan §1, §18). *(`Views/MainView.axaml` control deck — Play/Pause/Step/New, speed slider, Save/Load, stream-connect, edit-energy + status — bound to `MainViewModel` commands; the colour-mode selector, legend, and inspector are the Phase 13 `WorldView`. Chrome follows the Fluent house rules; the map is the exception.)*
- [x] **Desktop target:** embed `LifeSim.Core` in-process, run the engine on a background thread, render live; verify on Windows and Linux (Plan §1). *(`Engine/EngineRunner.cs` runs `SimulationWorld` on a background thread with play/pause/step/speed and pushes snapshots through a UI-marshal delegate — the desktop head wires `Dispatcher.UIThread.Post` and auto-starts. ⚠️ Windows/Linux **visual** run not verifiable in this headless environment — build-verified only.)*
- [x] **Browser target (WASM) — constrained demo:** load snapshots from file and from the `sim serve` stream and render them; do not advance canonical time in stream mode (Plan §1, §9). *(`Engine/SnapshotStreamClient.cs` polls `GET /snapshot`; `MainViewModel.ConnectStream` enters `SessionMode.Streaming` with **no local engine advance**. File load via the picked file's stream (no filesystem assumption). ⚠️ actual in-browser run/bundle not verifiable here.)*
- [x] **Browser target — optional small-world live mode:** embed the Core via WASM to tick *small* worlds with the same engine code, degrading gracefully against the browser's scale/threading/memory limits (Plan §1, §9). *(Browser head is `liveEngine: true, autoStart: false` — the same `EngineRunner`/Core, seeded with a small 48×48 world; the user opts in via Play rather than it running on load.)*
- [x] Implement editing on both targets that appends explicit `edit_log` entries and (browser stream mode) posts edited snapshots back (Plan §16). *(New typed `Snapshot/EditLogEntry.cs` + `Editing/SnapshotEditor.cs`; `MainViewModel.EditSelectedEnergy` records the entry and either adopts the edit as a new deterministic start (live) or `POST`s it back (streaming). `SimulationWorld` now carries the edit log across resume so provenance never silently drops.)*
- [x] Implement snapshot save/load and world exchange with the console app using the shared format (Plan §1, §12). *(`MainViewModel.CurrentJson`/`LoadFromJson` + `SaveTo`/`LoadFrom`; `SessionTests.Session_exchangesWorldsWithTheConsoleApp_inBothDirections` proves a `sim new` world loads in the app and an app save advances via `sim run`.)*
- [x] Highlight a selected organism's `env_radius`/`org_radius` footprint (Plan §18, §17). *(Delivered in Phase 13 — `WorldScene.SelectedFootprint` + `SimulationCanvas` dashed env/org rings.)*
- [~] Confirm the WASM bundle size, startup, and framerate meet the Phase 0 spike's accepted demo thresholds; large worlds are viewed via streaming, not simulated in-browser. *(Streaming-not-simulating is enforced in code (streaming mode never advances locally). ⚠️ The bundle/startup/framerate thresholds still require a **manual browser run** — the same decision gate left open since Phase 0; cannot be validated headlessly.)*

**Exit criteria:** One codebase builds and runs as a native desktop app and a constrained browser demo, sharing the Phase 13 views with no target-specific view code (only the head wiring differs). Verified: both heads build (Debug & Release); `EngineRunner` play/pause/step, the `SnapshotStreamClient` against a real `sim serve` (fetch + edited write-back), the live session, editing→edit-log, save/load, and bidirectional console world-exchange are all covered by `LifeSim.App.Tests` (25 tests). 211 tests total pass; `dotnet format --verify-no-changes` clean. ⚠️ **Open (headless-environment limits, unchanged from Phase 0):** actual desktop window rendering, Windows/Linux visual parity, and the WASM bundle/startup/framerate thresholds need manual runs on a display / in a browser.

**Judgment calls worth flagging:**
- **`MainViewModel` takes a `(liveEngine, autoStart, post)` triple** so it's fully headless-testable: the heads inject `Dispatcher.UIThread.Post`; tests inject a synchronous delegate. The desktop/browser difference is *only* this wiring in `App.axaml.cs` — the views are identical.
- **`SimulationWorld` now persists the `edit_log`** across `FromSnapshot`/`ToSnapshot`. Without it, the runner's first frame after adopting an edit (`world.ToSnapshot()`) would drop the provenance. `branch_id`/`parent_snapshot_id` and the edited-then-resumed replay test are Phase 15.
- **v1 editing exposes one field (organism energy)** end-to-end as the representative §16 intervention; `SnapshotEditor` is the extension point for more fields. Config/world-field edits are deferred.
- **Save/Load use the picked file's *stream*, not a path**, so the same code works on desktop and in the browser (WASM has no local filesystem).
- **The engine runner marshals via an injected delegate, not a hard `Dispatcher` dependency** — keeps `LifeSim.App` testable without an Avalonia app instance and the Core UI-free.
- **Rendering/threading correctness is verified by build + logic/integration tests, not pixels or a live window** (headless env), consistent with the Phase 0 render-spike gate that remains manual.

---

## Phase 15 — Editing, Branching & Provenance
**Goal:** Make interventions explicit, replayable, and comparable (Plan §16).
**Depends on:** Phase 14.

- [x] Ensure every UI edit (either Avalonia target) appends a structured `edit_log` entry (Plan §16). *(Phase 14's `Snapshot/EditLogEntry.cs` + `Editing/SnapshotEditor.cs`; `MainViewModel.EditSelectedEnergy` records the entry on every intervention, and `SimulationWorld` carries the edit log across resume.)*
- [x] Treat an imported edited snapshot as a new deterministic starting point (full state + PRNG streams captured) (Plan §16). *(`SimulationWorld.FromSnapshot` restores organisms, PRNG streams, bookkeeping, events, edit log, and provenance; `EditedReplayTests.EditedSnapshot_replaysByteIdenticallyFromThePointOfEdit` proves it.)*
- [x] Implement `branch_id` / `parent_snapshot_id` so interventions form comparable timelines without overwriting the original run (Plan §16). *(New optional `snapshot_id` / `parent_snapshot_id` / `branch_id` snapshot fields (schema + carried by `SimulationWorld`); `Editing/SnapshotProvenance.cs` (`Root`/`Branch`) forks a child that records the parent's snapshot id. `MainViewModel` roots each new world and forks a fresh branch on every edit. Ids are set only by explicit UI actions — the engine never mints them — so genesis/flagship runs stay byte-identical (`SnapshotProvenanceTests.Genesis_hasNoProvenanceIds_*`).)*
- [x] Add a test: edited-then-resumed run is itself deterministically replayable (Plan §16). *(`Determinism.Tests/EditedReplayTests.cs` — byte-identical replay from an edited+branched snapshot, provenance + intervention carried through the resumed run, and a check that the edit actually diverges the trajectory from the unedited run.)*

**Exit criteria:** An edited world replays deterministically from the point of edit ✅ and branches are traceable to their parent snapshot ✅ (`EditedReplayTests`, `SnapshotProvenanceTests`, `SessionTests.Session_edit_forksATraceableBranchWithoutOverwritingTheOriginal`). 154 Core + 14 determinism + 26 app + 16 console + 9 calibration = 219 tests pass (Debug & Release); `dotnet format --verify-no-changes` clean.

**Judgment calls worth flagging:**
- **Provenance ids are UI-supplied, never engine-minted** — the deterministic loop only carries `branch_id`/`parent_snapshot_id`/`snapshot_id` through `FromSnapshot`/`ToSnapshot`. This is what lets an untouched genesis run stay byte-identical (ids stay null and are omitted from JSON) while edited branches remain fully traceable.
- **Each edit forks a new branch** (rather than mutating in place on the current branch) — the simplest reading of "form comparable timelines without overwriting the original run." A parent chain (`root → branch → branch…`) is reconstructable from `parent_snapshot_id`.
- **The app uses GUID-based ids** (`branch-…`/`snap-…`); tests pin fixed ids for determinism. Ids are provenance metadata, so app-side `Guid.NewGuid()` never touches the Core's deterministic contract.

---

# Post-v1 Extension Phases

These extend the model beyond the v1 blueprint (`lifesim.md` §1–§19, which explicitly scoped out things like sexual reproduction). They are **two completely independent phases** — neither depends on the other, and either can be built without the other. Each should get its own authoritative `lifesim.md` section before implementation (**§20 Cooperation**, **§21 Multicellularity**), mirroring how every existing phase points at a spec section. Both must uphold the Cross-Cutting Definition of Done below (determinism contract, named PRNG streams, sorted resolution, schema+round-trip for new blocks, config-in-block, GUI-from-state-with-legend).

---

## Phase 16 — Cooperation (Kin Selection & Mutualism)
**Goal:** Give unicellular organisms the *mechanisms* and *information* to evolve cooperative behaviour — kin recognition, energy sharing, and restraint from cannibalising relatives — so that cooperation can emerge under the population viscosity the engine already produces (adjacent-tile birth + inherited `lineage_id` cluster kin, and asexual cloning makes intra-cluster relatedness ≈ 1). This is **social behaviour between separate individuals**; it does not change the level of selection.
**Depends on:** Phases 5–10 (NEAT brain, sensing, interactions, metrics). **Independent of Phase 17.**
**Spec section:** §20 (written as source of truth).

- [x] **Kin-recognition sensory input (Plan §13, §20):** `SensoryField.ClosestOrganismRelatedness` — genome relatedness (0–1) to the closest organism, via the pure `Organisms/Kinship.cs` (`1 − mean normalized trait distance`). `NeatTopology.InputCount` 17 → 18; genesis wiring re-baselines. Read in the Sensing phase; no new randomness.
- [x] **Energy-share actions (Plan §5, §11, §20):** `OrganismAction.ShareNorth/South/East/West` (`NeatTopology.OutputCount` 11 → 15); `SimulationWorld.ResolveShare` transfers `share_fraction` of the donor's energy, credited at `share_efficiency < 1`, resolving in ascending-id order (off-grid/empty/unborn-offspring targets → no-op).
- [x] **Relatedness-scaled sharing (balance follow-up, §20):** a chosen Share actually donates with probability `share_probability_floor + (ceiling − floor) · relatedness`, rolled against the behaviour stream — close kin are helped usually (not certainly), strangers rarely (but possibly). Still deterministic/replayable.
- [x] **Density-dependent crowding (balance follow-up, §3/§6):** `Metabolism.CrowdingTax` charges `crowding_cost_per_neighbour` per 3×3 neighbour beyond `crowding_free_neighbours` each tick — a soft carrying capacity so cooperation-driven growth is self-limiting without harming sparse populations.
- [x] **Kin-safe interaction:** the relatedness input lets brains evolve to withhold predation from kin; optional `kin_predation_penalty` (default 0) charges an attacker extra for killing kin, and kin kills are counted.
- [~] **Cooperation-requiring pressure (optional, advanced):** deliberately deferred — starting with kin selection + viscosity only, per the §20 plan; add later only if cooperation doesn't emerge.
- [x] **Config (Plan Appendix A):** `CooperationConfig` block — `share_fraction` (0.25), `share_efficiency` (0.8), `share_probability_floor` (0.05), `share_probability_ceiling` (0.9), `kin_relatedness_threshold` (0.9), `kin_predation_penalty` (0, off) — plus `MetabolismConfig.crowding_cost_per_neighbour` (0.5) / `crowding_free_neighbours` (1), in the versioned configuration.
- [x] **Metrics (Plan §14):** `EnergyShared`, `SuccessfulShare`, `FailedShare`, `KinPredation` on `SimulationMetrics` + CSV columns. (Mean-local-relatedness omitted to avoid a per-tick O(n·neighbours) reduction — a judgment call.)
- [x] **UI (Plan §18):** Share entry in the action-outline palette + legend; a **Cooperation** colour mode (teal when the last action shared, else neutral) with its own legend section; the inspector's softmax now includes the four Share outputs. Every new colour has a legend entry.
- [x] **Verification (Plan §15):** `KinshipTests`, a sensory test that relatedness tracks genome similarity, and `Advance_organismsShareEnergy_overALongAbundantRun` (sharing is used and transfers energy once kin clusters form). Both flagship determinism tests + the eventful-determinism test stay green with sharing live, and all Phase 12 calibration scenarios still pass with cooperation in the default action set.

**Exit criteria:** Organisms sense relatedness and transfer energy ✅; sharing is used, costly, and relatedness-scaled ✅; overpopulation is self-limiting via the crowding cost ✅; determinism holds and calibration still passes with cooperation live ✅; cooperation is visible in metrics and the UI ✅. 259 tests pass (Core 165 / Console 16 / Determinism 14 / App 55 / Calibration 9); `dotnet format --verify-no-changes` clean.

**Balance / options follow-up (evolvable generosity, toggles, senescence):**
- [x] **Generosity is an evolvable genome trait (§20):** `Genome.ShareFraction` ∈ `[0,1]` — a Share donates the donor's *own* evolved fraction, so lineages can drift toward hoarding (→0) or over-sharing (→1). Founders seed from `cooperation.share_fraction` (now only the genesis value); the trait mutates/inherits like any other and its population mean is a new metric + histogram (`avg_share_fraction`). Shown as "Generosity" in the inspector and lineage detail. Schema/config bumped to 1.1 (new required `share_fraction` genome field).
- [x] **Cooperation toggle (§20):** `cooperation.enabled` (default on), selectable per world at genesis — off makes Share actions inert and skips the kin-predation penalty. Exposed as a setup-screen checkbox.
- [x] **Senescence toggle + aging model (§17):** the previously-inert `senescence` flag now drives a real cost — `Metabolism.SenescenceTax(age)` adds `senescence_cost_per_tick` per tick beyond `senescence_onset_age`, so no lineage is immortal. Deterministic (age-based, no PRNG). Exposed as a setup-screen checkbox (default on).
- [x] **Verification:** `SenescenceTax`/`CrowdingTax` unit tests; genesis seeds founder generosity; share amount is genome-driven (selfish pop transfers 0, generous pop transfers energy, mutation frozen); cooperation-off transfers nothing; senescence caps lifespan vs an ageless world. Determinism (14) + calibration (9) still green — the trait-diversity calibration check was made robust (whole-genome diversity, not one brittle trait) since adding a genome trait shifts every seed's PRNG trajectory.

**Judgment calls worth flagging:**
- **Directional shares add 4 outputs** (brain re-baselined 11 → 15, determinism snapshots regenerated) — chosen for parity with Move/Harvest over a single "donate to a chosen neighbour" output that would need a target-selection scheme.
- **Relatedness is phenotype-matching (genome distance), not lineage-id** — self-contained (no lineage plumbing into per-organism sensing) and apt for an asexual world where clones are near-identical.
- **Cooperation ships enabled in the default config** and the Phase 12 calibration seeds still pass, so no re-tuning was needed; the `share_efficiency < 1` loss keeps random early sharing from being a free lunch.
- **Sharing is stochastic but deterministic** — the relatedness gate rolls the existing behaviour stream (no new PRNG stream), so replay/save-reload stay byte-identical.
- **Crowding cost tuned to free 1 / cost 0.5** — empirically bounds a starting pop of 80 to a ~110 ceiling across seeds 1/7/42/909090 without driving extinction; it reuses the local-density count the plague check already computes, so no extra per-tick scan.
- The optional group-only resource was **not** built (deferred), so cooperation here is purely kin-selection + viscosity driven.

---

## Phase 17 — Multicellular Individuals
**Goal:** Introduce a *new level of individuality* (a major evolutionary transition): organisms are **bodies of many differentiated cells** grown from a genome, with a germline/soma split, selected and reproduced as whole individuals. Intra-body coordination here is **structural** (a shared energy pool + a germline that alone reproduces), not evolved social behaviour — which is exactly why this phase is **independent of Phase 16**.
**Depends on:** Phases 2–10 (world, organism/genome, brain, tick loop, metrics). **Independent of Phase 16.**
**New spec section:** §21 (write first, as source of truth). This is effectively a mini-project layered on the cell model; the sub-tasks below are ordered.

- [ ] **Body/cell spatial model (Plan §2, §10):** generalize the one-organism-per-tile grid to one-*cell*-per-tile, where a **body** is a connected group of cells with a stable `body_id`. Define body growth, movement, and the spatial-conflict rules for multi-tile bodies.
- [ ] **Developmental genome (genotype → phenotype):** a deterministic development program (a gene-regulatory network / CPPN / L-system, separate from or layered on the NEAT brain) that grows a body from a single germ cell and assigns each cell a **type** from position/signal — deterministic so replay holds.
- [ ] **Cell types & division of labour (Plan §3, §5):** e.g. *feeder* (harvests into the shared pool), *defender* (raises combat/thermal resilience), *germ* (accumulates for reproduction). Per-type traits/costs live in config.
- [ ] **Germline / soma split (Plan §8):** only germ cells reproduce; somatic cells are sterile but support the body. This is the transition's defining feature and its stability mechanism against cheater cells — enforce that reproduction spawns a new body from a germ cell's (mutated) genome + developmental program.
- [ ] **Body-level energy economy (Plan §3, §11):** a shared per-body energy pool; per-cell metabolism plus a **coordination/size cost** that scales with body size (caps runaway growth); a body dies when its pool empties or it loses structural integrity, with §11-style corpse deposition per cell.
- [ ] **Body-level control (Plan §4):** decide the controller model — a single body-level NEAT brain acting on the whole body (aggregate senses → body actions) vs. per-cell brains with a coordination signal. Persist per-node state; keep evaluation deterministic (single synchronous update, §4).
- [ ] **Body reproduction & lineage (Plan §8, §14):** germ-cell budding of a new adjacent body when the pool is sufficient and off cooldown; lineage records track *bodies* (birth/death, generation depth, cell-composition summaries).
- [ ] **Snapshot schema (Plan §12):** represent bodies (body_id, member cells + types, shared energy, developmental genome, brain, lineage) in the JSON Schema; full round-trip + save/reload equivalence. Innovation/id counters for the developmental genome mirror the NEAT bookkeeping.
- [ ] **Determinism (Plan §9):** development, per-cell metabolism reductions (fixed-order, sorted by body id then cell coordinate), and body reproduction all resolve deterministically; new randomness (developmental mutation) draws from the mutation stream.
- [ ] **Metrics (Plan §14):** body count, cells-per-body distribution, cell-type ratios, germline-vs-soma fraction, body lifespans and sizes.
- [ ] **UI (Plan §18):** render bodies (cells coloured by type, body outline); a body inspector (cell composition, developmental genome, shared energy, germ readiness); lineage of bodies; legend entries for cell types.
- [ ] **Calibration (Plan §15):** multicellular bodies persist without runaway or immediate extinction; a viable division of labour appears (or is at least stable); the germline/soma split holds (soma never reproduces); both flagship determinism tests stay green.

**Exit criteria:** Bodies made of differentiated cells develop from a genome, sustain themselves via a shared economy, reproduce via a germline, and are selected as whole individuals; the whole thing round-trips through the snapshot and replays deterministically; bodies are inspectable and rendered.

**Key design decisions to settle in §21:**
- **Controller model** (body-level brain vs. per-cell brains + coordination) is the biggest fork — it shapes sensing, actions, and the whole tick's per-body loop.
- **Development representation** (GRN vs. CPPN vs. L-system) and how mutation acts on it (this is a second evolvable genome alongside/instead of the NEAT brain).
- **How a body occupies space** (rigid multi-tile shape vs. a flexible cell cluster) and how it moves/grows without breaking the one-cell-per-tile invariant.
- **Backward compatibility:** whether unicellular organisms remain a degenerate case (a body of one germ cell) so the transition is continuous, or multicellularity is a separate world mode. A continuous encoding is more faithful to the real transition but harder to design.
- This phase is large enough that it may warrant splitting into ordered sub-phases (spatial+body model → development → economy → reproduction → UI) during implementation.

---

## Cross-Cutting / Definition of Done
These hold at every phase, not just one:

- [x] The two flagship determinism tests (Plan §15) stay green on every merge from Phase 4 onward — plus the events, recurrent-state, and edited-replay determinism tests layered on since.
- [x] Any new randomness draws from the correct named PRNG stream and never from wall-clock/ambient entropy (Plan §9) — genesis, behavior/combat, mutation, events, and sensory-noise streams; the App uses `Guid` only for non-simulation provenance ids.
- [x] Any new unordered iteration over simulation-sensitive collections is sorted with explicit tie-breaking (Plan §9) — organisms live in a `SortedDictionary`; metrics/mutation/birth reductions iterate ascending id.
- [x] New snapshot fields are added to the JSON Schema and covered by the save/reload test (Plan §12) — events, metrics, edit log, and branch provenance all extended the schema and round-trip.
- [x] New config constants live in the `configuration` block (Plan Appendix A), not in source — e.g. `plague_energy_drain_per_tick`, `temperature_variation`; Phase 12 tuned defaults in-block. (Exception: histogram bin count and marker-radius bounds are UI/analysis presentation constants, not simulation config.)
- [x] Anything rendered by a GUI is derived from snapshot/state fields only and has a corresponding legend entry (Plan §18) — the shared views render purely from `WorldSnapshot`; `LegendBuilder` covers every colour channel.

---

## Deferred (explicitly out of scope for v1)
Tracked here so they are not mistaken for gaps (Plan §4, §8, §9):
- Sexual reproduction & NEAT crossover (innovation ids are already recorded to enable it later) (Plan §4, §8).
- Fixed-point numeric migration (only if cross-platform replay drift appears) (Plan §9).
- Active name-collision avoidance via registry/Bloom filter (Plan §19).
