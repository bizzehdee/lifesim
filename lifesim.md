# Architectural Blueprint: Emergent Evolutionary ML Simulation

## 1. System Overview & Technology Stack

This simulation is built on a **shared-core, two-surface architecture**. A single deterministic C# **Simulation Core** holds all simulation logic and is decoupled from any graphics framework. Two surfaces sit on top of it: a headless **console app** for fast batch evolution, and a single **Avalonia app** that compiles to *both* native desktop (Windows/Linux) and the browser (WebAssembly). This keeps heavy numerical processing in one authoritative engine and the entire UI — visualization, inspector, editing — in one codebase shipped to both desktop and web.

        +-------------------------------------------------------------+
        |              C# SIMULATION CORE (class library)             |
        |  Deterministic engine: phased ticks, PRNG streams, NEAT     |
        |  brains, spatial lookups, energy economy, biomes, I/O       |
        +-------------------------------------------------------------+
              ^  references                     ^  references
              |                                 |
   +---------------------+        +--------------------------------------+
   |    Console App      |        |      Avalonia App (one codebase)     |
   |    (headless)       |        |                                      |
   | batch / CI / max    | <==JSON| Desktop (Win/Linux): embeds Core,    |
   | throughput; the     | snapshot| live run + render + inspect + edit  |
   | `sim` CLI + serve   |  bridge | Browser (WASM): renders streamed/    |
   +---------------------+ ======> | loaded snapshots; may embed Core     |
                                   | for small local worlds               |
                                   +--------------------------------------+

### The Simulation Core (C# Class Library)
*   **Role:** The single authoritative engine and heavy data-processing muscle. It has no UI dependency and performs no I/O beyond snapshot serialization.
*   **Characteristics:** Maximizes CPU core utilization to execute millions of environmental ticks and machine-learning updates. It owns spatial lookups, physics equations, the global energy economy, NEAT mutation, and the deterministic contract (§9). Every surface that advances simulation time does so *through this core* — there is exactly one implementation of the tick loop. It must stay trimming/AOT-friendly and free of desktop-only APIs so it can also run under the Avalonia browser target's WebAssembly runtime.

### The Console App (Headless)
*   **Role:** Fast, uninterrupted evolution for batch experiments, calibration scenarios (§15), and CI.
*   **Characteristics:** References the Core directly and runs with no rendering. It is the `sim` CLI (see Transport Protocol) and the tool used by the determinism regression tests.

### The Avalonia App (Desktop + Browser)
*   **Role:** The single UI codebase and manual manipulator, built once and shipped to two targets — native desktop for Windows and Linux, and the browser via WebAssembly.
*   **Shared UI:** Both targets present the identical visualization language, colour key, control deck, organism inspector, and NEAT-graph view (§18) — because they are literally the same C# views. Terrain is reconstructed from the world seed using the Core's own Simplex implementation on both targets; there is no separate JavaScript reimplementation to keep in sync.
*   **Desktop target:** References the Core in-process and runs the engine on a background thread, rendering directly from live engine state. It offers the full control deck (pause, frame-step, speed control), inspector, and in-place editing, and is the authoritative engine while running.
*   **Browser target (WASM) — a constrained demo surface:** The browser build is intended as a **lightweight, constrained demo**, not a peer of the desktop app. It defaults to rendering snapshots — from exported files or a live stream served by the console app (`sim serve`) — and **does not advance canonical simulation time in this mode** (§9). It may embed the Core via WebAssembly to tick *small* worlds locally using the exact same engine code, but three browser constraints cap its ambitions and it should degrade gracefully against all three:
    *   **Scale:** organism counts and world sizes are far lower than desktop; large runs are viewed via streaming, never simulated in-browser.
    *   **Bundle/startup:** shipping the .NET+Avalonia WASM runtime is a multi-MB download; mitigate with trimming/AOT/compression and treat cold start as a demo cost.
    *   **Threading & memory:** WASM threading is limited (SharedArrayBuffer + COOP/COEP headers) and memory is bounded, so the background-thread live engine model of the desktop target is restricted here.

### The Bridge: Universal JSON Schema & Shared Terrain Math
*   **The Data Bridge:** A standardized JSON text file containing the exact state of the world at a specific tick. It logs global variables, the initial generator seed, current environmental event modifiers, and a comprehensive flat array of organisms (including their spatial coordinates, current metabolic reserves, genome values, and historical brain topologies).
*   **The Deterministic Terrain Bridge:** Terrain is never written to disk; it is reconstructed on demand from the world seed via the Core's 2D Simplex Noise. Because the Avalonia browser target runs that same C# implementation under WebAssembly, there is no second (JavaScript) noise implementation to keep in sync — the previous cross-language parity risk is eliminated by construction. The PRNG that drives the tick loop lives only in the C# Core and its full state is serialized into every snapshot, so runs remain replayable and all simulation-critical randomness stays confined to one engine.

### The Transport Protocol
The Core is driven headlessly through the console app's `sim` CLI over snapshot files:
*   `sim run --in state.json --out state.json --ticks N` advances the world N ticks and writes the resulting snapshot.
*   `sim run --in state.json --out-dir ./frames --ticks N --stream K` additionally writes a snapshot every K ticks into a directory a UI can poll to render progress.
*   `sim serve --in state.json --port P` runs the engine and exposes snapshots over a local HTTP/WebSocket endpoint so the Avalonia browser demo can stream a live run and post edits back.

The Avalonia browser target reads snapshots (from files or the `serve` endpoint) to render/inspect and writes edited snapshots back — the primary way it shows large worlds, given its scale constraints. The Avalonia desktop target skips the file bridge for local play — it embeds the Core and reads live state directly — but both targets use the same snapshot format for save/load and for exchanging worlds with the console app.

---

## 2. The Deterministic Environment & Biome Engine

The environment is a continuous 2D tile-grid that acts as the primary selective pressure on the organisms. No terrain layout data is written to disk when saving; instead, the world maps are entirely implicit, constructed on-the-fly by seeding noise layers from the Core's single Simplex implementation (which runs identically on every surface, including the Avalonia browser target under WebAssembly).

### Procedural Generation (Seeded Maps)
Using a shared random seed, the environment engine overlays two distinct noise distributions for every grid coordinate: **Moisture Noise** and **Temperature Noise**. These continuous gradients intersect to establish sharp geographic boundaries across four structural biomes:

| Moisture (Low → High) \ Temp (Cold → Hot) | Cold | Temperate | Hot |
| :--- | :--- | :--- | :--- |
| **Low Moisture** | **Ice Sheet** | **Grassland** | **Desert** |
| **High Moisture** | **Ice Sheet** | **Swamp** | **Swamp** |

### Biome Characteristics & Mechanics
1.  **Grassland:** The stabilizing nursery. Features a baseline temperature comfortable for unmutated organisms. It possesses minimal movement friction and yields a highly stable, uniform ambient energy regeneration rate.
2.  **Desert:** The punishing void. Possesses an elevated regional temperature that induces severe metabolic spikes in organisms lacking heat tolerance. Movement friction is low, but ambient energy regeneration is profoundly sparse.
3.  **Swamp:** The high-stakes bog. Ambient energy regeneration is incredibly dense and continuous, but the tile physics injects a massive movement friction penalty, requiring vast energy expenditure to traverse.
4.  **Ice Sheet:** The frozen wall. Regional temperatures are negative, severely draining agents lacking cold-specialized genomes. Ambient energy regeneration is zero; organisms must bring energy in or feed on others to survive.

