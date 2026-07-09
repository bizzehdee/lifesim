# Design plan: diurnal & seasonal cycles, light, and phototaxis

A planned extension that gives the world a **nested day/night + seasonal clock**, a derived **per-tile
light field**, and two coupled consequences: the clock **swings temperature** (colder nights and
winters), and — behind a headline toggle — **light drives food regeneration (photosynthesis)**.
Organisms gain new senses so behaviour can time itself to the cycle (circadian rhythm) and orient to
light (phototaxis).

> **Implementation status (2026-07-09).** Fully implemented (tasks EC-1…EC-11). Phase 1: the
> `EnvironmentCycleConfig` + pure `EnvironmentClock`, with the cyclic temperature offset folded into the
> existing offset seam. Phase 2: per-biome `LightFactor`, and the `Photosynthesis` toggle scaling
> ground-energy regen by local light (setup checkbox + editor exclusion + options round-trip). Phase 3:
> the seven light/cycle sensory inputs, `InputCount` 19→26, and a schema major bump to 2.0 so pre-cycle
> snapshots reject cleanly. Phase 4: the glance/statistics time line, a `Light` colour mode + legend,
> the inspector light readout, and this rules.md update. Determinism held throughout (the clock is a pure
> function of the tick); calibration stayed green. Follow-ups below (latitude bands, per-sense evolvable
> acuity/cost) remain open.

Like every feature it must respect the hard constraints: **determinism** (byte-identical replay /
save-reload / any thread count), **WASM/AOT/trimming friendliness** in the Core, and **no
objective/fitness function bolted onto selection** — a rhythm or a light-seeking habit has to pay for
itself in surviving descendants, or it doesn't spread.

## Scope decisions (confirmed)

| Decision | Choice |
| :--- | :--- |
| Light ↔ energy economy | **Both, headline toggle.** A `Photosynthesis` setup checkbox (like Senescence). On → local light scales ground-energy regen; off → regen is exactly as today. |
| Cycles | **Both, nested.** A fast day/night cycle with a slow seasonal cycle riding on top. |
| Temperature | **Light + temperature.** The cycle also shifts effective tile temperature, feeding the existing thermoception input and thermal-stress metabolism. |

The **clock, light field, temperature swing, and new senses are always on** (only *photosynthesis* is
gated). A "flat" world is still reachable without a second toggle: set the cycle amplitudes and the
light floors so nothing varies (see Config). The new sensory inputs then simply carry near-constant
values that evolution ignores.

---

## Why this fits the architecture

The engine is friendly to this because the hard prerequisites already exist:

