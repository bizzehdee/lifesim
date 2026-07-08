# LifeSim

**A little world where life evolves on its own.** LifeSim drops a population of organisms into a
procedurally generated map and lets natural selection do the rest. Each organism has a brain (a small
neural network), a body of inherited traits, and an energy budget. The ones that manage to stay alive
and reproduce pass their genes on — with small mutations — so over thousands of ticks the population
adapts to its world. Nobody scripts the behaviour; it emerges.

You can just watch it unfold, poke at it while it runs, or **design your own "types" of organism** and
set them loose to compete (see [Creating custom organisms](#creating-custom-organisms)).

For the full plain-language rules of the world, see [`rules.md`](./rules.md).

---

## Getting LifeSim

Grab the latest build for your operating system from the **Releases** page, unzip it anywhere, and
launch it:

- **Windows** — run `LifeSim.exe`
- **Linux** — run the `LifeSim` executable (`chmod +x LifeSim` first if needed)

No installation and nothing else to set up — it's a self-contained desktop app. (There's also a
try-it-in-your-browser demo linked from the Releases page.)

> Building from source instead? See [For developers](#for-developers).

---

## Creating a world

Press **⟳ New…** and set the starting parameters:

- **Seed** — the world is fully determined by its seed. The same seed always grows the same *map*; the
  🎲 button rolls a fresh one.
- **Width / Height** — the map size, in tiles.
- **Population** — how many organisms to start with (ignored if you compose the population *by type* —
  see below).
- **Threads** — how many CPU cores to use. Purely a speed knob; it never changes the outcome.

And a few world-shaping toggles:

- **Cooperation** — organisms can share energy and evolve how generous to be. Off = no sharing at all.
- **Senescence (aging)** — old organisms pay a growing upkeep so no lineage is immortal.
- **Multicellularity** — bodies can evolve into many specialised cells (see the rules). Off = everyone
  stays a single cell.
- **Entropy** — seed the *life* (behaviour, mutation, events) from randomness, so the same map plays out
  differently every run. Off = a run is fully reproducible from its seed. Either way a created world
  still saves, reloads, and replays exactly.

The **Advanced** panel exposes every underlying constant if you want to tune the physics, economy, or
mutation rates. You can **💾 Save / 📂 Load options** to reuse a setup later.

Press **Create World** — the world starts paused. Then use the control deck: **▶ Play / ⏸ Pause /
⏭ Step**, the **speed** slider (drag far right for unthrottled), and **Save… / Load…** to snapshot a run
to disk.

---

## Watching the world

- **The map** is the big view. Zoom with **+ / −**, fit with **⤢** (or double-click), and **click any
  organism** to inspect it.
- **🎨 Colours** picks what the colours *mean* (biome, energy, a trait, lineage, …) and shows the legend.
- **📊 Statistics** opens the live dashboard: population and vital rates, the energy economy, trait
  averages and histograms, the biome spread, and — when you've seeded types — a **"Population by type"**
  scoreboard and a **kin-selection** readout.
- **🔔 Notifications** surfaces notable moments (an ice age, a population crash, the first multicellular
  body, a lineage milestone…). Many are clickable and jump to the organism involved.

The right sidebar (drag its edge to resize) has three tabs:

- **Info** — an *at-a-glance* summary (tick, population, generation, births/deaths, …) and, when you've
  selected an organism, its full **inspector**: identity and lineage, physical state, its genome traits
  against their bounds, the per-tick energy breakdown, its action probabilities, and its brain graph.
- **Ranking** — the all-time leaderboard by *descendant score* (children + ½ grandchildren + …), alive
  or dead, with how much each has helped kin.
- **Organisms** — every living organism, sortable by age, size, children, score, brain size, or prey
  count. Click one to jump to it.

### What an organism evolves

Every organism carries inheritable traits you can watch drift in the inspector and the statistics
histograms: body **size**, **speed**, a **thermal comfort band**, **sensing** radii and acuity,
**metabolic efficiency** (frugality), the defences **armour / evasion / toxicity**, neural
**plasticity** and **learning decay** (within-life learning), **generosity**, and — with
multicellularity on — a **body plan** of specialised cells. None of these are set by you; selection
finds whatever values survive in your world.

---

## Creating custom organisms

This is the fun part: instead of letting the founders start with random brains, you can **design
"types" — personalities — and seed a world with a mix of them to see which wins.**

The golden rule: **a type is only a starting point.** Its script becomes the *seed* of a brain; from
tick 0 that brain mutates and competes exactly like any evolved brain. So you don't hand-craft a winner
— you hand-craft a starting instinct, and **survival of the fittest decides** whether it thrives. Every
organism also keeps a label of the type it descended from, so you can watch the mix shift on the
scoreboard.

### Seeding a world by type

In the **New World** screen, open **"Founding population by type"**. You'll see:

- **Generic** — the normal evolved brain (random start), and
- the seven **built-in examples** (Forager, Selfish, Selfless, Cooperator, Aggressor, Fearless, Coward).

Give any of them a **count** above zero and they replace the flat Population field. For example
`40 Selfish, 40 Selfless, 40 Aggressor, 40 Generic` seeds an even four-way contest. Press **Create
World** and watch the **Statistics → Population by type** scoreboard and the share-over-time chart.

Each built-in is shown as **editable script text** — copy one and tweak it — and **Add custom type**
gives you a blank one to name and write yourself. The editor validates as you type: a green *"compiles"*
or a red parse error.

### The scripting language

A brain script is a short list of *leanings*: rules that push the organism toward or away from actions.
It's deliberately tiny and line-oriented:

```text
type Selfish:
  prefer HarvestToward(food)   strong
  prefer MoveToward(food)      always
  prefer HarvestSelf           when hungry
  prefer Reproduce             when ready
  avoid  Share(any)            strong
```

Each rule reads: **`prefer` or `avoid`** an **action**, optionally **`when` a condition** holds, at an
optional strength (**`weak`** / **`strong`**, or **`always`** = unconditional). Lines starting with `#`
are comments.

**Actions you can steer toward or away from:**

| Action | Meaning |
| :--- | :--- |
| `Reproduce` | make an offspring (when able) |
| `Idle` | do nothing |
| `HarvestSelf` | eat the food on your own tile |
| `MoveToward(dir)` / `MoveAway(dir)` | move toward / away from something |
| `HarvestToward(dir)` | harvest in a direction — **onto an occupied tile this is an attack (predation)** |
| `ShareToward(dir)` | donate energy to a neighbour in that direction |
| `Move(any)` / `Harvest(any)` / `Share(any)` | the whole family of that action (handy with `avoid`) |

**Directions** (what `dir` can be):

| Direction | Points at |
| :--- | :--- |
| `food` | the richest nearby tile |
| `nearest` | the closest organism |
| `smaller_neighbour` | the nearest organism, biased toward smaller (easier prey) |
| `kin` / `stranger` | the nearest organism, biased toward relatives / non-relatives |

**Conditions** (what `when` can gate on):

| Condition | Fires when… |
| :--- | :--- |
| `always` | unconditionally |
| `ready` | it can reproduce right now |
| `hungry` / `full` | its energy is low / high |
| `threatened` | larger organisms are nearby |
| `crowded` | it's in a dense cluster |
| `prey_near` | smaller organisms are nearby |
| `kin_near` / `stranger_near` | the closest organism is / isn't a relative |
| `toxic_prey_near` | the closest organism is toxic (a warning signal) |
| `stressed` | a world event (ice age, blight…) is active |

**A worked example — the Aggressor:**

```text
type Aggressor:
  prefer HarvestToward(smaller_neighbour) strong   # hunt, preferring smaller prey
  prefer MoveToward(nearest)    always             # close the distance
  prefer HarvestToward(nearest) when prey_near     # press the attack when prey is around
  prefer Reproduce              when ready
  avoid  Share(any)             always             # take, never give
```

Drop a few Aggressors into a world of Foragers and watch whether predation pays — and whether the prey
evolve armour, toxicity, or the sense to flee.

### Good to know

- **Your script is a seed, not a straitjacket.** Once the world runs, mutation and selection take over;
  a "Selfish" lineage may evolve into something you didn't write. That's the point.
- **It's a bias, not a program.** There are no `if/then` blocks — each rule just tilts the odds of an
  action. Strong, well-chosen leanings make a decisive personality; the network fills in the rest and
  evolution refines it.
- **Predators can't see everything.** They sense the direction, distance, size, relatedness, and
  toxicity of the nearest organism — so, e.g., avoiding toxic prey has to be learned or evolved; it
  isn't free.
- Setups (including your custom types) round-trip through **Save / Load options**.

---

## For developers

A single deterministic **C# Simulation Core** consumed by two surfaces (a headless CLI and an Avalonia
app with desktop + browser heads).

| Project | Role |
| :--- | :--- |
| `src/LifeSim.Core` | Deterministic engine (class library). Authoritative; no UI deps; trim/AOT-friendly so it runs under WASM. |
| `src/LifeSim.Console` | Headless CLI (`sim`) — batch evolution, calibration, snapshot I/O, metrics export. |
| `src/LifeSim.App` | Shared Avalonia views/view-models. |
| `src/LifeSim.App.Desktop` | Desktop head (Windows/Linux) — embeds the Core, live. |
| `src/LifeSim.App.Browser` | Browser/WASM head — a constrained demo; renders snapshots/streams. |
| `tests/…` | Engine, determinism, calibration, console, and app tests. |

**Prerequisites:** .NET SDK **10.0.201+** (pinned in [`global.json`](./global.json)). The browser target
builds with the SDK's bundled wasm capability; if a machine lacks it, run
`dotnet workload restore src/LifeSim.App.Browser`.

```bash
# Build / test everything
dotnet build LifeSim.slnx -c Release
dotnet test  LifeSim.slnx -c Release

# Run the desktop app
dotnet run --project src/LifeSim.App.Desktop

# Headless batch run (create a world, advance it, export metrics)
dotnet run --project src/LifeSim.Console -- new --out world.json --seed 42 --width 128 --height 128
dotnet run --project src/LifeSim.Console -- run --in world.json --out out.json --ticks 1000 --metrics m.csv --metrics-format csv

# Publish the browser (WASM) demo (serve artifacts/wasm/wwwroot over HTTP with COOP/COEP headers)
dotnet publish src/LifeSim.App.Browser -c Release -o artifacts/wasm

# Formatting (CI enforces this)
dotnet format LifeSim.slnx --verify-no-changes
```

**Conventions**
- Central package management: versions in [`Directory.Packages.props`](./Directory.Packages.props).
- Build settings & analyzers: [`Directory.Build.props`](./Directory.Build.props); style in
  [`.editorconfig`](./.editorconfig).
- **Determinism is a hard contract:** named PRNG streams, sorted iteration with explicit tie-breaking,
  and the flagship determinism tests (thread-count, seed-replay, save/reload) must stay green.
- Design notes for planned features live in [`docs/design`](./docs/design).
- UI guidelines: the user-level `avalonia-ui` skill plus the project `lifesim-ui` skill
  (`.claude/skills/lifesim-ui/`).