### The Ground Energy Pool
Every tile acts as an energy buffer. Based on its biome, a tile regenerates a slow trickle of **Ambient Energy** every turn up to a hard ceiling. This represents localized plant, moss, or algae growth, serving as the foundational energy baseline for the entire food chain.

---

## 3. Organism Physiology & The Inheritable Genome

An organism is a dynamic state machine operating inside a physical grid slot. Its biological behavior, survival constraints, and physical limitations are explicitly governed by its **Genome**—a mutable sequence of values passed down through replication.

### Inherited Structural Traits
*   **Size (Mass):** Dictates physical volume. Large size increases an organism's success multiplier in physical combat, but introduces a heavy baseline metabolic tax and significantly amplifies the energy required to construct an offspring.
*   **Speed Capacity:** Determines the maximum number of tile coordinates an agent can cross within a single simulation tick. Evolving a high speed capability demands an elevated metabolic idle cost and a massive energy drain during locomotion.
*   **Thermal Envelope:** Defines an organism's ideal temperature sweet spot. If the current tile temperature deviates from this range, an environmental stress multiplier scales up, burning away internal energy reserves.
*   **Sensory Range Vector (`env_radius`, `org_radius`):** Dictates how many tiles away from its center point an organism can sense static tile fields versus moving entities.
*   **Sensory Acuity:** Governs the signal-to-noise ratio of incoming data. A high value provides pristine telemetry; a low value heavily degrades input signals with random Gaussian fluctuations.

### The Energy Transaction Economy
Survival is determined strictly by an agent's internal energy budget (ranging from 0.0 to 100.0). Every action is an explicit transactional subtraction or addition. If the budget hits zero, the organism is removed from the global simulation index.

1.  **The Base Metabolism:** Calculated every single tick regardless of action. It is the cost of existing, defined as:
    $$\text{Metabolism} = (\text{Size} \times \text{Base}) + \text{Thermal Stress Penalty} + \text{Sensory Tax}$$
2.  **The Sensory Tax:** Perception is computationally expensive. To prevent organisms from evolving omniscient vision for free, sensing fields are taxed heavily. Tracking static terrain scales linearly, while tracking dynamic, moving agents scales quadratically:
    $$\text{Sensory Tax} = (\text{env\_radius} \times c_1) + (\text{org\_radius}^2 \times c_2) + (\text{sensory\_acuity} \times c_3)$$
3.  **The Locomotion Tax:** Moving across grid spaces burns energy relative to the square of the speed combined with the terrain resistance:
    $$\text{Movement Cost} = \text{Distance} \times \text{Velocity}^2 \times \text{Biome Friction Modifier}$$

---

## 4. The Non-Deterministic ML Brain (Stochastic NEAT)

Organisms are guided by an active, non-deterministic machine learning system. Instead of utilizing rigid, hard-coded decision matrices, behavior is processed via a neural network whose topological structure grows, adapts, and shifts entirely through evolution.

[ FIXED SENSORY INPUTS ]

+--------------------------------+
|  - Local Tile Temperature     |
|  - Internal Energy Level       |
|  - Richest Tile Vector         |
|  - Closest Organism Vector     |
|  - Global Stress Level Sensor  |
+--------------------------------+
|
v
[ DYNAMIC NEAT GRAPH ]
+--------------------------------+
|  Hidden nodes & synaptic links |
|  mutate and complexify over    |
|  generations.                  |
+--------------------------------+
|
v
[ STOCHASTIC OUTPUTS ]
+--------------------------------+
|  Softmax -> Action Probabilities|
|  (e.g., Move N: 70%, Eat: 30%) |
+--------------------------------+
|
v
[ PRNG WEIGHTED SELECTION ]

### Fixed Sensory Inputs & Blurred Telemetry
Because neural network node configurations cannot dynamically alter their input layer dimensions on-the-fly, organisms process their environment via a fixed array of telemetry vectors, regardless of their evolved sensory radius:
*   *Internal Telemetry:* Current energy budget, age.
*   *Environmental Proximity:* Distance and directional angle to the richest ambient energy tile inside their `env_radius`.
*   *Radar Telemetry:* Distance, directional angle, and relative size delta of the closest organism within their `org_radius`.
*   *Systemic Telemetry:* Global Stress Level Sensor (notifies the brain when a large-scale event or disaster is active).

The list above and the diagram are illustrative; **§13 defines the authoritative fixed input vector** that the implementation must use.

*The Blur Factor:* If an organism's `sensory_acuity` is low, the engine injects Gaussian noise directly into these input vectors before they reach the hidden layers, blinding or confusing the agent regarding a predator's true proximity.

### The Topological Brain Structure (NEAT)
The hidden layer layout is completely unconstrained. Rather than a standard fixed-layer network where only weight variables change, the structural connections mutate. Over generations, genomes can introduce:
*   *Connection Mutations:* Spawning a completely new synaptic link between previously unlinked node paths.
*   *Node Mutations:* Splitting an existing connection path to insert an entirely new hidden neuron, expanding the brain's internal processing capacity.

**Network Type (v1: Recurrent).** The network permits recurrent connections from the start (`network_type: "recurrent"` in the schema) so that organisms can evolve genuine internal memory — persistent hidden state that carries information across ticks, enabling learned behaviors like sustained migration headings, hysteresis in fight-or-flight, and multi-tick strategies. Cycle-creating connection mutations are allowed, not rejected.

To keep recurrence fully deterministic, evaluation uses a **single synchronous update per tick**: every node computes its new activation from the values its inputs held **at the end of the previous tick**, and all nodes commit their new values together. This sidesteps any node-ordering ambiguity — there is no "which node fires first" question because no node reads another node's *current*-tick output. The consequence is a one-tick propagation latency per hidden layer, which is itself an evolvable property of the topology.

**Persistent Node State.** Because activations carry across ticks, each node's last activation value **is** organism state. It is initialized to zero at birth, serialized per-node in the brain block of every snapshot, and restored on reload. This is required for the save/reload equivalence test (§15) to hold. Recurrence remains available as a mutation target throughout the run.

**Historical Markings (Innovation Numbers).** Every connection and node gene carries a globally unique **innovation ID** drawn from a monotonic counter. Even though v1 uses only asexual reproduction, these markers are recorded from day one so that future sexual reproduction / crossover (§8) can align homologous genes without rewriting every stored genome. The counter (`next_innovation_id`) is part of deterministic state and is serialized in the snapshot alongside the PRNG streams (see §9, §12).

**Activation Function.** Hidden and output nodes use `tanh` in v1 and are not per-node mutable. Each node gene still carries an `activation` field defaulting to `"tanh"` so per-node activation mutation can be enabled later without a schema change. Bounded `tanh` output composes cleanly with the softmax stage and the normalized input vector (§13).