- **A deterministic canonical clock.** `SimulationWorld.Tick` is the single source of time. The whole
  cycle is a *pure function of `Tick` + config* — no randomness, so **no new PRNG stream** and nothing
  new to serialize (it's re-derived on load). This is the same "derived, not stored" property that
  keeps replay/save-reload trivially correct.
- **A temperature-offset seam already exists.** Climatic events already add
  `EnvironmentState.TemperatureOffset` to `TerrainSampler.TemperatureCelsiusAt(x,y)` at both the
  sensing site (`SensoryInputBuilder.Build`) and the metabolism site (Metabolism phase). The cyclic
  offset just adds into that same effective offset — and, crucially, stays *outside* the per-tile
  temperature cache (which is time-invariant), exactly as the event offset already does.
- **A per-tile regen seam already exists.** `GroundEnergyGrid.RegenerateTick()` walks depleted tiles
  and returns each toward its biome cap by that biome's `RegenRate`. Photosynthesis is a single
  multiplier on that rate.
- **A fixed, normalized sensory vector with an evolvable acuity/cost model.** New senses slot into
  `SensoryField` + `SensoryInputBuilder`, inherit the existing sensory-noise/acuity treatment for free,
  and (like `EnvRadius`/`OrgRadius`) can be made to cost something so perception stays a trade-off.

---

## The model

### 1. The clock (pure function of `Tick`)

Two phases in `[0,1)`, from config lengths:

- `dayPhase   = (Tick mod DayLengthTicks)  / DayLengthTicks`
- `seasonPhase = (Tick mod YearLengthTicks) / YearLengthTicks`

A small `EnvironmentClock` helper (Core, static/pure) turns these into the three driving quantities:

- **Global light** `globalLight ∈ [NightLightFloor, 1]` — a raised cosine of `dayPhase` (peak at noon,
  trough at midnight), its amplitude further scaled down in winter by a raised cosine of `seasonPhase`
  between `WinterLightScale` and `1`.
- **Cyclic temperature offset (°C)** — `DayNightTemperatureAmplitude · cos(dayPhase)` +
  `SeasonalTemperatureAmplitude · cos(seasonPhase)` (peaks at noon / midsummer, troughs at
  midnight / midwinter).

### 2. The light field (per tile)

`light(x,y) = globalLight × BiomeSettings.LightFactor(biome(x,y))`.

`LightFactor` is a new per-biome constant (default `1.0`): open biomes (Desert, IceSheet) stay ~`1.0`;
shaded ones (Swamp/forest-like) sit below `1.0`. Day/night is spatially uniform, so **the only spatial
light gradient comes from `LightFactor`** — which is precisely what makes phototaxis a real
navigational choice ("move toward sunnier ground / out of shade") rather than a no-op.

### 3. Temperature coupling

`temperatureShift = EnvironmentState.TemperatureOffset (events) + clock.CyclicTemperatureOffset`.
Computed once per tick in `SimulationWorld.Advance` and threaded through the existing
`temperatureOffset`/`temperatureShift` variables — no new call sites. Nights/winters get colder, so
the existing thermoception input and thermal-stress metabolism now vary on a second axis.

### 4. Photosynthesis (headline toggle, default on)

When `Photosynthesis` is on, `RegenerateTick` scales each depleted tile's return by its local light:
`return = RegenRate × light(x,y)`. Consequences that emerge, not scripted:

- **Daily productivity pulse** — food regrows fast at midday, stalls at night → day/night foraging vs
  rest rhythms; nocturnal niches become viable if predation is the bigger risk by day.
- **Seasonal boom/bust** — winters throttle regen → scarcity → migration / storage pressure. Pairs
  naturally with the existing corpse-energy and blight mechanics.
- **Phototaxis gets teeth for free** — sunlit tiles become genuinely richer, so even the *existing*
  richest-tile sense starts pointing toward the light; the dedicated light senses below add the ability
  to *anticipate* and to orient when photosynthesis is off.

When off: `RegenerateTick` is byte-for-byte today's behaviour. (Still suspended entirely during a
Resource Blight, as now.)

### 5. New sensory inputs (+7 → `InputCount` 19 → 26)

Added to `SensoryField` / `SensoryInputBuilder`; the clock values are passed into `Build(...)` beside
the existing `globalStress`/`temperatureOffset`:

| Field | Meaning | Enables |
| :--- | :--- | :--- |
| `LightLevel` | `light(self)` normalized | grazing-by-light; day/night state even with photosynthesis off |
| `DayPhaseSin`, `DayPhaseCos` | circadian phase as a smooth cyclic pair | *anticipating* dawn/dusk (not just reacting to current darkness) |
| `SeasonPhaseSin`, `SeasonPhaseCos` | seasonal phase | seasonal breeding / pre-winter behaviour |
| `LightDirectionX`, `LightDirectionY` | unit vector toward the brightest tile within radius (reuses `EnvRadius`) | phototaxis / shade-seeking |

Sin/cos pairs are the standard way to hand a neural net a cyclic quantity (no discontinuity at the
wrap). `LightDirection*` can be dropped (−2 inputs) if we want to lean on the food gradient instead;
recommended to keep so light-seeking works with photosynthesis **off**.

---

## Determinism & persistence