### Non-Deterministic Softmax Inference
To ensure behavior is truly non-deterministic, the network's output node signals do not act as direct execution commands. 
1. The network generates a set of raw numerical values (logits) for each valid action: **Move North, Move South, Move East, Move West, Harvest-Self, Harvest-North, Harvest-South, Harvest-East, Harvest-West, Stay Idle,** and **Reproduce** (11 outputs). The directional harvest outputs let the brain explicitly aim each harvest: `Harvest-Self` grazes the organism's own tile, while `Harvest-<dir>` acts on the adjacent tile in that direction.
2. These logits are immediately processed through a **Softmax function**, translating the raw outputs into a strict probability distribution totaling 100%.
3. The backend engine runs a weighted roll using the deterministic PRNG against this distribution. If "Move North" has a 70% probability and "Harvest" has a 30% probability, the organism will *usually* move north, but will occasionally choose to harvest instead. This enables continuous exploration and behavioral branching.

---

## 5. Interaction Dynamics & Emergent Phenomena

When thousands of these stochastic, variable-sized agents are placed into the procedurally generated biomes, the system shifts from basic code loops into an interconnected, complex ecological web where major biological phenomena emerge naturally.

                    +----------------------------+
                    |  Universal Harvest Action  |
                    +----------------------------+
                                   |
               +-------------------+-------------------+
               |                                       |
               v                                       v
 [ TARGET: Empty/Tile ]                       [ TARGET: Live Agent ]
+--------------------------+                +----------------------------+
|    Ambient Grazing       |                |     Predatory Combat       |
| Drains energy from grass |                | P(kill)=Sa/(Sa+Sv), rolled |
|   (Emergent Herbivory)   |                |  (Emergent Carnivory)      |
+--------------------------+                +----------------------------+

### The Universal "Harvest" Interaction (Diet Emergence)
The source code contains no classifications for "predator" or "prey." It contains a single *kind* of physical action — `Harvest` — aimed at a target tile via the directional outputs of §4 (`Harvest-Self`, `Harvest-N/S/E/W`). There are no separate "graze" and "attack" code paths; the context of *what* occupies the targeted tile dictates the outcome:
*   **The Ambient Grazing Path (Herbivory):** If an agent executes `Harvest` on an empty tile, it drains the ground's accumulated energy pool. This resource is highly reliable but low-density.
*   **The Predatory Combat Path (Carnivory):** If an agent executes `Harvest` on a tile occupied by another organism, the engine triggers a **probabilistic** combat check. The kill probability is bounded and symmetric:
    $$P(\text{kill}) = \frac{\text{Size}_{\text{attacker}}}{\text{Size}_{\text{attacker}} + \text{Size}_{\text{victim}}}$$
    A weighted roll against the dedicated combat/behavior PRNG stream decides the outcome. This stays in $(0, 1)$ and avoids the blow-ups of a raw $\text{Size}_a / \text{Size}_v$ ratio. A successful check kills the target and transfers a massive percentage of its remaining metabolic budget to the attacker. Failure inflicts a physical energy penalty as retaliation damage.

### Evolutionary Speciation & System Dynamics
As a consequence of these structural loops, several key ecological paradigms will naturally solidify within the environment:

#### 1. The r/K Selection Reproduction Dynamic
Because the energy cost to produce an offspring scales directly with parent mass ($\text{Base Cost} \times \text{Size}_{\text{parent}}$), the reproduction system forces an organic divide:
*   **Small Prey (r-Strategists):** Small size makes them vulnerable to predators, but their children are incredibly cheap to manufacture. When grazing on a rich Swamp tile, a small agent can save up energy and reproduce on nearly every eligible tick (one offspring per tick, gated by `reproduction_cooldown_ticks`; see §17), spreading a rapid burst of cheap offspring across successive ticks to out-populate predator harvesting.
*   **Large Predators (K-Strategists):** To win combat checks, hunters must mutate a massive `Size` trait. This massively escalates their baseline reproduction cost. They are economically restricted from flooding the map; they must survive long periods, track prey over vast distances, and reproduce rarely, investing heavily in single, high-mass offspring.

#### 2. Sensory Specialization Profiles
The energy cost constraints force brains to adapt sensory configurations directly to their lifestyles and geographical biomes:
*   *Open Desert Hunters* will abandon environment vision entirely to avoid the tax, expanding their `org_radius` to its absolute ceiling to track the distant movement of scarce prey.
*   *Grassland Grazers* will contract their radar range to zero to run an ultra-low-cost metabolism, keeping only a tight, high-acuity 1-tile environmental look-down to maximize harvesting efficiency.

#### 3. Lotka-Volterra Population Waves
As the non-deterministic neural paths optimize, population charts will display cyclical waves. An explosion of r-strategist prey provides a vast energy field for predators. Predators consume this field, triggering a slow K-strategy population boom. The over-abundant predators decimate the prey population, triggering a catastrophic famine that crashes the predator numbers, reset-seeding the cycle to begin anew.

---

## 6. Environmental Stochasticity & Random Events

To prevent the ecosystem from stagnating into permanent, unyielding local maxima, the simulation engine injects unexpected external shocks and disasters. These simulate the chaos of real-world nature, testing population-level generalized resilience over hyper-specialization.

              +-----------------------------------+
              |      GLOBAL CLOCK TICK LOGIC      |
              +-----------------------------------+
                                |
            +-------------------+-------------------+
            | (Low Roll)                            | (High Roll)
            v                                       v
  [ Standard Physics ]                     [ STOCHASTIC EVENT ]
  
- Regular metabolism                    - Blight (Zero tile growth)

- Baseline biome friction               - Plague (Density-based drain)

- Linear food regeneration              - Temperature Anomaly (+/- 20C)

### Event Mechanics & State Mutation
Every global tick loop in the backend, the engine calculates an occurrence roll against the deterministic PRNG. If an event conditions roll passes, a temporary modifier is updated in the global `environment_modifiers` configuration block within the master JSON schema.

1.  **Density-Dependent Plagues:** The engine tracks crowded sub-regions. When active, this event imposes an energy depletion state over several turns affecting entities sharing tiles or immediate neighbors. This functions as a massive selective counter-weight against exponential, identical cloning clusters, giving an evolutionary edge to sparse or structurally diverse lineages (and, once sexual reproduction is added per §8, to genetically diverse ones).
2.  **Climatic Anomalies (Ice Ages / Heatwaves):** Temporarily shifts global temperature scales by a significant factor ($\pm20^\circ\text{C}$). This distorts standard biome lines, turning rich territories lethal overnight and triggering macro-migration flights across the map.
3.  **Resource Blights:** Completely halts ground ambient energy regeneration across entire targeted biomes for a fixed duration. This tests the metabolic buffering behaviors of agents, forcing a temporary survival spike in carnivorous actions.

### Evolutionary Consequences of Bad Luck
The addition of inevitable, random bad luck alters the baseline rules of evolutionary fitness:
*   **Metabolic Buffering:** Neural networks must learn to treat reproduction as a luxury. Brains that evolve to maintain a substantial energy safety reserve *before* attempting reproduction survive sudden famines, while greedy strains are eliminated.
*   **Phenotypic Plasticity:** Organisms leverage the `Global Stress Level Sensor` input. Highly adaptive networks utilize this indicator to transition their baseline hidden layer pathways into survival crisis scripts (e.g., executing defensive migrations or turning predatory) when macro disasters disrupt the world.

---

## 7. Simulation Tick Semantics

The simulation uses a **phased tick model**. This prevents earlier organisms in the update list from gaining unfair access to newer information and gives every organism a consistent view of the world for that tick.

### Authoritative Tick Order
Each global tick advances through the following phases:

1.  **Environment Phase:** Active event modifiers are aged, expired modifiers are removed, and any newly triggered stochastic events are queued using the deterministic PRNG.
2.  **Sensing Phase:** Every living organism receives a sensory snapshot based on the world state at the start of the tick. These inputs are cached and are not affected by other organisms' decisions during the same tick.
3.  **Decision Phase:** Every organism runs its NEAT brain against its cached inputs using a single synchronous update — nodes read their inputs' previous-tick values and commit new activations together (see §4). Persistent node state is updated here. The stochastic softmax roll then selects one intended action per organism. Because each brain evaluates only from its own cached inputs and its own prior state, the **forward pass is parallelized across up to `--threads` / GUI-selected threads** (1..hardware-threads): it is a pure function with no shared writes and no randomness, so it runs concurrently, and only then does the single softmax roll draw from the shared behaviour stream **strictly in ascending organism-id order**. Thread count is therefore an execution knob only — results are **byte-identical for any thread count** (a flagship determinism test pins threads 1 vs 2/4/8), and it is not part of the snapshot.
4.  **Intent Resolution Phase:** Movement, harvest, and reproduction intents are resolved using deterministic ordering and spatial conflict rules.
5.  **Metabolism Phase:** Base metabolism, sensory tax, movement tax, thermal stress, plague drains, and other global costs are applied.
6.  **Death & Transfer Phase:** Organisms at or below zero energy are removed. Any pending energy transfers, corpse deposits, or predation rewards are finalized.
7.  **Resource Regeneration Phase:** Ground energy pools regenerate according to biome rules, event modifiers, and tile caps.
8.  **Mutation & Birth Commit Phase:** Valid offspring are inserted into the organism index with inherited-then-mutated genomes and brain topologies (§8), fresh organism IDs, and node state initialized to zero (§4).
9.  **Metrics & Snapshot Phase:** Population counts, trait distributions, event logs, and optional state snapshots are recorded.

### Deterministic Ordering
All organism operations that require a stable order use ascending organism ID. If two intents conflict, the lower organism ID resolves first unless a specific mechanic explicitly defines a different priority. This rule exists for reproducibility, not biological realism.

---

## 8. Reproduction, Mutation & Lineage

The initial implementation uses **asexual reproduction with mutation**, while keeping the schema open for future sexual reproduction and crossover. This allows ecological pressure and mutation to work before adding mate selection complexity.

### Asexual Reproduction
An organism may reproduce when:
*   It selects the `Reproduce` action.
*   Its current energy is greater than or equal to `reproduction_base_cost * size`.
*   At least one adjacent tile is available under the spatial conflict rules.
*   It is off cooldown — at least `reproduction_cooldown_ticks` have elapsed since its last birth, and it has not already reproduced this tick (see §17).

When reproduction succeeds:
*   The parent pays the reproduction cost immediately.
*   The offspring receives an initial energy allocation from that cost.
*   The offspring is placed in a deterministic valid adjacent tile.
*   The offspring receives a copy of the parent's genome and NEAT brain, then mutation is applied.
*   The offspring records `parent_id`, `lineage_id`, `birth_tick`, and `generation_depth`.

### Mutation Rules
Mutation is applied independently to structural traits and brain topology.

*   **Trait Mutation:** Size, speed capacity, thermal envelope, sensory range, and sensory acuity may drift by small bounded deltas. All traits have hard minimum and maximum values.
*   **Weight Mutation:** Existing NEAT connection weights may be perturbed by deterministic PRNG rolls.
*   **Connection Mutation:** A new valid connection may be added between previously unconnected nodes.
*   **Node Mutation:** An existing connection may be split by inserting a new hidden node.

Mutation rates are stored in the global simulation configuration so experiments can tune them without changing source code.

### Evolution Model
The simulation uses **continuous open-ended survival** rather than discrete generations. Organisms reproduce, mutate, and die inside the world. Analytics may group organisms into birth cohorts or generation depths, but the engine does not pause to perform external selection.

---

## 9. Determinism Contract

The C# **Simulation Core** is the **authoritative simulation engine**, whether it runs headless in the console app, embedded in the Avalonia desktop target, or embedded via WebAssembly in the Avalonia browser demo. When the browser target is only rendering a streamed or loaded run — its normal mode for anything beyond a small world — it does not advance canonical simulation time; it visualizes snapshots and submits edits. Because every mode runs the *same* Core code, there is one implementation of the tick loop and no cross-runtime divergence.

### Deterministic Runtime Rules
*   The Core owns all tick advancement; no UI reimplements the tick loop.
*   The current PRNG state is serialized into every snapshot, not just the initial seed.
*   Separate PRNG streams are used for genesis/world initialization, behavior selection (including softmax action rolls and combat kill rolls), mutation, environmental events, and sensory noise. Every stream's state is serialized in `prng_streams`.
*   Organism IDs are stable and never reused.
*   All unordered collections must be sorted before simulation-sensitive iteration.
*   Tie-breaking must be explicit and deterministic.
*   Parallel processing is allowed only for phases that do not mutate shared state or depend on iteration order — in practice, per-organism-independent work such as sensory snapshotting and brain inference.
*   **Floating-point reductions** (cross-organism aggregations such as local density, energy sums, and metrics) run single-threaded in sorted organism-ID order regardless of threading. Sorting before iteration is not sufficient on its own, because parallel float summation is order-sensitive; the reduction order itself must be fixed.
*   The global innovation counter (`next_innovation_id`) is deterministic state: it only advances during the Mutation & Birth Commit phase, in sorted organism-ID order, and is serialized in every snapshot.
*   Organism names are a deterministic pure function of the stable `organism_id` and the pinned word-list version (§19); they consume no PRNG draw and reproduce identically on replay.

### Numeric Policy
The first implementation uses standard backend floating-point math with strict deterministic ordering. If cross-platform replay drift becomes a problem, simulation-critical calculations can be migrated to fixed-point values.

---

## 10. Spatial Conflict Rules

The first implementation uses a **one organism per tile** occupancy model. This keeps movement, harvesting, reproduction, and collision behavior easy to reason about while the evolutionary system is being validated.

### Occupancy Rules
*   A tile may contain at most one living organism.
*   A movement action into an empty tile succeeds if the organism can pay the movement cost.
*   A `Move` action into an occupied tile fails. To interact with an occupied adjacent tile an organism must instead select the matching directional `Harvest` action.
*   A reproduction action requires at least one valid adjacent empty tile.
*   If multiple offspring or movement intents target the same empty tile, intents resolve in deterministic organism ID order.

### Harvest Targeting
Harvest is expressed as five distinct action outputs (§4): `Harvest-Self` targets the organism's own tile, and `Harvest-North/South/East/West` target the adjacent tile in that cardinal direction. If the targeted tile contains ground energy and no organism, ambient grazing occurs. If it contains an organism, predatory combat occurs (§5). A directional harvest aimed off-grid or at an empty tile with no ground energy resolves as a no-op that still pays the tick's base costs.

---

## 11. Energy Accounting

The simulation uses a **semi-conserved energy economy**. Biome regeneration creates new ground energy, but organism-to-organism and organism-to-environment transfers are explicitly accounted for.