- **No new PRNG stream.** The cycle is deterministic from `Tick`; sensory noise still comes from the
  existing `SensoryNoise` stream and applies to the new inputs automatically.
- **Nothing new to serialize for the clock** — it's re-derived from `Tick` + `Configuration` on load.
  Ground-energy state already persists (so a mid-day light-boosted grid round-trips as-is).
- **`InputCount` change is the one migration cost.** It flows through `NeatGenomeFactory` (founders get
  N input nodes) and the brain portion of the snapshot schema. Both runs in any determinism test share
  the same `InputCount`, so thread-count and seed-replay equivalence are unaffected — but this is a
  **schema bump**: old snapshots saved with 19 inputs won't load, and any test hard-coding the input
  count / trait list / golden snapshot needs regenerating. Worth batching all +7 inputs in one change.

---

## Config (all advanced-editor tunable; auto-surfaced)

New `EnvironmentCycleConfig` under `SimulationConfig` (sensible starting values):

- `DayLengthTicks` = 240, `YearLengthTicks` = 4800 (≈ 20 days/year)
- `DayNightTemperatureAmplitude` = 6 °C, `SeasonalTemperatureAmplitude` = 12 °C
- `NightLightFloor` = 0.05, `WinterLightScale` = 0.4

New per-biome `BiomeSettings.LightFactor` (default `1.0`; Swamp lower).

New headline `SimulationConfig.Photosynthesis` (bool, default **on**).

**Flat-world escape hatch** (no second toggle needed): `NightLightFloor = 1`, `WinterLightScale = 1`,
both temperature amplitudes `0` → constant light, no temperature swing.

---

## UI touchpoints (per CLAUDE.md)

- **Headline toggle:** add a **Photosynthesis** setup checkbox, add its config path to the editor's
  `Excluded` set, and round-trip it through `SaveOptionsJson`/`LoadOptionsFromJson` (mirroring
  `Senescence`).
- **Advanced editor:** verify the new `EnvironmentCycleConfig` group and `LightFactor` fields appear,
  are grouped sensibly, and are labelled well (auto-generated, but check).
- **At-a-glance / HUD:** a small time indicator — e.g. `🌞 Day 3 · midday · late spring` — from the
  clock, in the Info panel's glance stats.
- **Colour mode + legend:** a `Light` colour mode over the map (with its legend entry — every colour
  has one). Optional but on-theme.
- **Inspector:** show the selected organism's local `LightLevel`. Optional.

---

## Suggested phasing

1. **Clock + temperature** — `EnvironmentCycleConfig`, `EnvironmentClock` (pure), fold cyclic offset
   into `temperatureShift`. No new inputs yet; determinism/tests unchanged. Ship-able alone.
2. **Light field + photosynthesis toggle** — `LightFactor`, light in `RegenerateTick` behind the
   toggle, setup checkbox + editor/Excluded + options round-trip.
3. **Senses** — the +7 `SensoryField` inputs, `InputCount` bump, snapshot-schema regen, determinism
   test refresh. (The big migration; do last so 1–2 land cleanly first.)
4. **UI polish** — glance time indicator, `Light` colour mode, inspector light readout.
5. **rules.md** — document the cycle, phototaxis, and photosynthesis.

## Resolved decisions

- **Latitude → uniform for v1.** One global cycle across the whole map; no `y`-dependent climate bands.
  Spatial climate (poles vs equator, and possibly flipped hemispheres) is a clean follow-up once the
  base system is proven.
- **Sense set → keep all +7** (`InputCount` 19 → 26). Full circadian *and* seasonal anticipation, plus
  a phototaxis direction that works even with photosynthesis **off**. One migration, done properly.
- **Photosynthesis → default on.** New worlds get light-driven regen out of the box (the version where
  the cycle actually shapes the ecology); the setup checkbox lets a world opt out.

## Future follow-ups

- Latitude bands / flipped hemispheres for spatial climate + north–south migration.
- Per-sense evolvable acuity/cost for the light senses (as `EnvRadius`/sensor cells already do).