### Energy Sources
*   Ground energy regeneration creates energy up to each tile's biome-specific cap.
*   Successful grazing transfers energy from the tile pool to the organism.
*   Successful predation transfers a percentage of the victim's remaining energy to the attacker.
*   Energy gains (grazing and predation) are clamped to the 0.0–100.0 budget ceiling (§3); any surplus beyond the cap is lost, not banked. This keeps the ceiling a hard invariant.

### Energy Sinks
*   Base metabolism, sensory tax, thermal stress, movement cost, failed combat penalties, and plague drains remove energy from organisms.
*   Removed metabolic energy does not return to ground tiles in the initial model.
*   Failed reproduction attempts still pay no cost unless explicitly configured otherwise.

### Death and Corpses
When an organism dies from combat, a configured percentage of its remaining energy transfers to the attacker. When an organism dies from starvation, stress, or plague, a smaller configured percentage may be deposited into the tile as corpse energy, capped by the tile's maximum energy capacity.

### Reproduction Energy
Reproduction converts parent energy into offspring energy. The parent pays `reproduction_base_cost * size`, and a configured fraction becomes the offspring's starting energy. The remainder is treated as construction inefficiency and leaves the system.

---

## 12. Snapshot Schema & Versioning

Every exported state file must be self-describing, validated, and replayable by the backend.

### Required Snapshot Blocks
```json
{
  "schema_version": "1.0",
  "config_version": "1.0",
  "simulation_version": "0.1.0",
  "tick": 0,
  "world": {},
  "configuration": {},
  "prng_streams": {},
  "evolution_bookkeeping": { "next_innovation_id": 0, "next_organism_id": 0 },
  "environment_modifiers": [],
  "organisms": [],
  "lineages": [],
  "metrics": {},
  "edit_log": []
}
```

Each organism's brain is stored as a NEAT genome: a list of node genes (`{ id, type, activation, state }`, `activation` defaulting to `"tanh"`, `state` being the persistent activation carried across ticks and initialized to 0 at birth), a list of connection genes (`{ innovation_id, from, to, weight, enabled }`), and `network_type` (`"recurrent"` in v1). Node `state` is dynamic organism state and must round-trip through serialization.

### Schema Rules
*   `schema_version` uses semver and is required. Import **hard-rejects on major-version mismatch** with a clear error. Minor-version differences are allowed and may pass through forward-migration hooks; v1 ships no migrations and simply requires an exact match.
*   `config_version` is validated alongside `schema_version` under the same major/minor policy.
*   `simulation_version` records the engine version that created the file.
*   `tick` records the exact world time of the snapshot.
*   `prng_streams` stores the full state of every deterministic random stream.
*   `evolution_bookkeeping` stores the monotonic innovation and organism-ID counters so IDs are stable and never reused across a resume.
*   `organisms` stores only dynamic organism state and inherited genome data. Each record carries at minimum: `organism_id`, `name` (the display name — §19), position (`x`, `y`), `energy`, `age`, the full genome (traits + brain), `last_action` (the action selected on the previous tick, used for action-coloured rendering — §18), `last_action_result`, and `last_birth_tick` (so reproductive readiness and cooldown are reconstructable without replay).
*   Procedural terrain is reconstructed from world seed and noise configuration unless a debug snapshot explicitly includes cached tile data.
*   Imports must be validated against a JSON Schema file before simulation resumes.

---

## 13. Observation Inputs

The initial sensory model is expanded with aggregate local signals. This preserves a fixed-size neural input layer while giving organisms enough context to evolve more varied strategies.

### Fixed Input Vector
The brain receives:
*   Current internal energy.
*   Age.
*   Local tile temperature.
*   Local biome/friction value.
*   Distance and direction to the richest visible ground energy tile.
*   Distance, direction, and relative size delta of the closest visible organism.
*   Count of nearby smaller organisms inside `org_radius`.
*   Count of nearby larger organisms inside `org_radius`.
*   Local organism density.
*   Last action result encoded as a fixed numeric value.
*   Reproductive readiness.
*   Global stress level.

All sensory values are normalized into stable numeric ranges before entering the neural network. Sensory acuity controls Gaussian noise injection after normalization.

---

## 14. Metrics, Experiments & Analysis

The engine records analytics as first-class simulation output. The goal is not only to run an evolving world, but to show how traits and behaviors respond to environmental pressure.

### Core Metrics
The backend tracks:
*   Total population.
*   Births and deaths per tick window.
*   Average, minimum, and maximum energy.
*   Average trait values and trait distribution histograms.
*   Population by biome.
*   Successful grazing, failed grazing, successful predation, failed predation.
*   Reproduction rate by lineage.
*   Extinction events.
*   Active environmental events.

### Lineage Tracking
Every organism records enough ancestry to reconstruct lineage trees:
*   `organism_id`
*   `parent_id`
*   `lineage_id`
*   `birth_tick`
*   `death_tick`
*   `generation_depth`
*   summary genome traits at birth and death

### Export Formats
Snapshots use JSON for replay and inspection. Metrics may also be exported as CSV or newline-delimited JSON for plotting and batch analysis.

---

## 15. Calibration Scenarios

Because the system contains many coupled constants, the project includes fixed-seed scenario tests. These tests are not expected to prove that evolution is "correct," but they should catch obvious runaway dynamics and broken mechanics.

### Determinism Regression (Flagship Tests)
These guard the guarantee the entire architecture rests on and run before any calibration scenario:
1.  **Seed Replay:** Running the same seed for N ticks twice must produce **byte-identical** final snapshots.
2.  **Save/Reload Equivalence:** Running 100 ticks straight must produce a snapshot identical to running 50 ticks, serializing, reloading, and running 50 more. This proves the serialized PRNG streams and `evolution_bookkeeping` fully capture simulation state.

### Baseline Scenarios
1.  **Grassland Survival:** A small population in stable grassland should survive for a minimum tick count without immediate extinction.
2.  **Desert Stress:** Heat-intolerant organisms should experience measurable energy pressure in desert tiles.
3.  **Swamp Movement Cost:** Swamp populations should gain access to abundant ground energy while paying visibly higher movement costs.
4.  **Predator/Prey Transfer:** Larger organisms should be able to gain energy through successful predation, but not without meaningful risk or cost.
5.  **Overcrowding Plague:** Dense cloned clusters should be penalized during plague events.
6.  **Blight Recovery:** Populations with energy buffers or flexible behavior should recover more often than greedy populations after temporary resource collapse.

### Calibration Goals
The default configuration should avoid:
*   extinction within the first few ticks,
*   infinite population growth without resource pressure,
*   one trait dominating every biome,
*   reproduction loops that ignore environmental constraints,
*   predators becoming viable without prey density or tracking cost.

---

## 16. UI Edit Log & Branching

Interactive edits from the Avalonia app (either its desktop or browser target) are allowed, but they must be explicit interventions. This keeps experiments understandable and prevents manually changed worlds from being mistaken for untouched deterministic runs.

### Edit Log
Every UI modification appends an entry to `edit_log`:
*   tick of intervention,
*   edited entity or world field,
*   previous value,
*   new value,
*   user-facing reason or label when provided.

### Determinism After Edits
After an edit, the backend treats the imported state as a new deterministic starting point. The snapshot remains replayable from that point because it includes full organism state, configuration, and PRNG stream state.

### Branch Identifiers
Snapshots may include a `branch_id` and `parent_snapshot_id`. These fields allow manual interventions to form comparable timelines without overwriting the original experimental run.

---

## 17. Lifecycle & Movement Rules (Resolved Decisions)

These fill gaps that would otherwise be discovered mid-implementation. All are v1 commitments with explicit config hooks where they may later be tuned.

### Genesis (Tick 0)
The initial world is built from a config-driven `initial_population` (default ~200 organisms), scattered across Grassland tiles using a dedicated genesis PRNG stream. Each starts with mid-range genome traits and a **minimal fully-connected brain** (inputs wired directly to outputs with random weights, no hidden nodes). Minimal starting topology is canonical NEAT — structure grows only under real selective pressure rather than starting bloated.

### Full Extinction
When population reaches zero the engine **halts and flags** (`metrics.extinct = true`), stops advancing time, and writes a final snapshot. It does **not** auto-reseed — silent reseeding would destroy experiment comparability. Reseeding is an explicit CLI/UI action, logged in `edit_log`.

### Multi-Tile Movement
Speed capacity lets an organism cross several tiles per tick, but the one-organism-per-tile invariant (§10) is preserved by **path-checking intermediate tiles**. A move advances tile-by-tile toward its destination and stops at the first blocked or occupied tile; movement cost is paid only for the distance actually traveled. There is no teleporting past occupants and no overlap. Competing destination claims resolve in ascending organism-ID order.

### Sensing vs. Action Space (Intentional Asymmetry)
Sensing is **ranged** (`env_radius` / `org_radius` can span many tiles) while the action space is deliberately **local**: `Move N/S/E/W` (cardinal only, no diagonals in v1), `Harvest-Self`, `Harvest-N/S/E/W`, `Stay Idle`, `Reproduce` (11 outputs; see §4). The gap between "sees far, acts near" is what forces pursuit and navigation to evolve rather than being free. This asymmetry is a design choice, not an oversight.

### Age & Senescence
The `senescence` config knob (default **on**, selectable per world at genesis) applies a simple aging model: past `senescence_onset_age` ticks an organism pays a metabolic tax that grows linearly with age (`senescence_cost_per_tick` per tick beyond the onset), so old individuals eventually starve however much they hoard and no lineage is immortal. The tax is a deterministic function of `age` — no new randomness — and layers onto the metabolism phase (§3) alongside the crowding cost. Disable it to fall back to the alternative regime: no hard lifespan, with turnover left entirely to the famine/predation cycles (§5.3).

### Reproduction Cadence
An organism may produce **at most one offspring per tick**, and is gated by `reproduction_cooldown_ticks` (default 3) between births. This prevents a degenerate single-tick "flood-fill" while still allowing r-strategist bursts spread across consecutive ticks — the §5 narrative holds, just spread over time.

### Corpses
There is no distinct corpse entity. Corpse energy folds into the tile's ground pool as described in §11, capped by tile capacity. Scavenging is therefore **emergent** — an organism grazing a corpse-boosted tile — not a separate mechanic. This keeps the "one universal Harvest action" (§5) intact.

---

## 18. GUI Visualization & Inspection

The Avalonia app renders the world identically on both its targets (§1) — the visual language, colour key, and inspector are one shared set of C# views, so a world looks the same on desktop and in the browser demo. The desktop target (and small-world browser mode) render directly from live in-process engine state; the browser demo normally renders from snapshots (file or `serve` stream), subject to its scale constraints. All colour mappings are derived purely from the state fields defined in §12, so a static snapshot renders identically to a live frame — the UI never invents state. The shared scene is a legible, colour-coded map with an always-visible key, visibly distinct biomes, and a click-through inspector exposing every stat behind an organism.

**In-app event notifications.** The app surfaces notable transitions as transient in-window toast cards (a `WindowNotificationManager`, not native OS notifications). The `EventWatcher` flags: environmental events starting and ending (blight / plague / climatic anomaly); population milestones (explosion, crash, near-extinction, extinction, new record high); flow bursts (baby boom, mass die-off) and widespread starvation; evolutionary firsts (first cooperation, first kin predation, first sterile soma, each new whole-cell-count body §21, each 10th generation reached); ecological shifts (colonising a hostile biome, a swing between predatory and herbivore eras). Detection is UI-only and purely a function of consecutive snapshots (the watcher folds one frame at a time) — it never touches the engine, seeds silently on the first frame, re-seeds on a new or reloaded world, latches one-off milestones, re-arms crises after they clear, and cools down noisy per-tick signals.

### Biome Rendering (Visibly Distinct)
Each biome has a distinct base tile colour so regions read at a glance, and the tile's **ground energy pool** modulates brightness (darker = depleted, brighter/more saturated = near cap):

| Biome | Base Colour | Reads As |
| :--- | :--- | :--- |
| Grassland | green | temperate nursery |
| Desert | amber / tan | hot, sparse |
| Swamp | dark teal | murky, energy-dense |
| Ice Sheet | pale blue-white | cold, barren |

Active `environment_modifiers` tint the affected area so events are visible: **blight** desaturates the region toward grey, **heatwave/ice-age** applies a warm/cool overlay, and **plague** zones get a diagonal hatch. Biome boundaries are drawn straight from the shared Simplex reconstruction (§1) — no tile data is read from the snapshot.

### Organism Rendering — Two Independent Channels
State and action are encoded on **separate visual channels** so both are readable simultaneously:

*   **Fill colour = colour mode (see below).** Drives the legend.
*   **Outline / halo = last action this tick** (`last_action`, §12), always on regardless of mode:
    *   Move (any direction) — blue
    *   Harvest → grazed ground — green
    *   Harvest → predatory attack — red
    *   Reproduce — magenta
    *   Idle — grey
*   **Marker radius ∝ `Size` trait**, so the r/K body-size split (§5) is visible spatially.
*   **Overlays:** a small pip for reproductive-ready (off cooldown + sufficient energy), a pulsing amber ring when the organism is under thermal stress or an active global event affects it, and a brief flash on the victim's tile when predation resolves.

### Colour Modes (Fill Channel)
A mode selector switches what the fill colour encodes; the legend updates to match. This is how "certain states" are surfaced without overloading one channel:

*   **Action** — same palette as the outline, for a pure action heatmap.
*   **Energy** — gradient from red (near 0) through amber to green (near the 100.0 cap, §3).
*   **Diet tendency** — derived from recent `last_action` history: green (grazing), red (predatory), grey (mixed/neither). Diet is emergent, so this is a behavioural readout, not a stored class.
*   **Stress fit** — how far the local tile temperature sits outside the organism's thermal envelope (blue = too cold, red = too hot, neutral = comfortable).
*   **Lineage** — a stable hashed colour per `lineage_id`, making clonal clusters and speciation visible.

### The Colour Key
A persistent legend panel is always visible and reflects the active colour mode plus the always-on channels (action outlines, overlay glyphs, biome swatches with the energy-brightness ramp). Nothing on screen is colour-coded without a corresponding key entry. Palettes are chosen to remain distinguishable under common colour-vision deficiencies, and both light and dark canvas backgrounds are supported.

### Organism Inspector (Click to Open)
Clicking any organism opens a panel showing **every stat associated with that model**, all sourced from its snapshot record:

*   **Identity & lineage:** `name` (§19), `organism_id`, `lineage_id`, `parent_id`, `generation_depth`, `birth_tick`, current `age`.
*   **Physical state:** position, current biome, `energy` (with the 0–100 bar), reproductive readiness and ticks remaining on `reproduction_cooldown_ticks`.
*   **Genome traits:** `Size`, `Speed Capacity`, `Thermal Envelope`, `env_radius`, `org_radius`, `sensory_acuity` — each shown against its `trait_bounds` (§Appendix A).
*   **Per-tick economy:** a live breakdown of the metabolism equation (§3) for this organism — base, thermal stress, sensory tax, last movement cost — so its energy trajectory is explainable.
*   **Behaviour:** `last_action`, `last_action_result`, and the current softmax action-probability distribution.
*   **Brain:** `network_type`, node and connection counts, and a rendered **NEAT graph** with live per-node activation values (from node `state`, §4/§12) and weighted edges. Recurrent links are visually distinguished from feed-forward ones.
*   **Ancestry link:** jump to the parent (if still alive) or view the lineage tree (§14).

Selecting an organism also highlights its `env_radius` / `org_radius` sensory footprint on the map, making the sensing-vs-action asymmetry (§17) visible.

### Relationship to Editing
Inspector fields that are editable feed the intervention flow of §16 — changing a value writes an `edit_log` entry rather than silently mutating state.

---

## 19. Organism Naming

Every organism receives a human-readable name so lineages, inspectors (§18), and event logs read naturally. The template is **Adjective-Adjective-Noun** (e.g. *Silent-Amber-Vole*), drawn from two curated word lists: an adjective list of size **A** (used for both adjective slots) and a noun list of size **N**.

### Namespace
With both adjective slots drawing from the same list — the same word may appear twice, though rarely — the total distinct-name space is:

$$S = A^2 \times N \quad (\text{or } A \times (A-1) \times N \text{ if the two adjectives must differ})$$

Because there are two adjective slots, the adjective list contributes **quadratically**: doubling `A` quadruples the namespace, while doubling `N` only doubles it. Invest in adjectives first.

### Collision Behaviour (Repeats Allowed, Discouraged)
Names are assigned with **no global uniqueness lock** — repeats are avoided by making the namespace large, not forbidden. For `M` organisms alive at once and names drawn uniformly from `S`, the expected fraction sharing a name with another organism is approximately:

$$\text{repeat fraction} \approx \frac{M}{S}$$

So a namespace ~100× the peak living population yields ~1% repeats, and ~1000× yields ~0.1%. Zero-collision naming at billions-scale would require `S ≈ M²/2` (~10¹⁷ names) and is deliberately not attempted.

### Recommended List Sizes
| Peak concurrent population `M` | Target repeat rate | Namespace `S` | Suggested `A` × `N` |
| :--- | :--- | :--- | :--- |
| ~1 million | ~0.1% | ~1×10⁹ | 1,000 adj × 1,000 nouns |
| ~10 million | ~1% | ~1×10⁹ | 1,000 × 1,000 |
| ~100 million | ~1% | ~1×10¹⁰ | 3,000 × 1,100 |
| ~1 billion | ~10% (budget lists) | ~1×10¹⁰ | 3,000 × 1,200 |
| ~1 billion | ~1% | ~1×10¹¹ | 5,000 × 4,000 (or 10,000 × 1,000) |

**Default target: 1,000 adjectives + 1,000 nouns → ~1 billion names.** This gives ~0.1% repeats at a million alive and ~1% at ten million, and remains functional (with more repeats) into the billions. Scale the adjective list up first if higher concurrent populations must stay under ~1% repeats.

### Deterministic Derivation
Names must reproduce identically on replay (§9). The name is a **pure function of the organism's stable, never-reused `organism_id`**: a fixed hash of the id is split into three indices — two into the adjective list, one into the noun list. This needs no name registry and no per-tick state, consumes no PRNG draw, and reproduces exactly on reload. Collisions therefore occur only at the birthday rate above, which is precisely the "avoid where possible, not strictly forbidden" behaviour.

The word lists are versioned assets referenced from configuration; the resolved `name` is stored on each organism record (§12) so a snapshot carries names directly. A run pins its word-list version, so a fixed run always replays to identical names even if the default lists are later expanded.

*If stricter avoidance is ever required, a per-world active-name set (or a Bloom filter at billions-scale, to bound memory) can gate births with a bounded number of re-rolls, accepting a duplicate after `k` failed attempts — at the cost of adding that structure to snapshot state and a naming PRNG stream.*

---

## 20. Cooperation (Kin Selection & Mutualism)

*Post-v1 extension (see `tasks.md` Phase 16). This is social behaviour **between separate individuals**; it does not change the level of selection (that is §21).*

The engine already produces the substrate cooperation needs — **population viscosity**: offspring are placed on an adjacent tile and inherit their parent's `lineage_id`, so lineages form spatial clusters, and asexual cloning makes within-cluster relatedness ≈ 1. What v1 lacks is a way for an organism to *benefit* a neighbour and *recognise* kin. §20 adds both, then lets cooperation evolve (or not) under selection.

### Kin recognition
The fixed input vector (§13) gains a **relatedness signal**: the genome relatedness (0–1) to the closest organism inside `org_radius`. Relatedness is a phenotype-matching proxy — `1 − mean(|trait difference| / trait bound span)`, clamped to [0,1] — so clones read ≈ 1 and divergent organisms read lower. This needs no lineage bookkeeping and suits an asexual world; a lineage-id match could be substituted if exact kinship is preferred.

### Energy sharing
The action set (§4) gains four directional **Share** outputs (N/S/E/W). A Share transfers a fraction of the donor's energy to the live organism on the adjacent tile, credited at `share_efficiency < 1`; the lost remainder is what makes altruism a genuine cost (and prevents free energy-laundering loops). Off-grid, empty, or not-yet-materialised-offspring targets are a no-op.

**Generosity is an evolvable trait, not a global constant.** The fraction of energy a Share donates is `share_fraction`, a **per-organism genome trait** (bounded `[0, 1]`, §3) that mutates and is selected like any other. Lineages are free to drift toward hoarding (→ 0, the brain may still emit Share but nothing moves) or over-sharing (→ 1, giving away nearly everything), and selection — not the designer — decides which pays off in a given local kin economy. The `share_fraction` **config** value is only the *genesis* generosity every founder starts with; from there the trait is inherited with mutation. Its population mean is tracked in metrics (§14) so drift toward or away from altruism is observable.

**The whole cooperation feature set is toggleable per world at genesis** via a `cooperation.enabled` flag (default on). With it off, Share actions are inert no-ops and the kin-predation penalty is not charged, so a run can be observed with cooperation entirely absent (the kin-relatedness sense stays wired in either way — it is information, not cooperation).

**Sharing is relatedness-scaled.** When an organism chooses to Share, the donation actually goes through with probability `share_probability_floor + (share_probability_ceiling − share_probability_floor) · relatedness(donor, recipient)`. The ceiling `< 1` means even identical kin are helped *usually, not always*; the floor `> 0` means strangers are helped *rarely, but possibly*. The gate is rolled against the behaviour PRNG stream (the same stream that drives action selection and combat), so sharing is stochastic yet fully deterministic and replayable (§9). With kin recognition present, brains can also evolve to *withhold* predation from kin (cooperation via restraint) with no new action; an optional `kin_predation_penalty` (default 0) is a tunable structural deterrent against cannibalism.

### Carrying capacity (density-dependent crowding)
Cooperation props up organisms that would otherwise starve, which pushes populations upward, so §20 pairs it with a **continuous overpopulation cost** (a metabolism term, §3): each tick an organism pays `crowding_cost_per_neighbour` for every neighbour in its 3×3 block beyond `crowding_free_neighbours`. This is density-*dependent*, so it self-targets — it is zero in sparse regions and only bites where organisms pack together, giving the world a soft carrying capacity (dense clusters starve faster → deaths → density relaxes) without harming sparse populations. It complements the stochastic Density-Dependent Plague (§6), which is a rarer, harsher shock; the crowding cost is the always-on baseline.

### What this changes
Cooperation is expected to be favoured **inside kin clusters** (Hamilton's `rB > C`, with `r ≈ 1` for clones and `C` set by `1 − share_efficiency`) and disfavoured between strangers — an emergent, not scripted, outcome. Metrics (§14) expose energy shared, share success/failure, and kin-predation counts; the GUI (§18) adds a Share action colour and a Cooperation colour mode. The default configuration stays calibrated (§15) with sharing available.

---

## 21. Multicellular Individuals

*Independent of §20. This introduces a **new level of individuality** — a body of many differentiated cells, selected and reproduced as one organism, with a germline/soma split. Intra-body coordination is structural (one shared energy budget, a germline that alone reproduces), not evolved social behaviour, which is why it needs nothing from §20.*

### The aggregate body model
A body is an **aggregate that still occupies a single tile** (not a spatial multi-tile cluster): it carries an evolvable `cell_count` and, from six evolvable specialisation weights, a normalised split of those cells across six jobs. A single `cell_count = 1` body is a plain organism, so the transition is **continuous** — founders start unicellular and lineages evolve upward. The existing single NEAT brain is the body-level controller (the whole body acts as one agent); there is no separate developmental program in this model. Multicellularity is a **per-world toggle** (`multicellular.enabled`, default on); with it off, every body is a single generalist cell and the model reduces exactly to the pre-§21 engine.

### Cell types (division of labour)
Six jobs, each a *bonus for emphasising that type above the ⅙ generalist baseline*, so a perfectly generalist body is neutral (behaves like a plain organism scaled by its cell count):
- **Germ** — reproduction. A body below `germ_reproduction_threshold` germ fraction is **sterile soma**: it can support the body but cannot bud. This is the germline/soma split and the transition's stability mechanism.
- **Feeder** — multiplies the usable energy extracted from grazing.
- **Store** — raises the body's **energy capacity** (`base_capacity + store_cells · store_capacity_per_cell`); this is how a bigger body "stores more".
- **Defender** — raises effective combat mass and insulates against thermal stress.
- **Mover** — cuts locomotion tax.
- **Sensor** — sharpens perception (higher effective `sensory_acuity`, i.e. less injected noise).

A body of `N` cells has `fraction_type · N` cells of each type — many cells per job, not one-of-each — so specialisation is a matter of *how much* of the body is devoted to each.

### The square-cube law (why bodies stay small)
Body **mass** is `cell_count · size`; metabolism and combat scale with it. The size limit is emergent, not a hard cap: **maintenance scales with volume (∝ N)** — every cell must be fed, plus a per-cell `coordination_cost` — while **grazing intake is capped by surface area (∝ N^⅔)** (`intake_surface_coeff · N^⅔`), because only surface cells exchange with the environment. The ratio of sustainable intake to upkeep therefore *falls* as the body grows (∝ N^{−⅓}), so beyond an optimal size a body runs a permanent deficit and starves. This — the classic square-cube constraint — is what keeps `cell_count` bounded well below its hard limit under selection, and why specialisation (feeder efficiency, store buffering) is what makes a larger body pay off.

### Economy, reproduction & determinism
Capacity is a deterministic function of genome + config, recomputed on load (not serialized), so save/reload stays byte-identical. Reproduction is gated on germ fraction and costs energy proportional to whole-body mass, so bigger bodies are dearer to bud; offspring inherit the body-plan genome (cell count + six weights) with mutation, exactly like every other trait — new draws come from the mutation stream in fixed order (§9). All effects are pure functions of the genome (see the engine's `Morphology`), so the whole model is deterministic and replayable. Metrics track mean and distribution of `cell_count`; the inspector shows a body's cell composition and whether it is fertile or sterile soma.

---

## Appendix A: Configuration Reference

The `configuration` block centralizes every coupled constant so experiments tune behavior without touching source. It is versioned by `config_version` (§12). Values below are illustrative defaults, not final calibration.

### Metabolism & Sensory
*   `base_metabolism_per_size` — Size × Base term in the metabolism equation (§3).
*   `sensory_tax_c1` — linear coefficient on `env_radius`.
*   `sensory_tax_c2` — quadratic coefficient on `org_radius`.
*   `sensory_tax_c3` — coefficient on `sensory_acuity`.
*   `thermal_stress_scale` — multiplier applied when tile temperature leaves the thermal envelope.

### Movement & Combat
*   `locomotion_velocity_exponent` — the power applied to velocity in the movement equation (default 2).
*   `biome_friction` — per-biome friction modifiers (`grassland`, `desert`, `swamp`, `ice_sheet`).
*   `predation_transfer_fraction` — fraction of victim energy transferred on a successful kill.
*   `failed_combat_penalty` — retaliation energy cost on a failed kill.

### Biomes & Resources
*   `biome_regen_rate` — ambient energy trickle per biome per tick.
*   `biome_energy_cap` — hard ceiling on tile ground energy per biome.
*   `biome_temperature` — regional temperature per biome.
*   `noise_config` — Simplex frequency/octaves/thresholds used to reconstruct terrain (the same C# implementation on every surface, including the Avalonia browser target).

### Reproduction & Mutation
*   `reproduction_base_cost` — multiplied by size to set the reproduction threshold and cost.
*   `offspring_energy_fraction` — fraction of the paid cost that becomes offspring starting energy (remainder is construction inefficiency, §11).
*   `reproduction_cooldown_ticks` — minimum ticks between births (default 3).
*   `trait_mutation_rate` / `trait_mutation_delta` — probability and bounded magnitude of trait drift.
*   `weight_mutation_rate` / `weight_mutation_power` — NEAT weight perturbation controls.
*   `connection_mutation_rate` / `node_mutation_rate` — structural mutation probabilities.
*   `trait_bounds` — hard min/max for every mutable trait.

### Events & Lifecycle
*   `event_probabilities` — per-tick roll thresholds for blight, plague, and temperature anomaly.
*   `event_durations` — active-tick lengths per event type.
*   `temperature_anomaly_magnitude` — ±°C shift (default ±20).
*   `plague_density_threshold` — crowding level that triggers density-dependent drain.
*   `corpse_energy_fraction` — fraction of a non-predation death's energy deposited to the tile.
*   `initial_population` — genesis organism count (default ~200).
*   `senescence` — aging model (default on).

### Naming (§19)
*   `name_adjective_list` — reference + version of the adjective word list (default size ~1,000).
*   `name_noun_list` — reference + version of the noun word list (default size ~1,000).
*   `name_require_distinct_adjectives` — if true, the two adjective slots must differ (default false).
