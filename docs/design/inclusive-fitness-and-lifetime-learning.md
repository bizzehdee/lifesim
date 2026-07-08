# Design plan: inclusive fitness accounting & lifetime learning

Two planned extensions that make the sim's selection story closer to real biology. Neither is
implemented yet — this is the design. Both must respect the project's hard constraints: **determinism**
(byte-identical replay / save-reload / any thread count), **WASM/AOT/trimming friendliness** in the
Core, and **no objective/fitness function bolted onto selection** (selection stays emergent — the only
thing that "wins" is what leaves more surviving descendants).

Context: today selection is pure *direct* differential reproduction. Kin behaviour (relatedness sensor,
energy sharing, kin-predation penalty) already exists, so kin selection *can* emerge, but nothing
measures it; and brains are fixed within a life — all adaptation is across generations.

---

## 1. Inclusive fitness accounting

**Goal.** Make kin selection *visible and measurable* — account for each lineage's inclusive fitness
(direct offspring **plus** relatedness-weighted help it gave kin), à la Hamilton's rule `rB > C`. This
is an **accounting/observability** feature, not a new selection rule: we do not reward inclusive fitness
directly (that would be an objective function). We measure whether altruism is being selected.

**Why accounting, not a new selection force.** Selection already is inclusive-fitness-driven *in
effect* — a gene for helping kin spreads iff it raises copies of itself, whether via own offspring or
relatives'. We just can't see it. Adding a metric respects the "no fitness function" rule while
answering "is kin selection happening here?"

### Mechanism
- At **share resolution**, accumulate per-organism:
  - `HelpGiven += energyDelivered × relatedness(donor, recipient)` — relatedness-weighted altruism.
  - Track whether the share satisfied Hamilton's rule this event: `r·B > C`, where `C` = energy the
    donor lost, `B` = energy the recipient gained (`C·ShareEfficiency`), `r` = relatedness. Count
    `hamilton_satisfied` vs `hamilton_violated` shares.
- **Inclusive fitness proxy** per lineage = direct weighted descendants (the existing "score") `+ λ ×
  HelpGiven-to-kin`, with `λ` a config weight. It's a proxy (we don't trace which of a recipient's
  offspring a specific gift caused), clearly labelled as such.

### Determinism / safety
- Pure accounting over events that already happen; no PRNG, no new stochasticity. Relatedness is
  already computed at share time. Safe for replay/threads (writes are per-organism, in the serial
  share/intent phase).

### Data-model changes
- `Organism`: add `HelpGiven` (double, relatedness-weighted) — serialized, **not** inherited (a
  lifetime tally, like age/predation record).
- `TickCounters` / `SimulationMetrics`: add `AltruisticShares` (Hamilton-satisfied) vs
  `WastefulShares`, and population mean `HelpGiven`.
- Snapshot: `help_given` on organism (optional field, back-compatible).

### Phasing
1. Track `HelpGiven` + Hamilton counters at share resolution; serialize; metrics.
2. Inspector + Ranking: show direct vs indirect (inclusive) contribution per organism/lineage.
3. Stats panel: "altruistic vs wasteful shares" and mean indirect fitness — the readout that tells you
   kin selection is (or isn't) operating.
4. (Optional) a Hamilton's-rule scatter/among the founding-type chart family, if it earns its keep.

### Open questions
- `λ` weighting is arbitrary; keep it a clearly-labelled analytics knob, not a balance lever.
- Indirect-fitness *attribution* (which offspring a gift caused) is genuinely hard; the proxy is honest
  about being a proxy. A precise version would need gift→survival→offspring causal tracing (expensive,
  probably not worth it).

---

## 2. Lifetime learning

**Goal.** Let an organism's brain *adapt within its own life*, not only across generations — while
keeping evolution in charge of *how well* it can learn. The faithful model is **Darwinian + Baldwin
effect**: learning is **not** inherited, but the *capacity* to learn is, and learning guides evolution
(lineages that can learn survamp environments faster, so genes that make good learners spread).

### The key design decision: Darwinian, not Lamarckian
- **Germline weights** (inherited, mutated at birth — today's `Brain` weights) vs **live weights** (a
  per-organism working copy that starts as a copy of the germline and is updated during life).
- Offspring inherit the **germline** (mutated), never the learned live weights. This is biologically
  correct and avoids the instability/limits of Lamarckian inheritance. Cost: brains store two weight
  sets per living organism (germline + live).
- Consequence — the **Baldwin effect**: because learning lets an organism reach good behaviour its
  germline only approximates, selection favours germlines that are *easy to learn from*, and over time
  good behaviour can become partly innate ("genetic assimilation"). This is the interesting emergent
  payoff and the reason to prefer this model.

### Mechanism (reward-modulated Hebbian plasticity)
- New evolvable trait **`Plasticity`** ∈ [0,1] (a learning rate), founders start at 0 (learning is
  evolved in, like the defensive traits) — so a no-learning baseline is always available and the
  Baldwin effect has to *earn* plasticity.
- **Reward signal**: the organism's recent energy delta (gained energy → positive; starving → negative).
  Purely from state, deterministic.
- **Update rule**, applied once per tick in the *serial* post-decision step (where `UpdateBrain`
  already runs — so the parallel Propagate stays a pure read):
  `Δw_ij = Plasticity · learnRateScale · reward · preActivation_i · postActivation_j`, weights clamped
  to a bound. No PRNG.
- **Metabolic cost**: plasticity adds upkeep (neural plasticity is expensive), folded into the
  efficiency-discounted self-cost like the defence traits. So learning is favoured only where the
  environment is variable enough to repay it — matching learning-evolution theory.

### Determinism / safety
- Update is a deterministic function of activations + energy delta; no randomness.
- **Parallelism**: keep weight updates in the serial phase; the parallel Decision phase must remain a
  pure forward pass (it currently is). Each organism writes only its own brain.
- **Save/reload**: both germline and live weights become per-organism serialized state. Flagship
  determinism tests stay equivalence-based, so they hold; the mutation-stream shift from the new trait
  will move seed-sensitive tests (re-tune, as with every trait added so far).

### Data-model changes
- `NeatGenome`/organism brain: separate **germline** (inherited) from **live** (working) weights.
  Simplest: `Organism` keeps `Brain` (germline, used for reproduction) and a `LiveBrain` (starts as a
  reset copy, updated each tick, used for Propagate/SelectAction). Reproduction mutates `Brain`;
  `ResetBrainState` already zeroes node state at birth — extend so offspring's `LiveBrain` = copy of
  inherited germline.
- Genome: add `Plasticity` trait (bounds, mutation, clamp, metrics, snapshot, inspector — same plumbing
  as `MetabolicEfficiency`/`Armour`).
- `MetabolismConfig`: `PlasticityUpkeep`; a `LearnRateScale` + weight clamp in a brain/learning config.

### Phasing
1. Split germline vs live weights on the organism; wire Propagate/SelectAction to the live brain and
   reproduction to the germline (behaviour identical while plasticity ≡ 0 — a safe no-op baseline to
   land first).
2. Add the `Plasticity` trait (all the usual plumbing) + the reward-modulated Hebbian update in the
   serial post-decision step + the metabolic cost.
3. Metrics/UI: mean plasticity over time; a way to see the **Baldwin effect** (e.g. in a variable /
   event-heavy world, do learners out-compete fixed brains, then does innate competence rise?).
4. (Optional, bigger) make the *learning rule itself* evolvable (evolvable neuromodulation / per-trait
   plasticity), so evolution discovers **how** to learn, not just how much.

### Open questions
- Memory/CPU: doubling brain weights per living organism at high population — measure; live weights are
  the hot path, germline is touched only at reproduction.
- Reward definition: raw energy delta is simplest; richer signals (reproduction events, damage avoided)
  are possible but risk becoming a disguised objective function — keep it a local homeostatic signal.
- Interaction with the brain-scripting seeds: a scripted seed is a germline; it would then learn on top
  of its authored starting point — a nice combination (author the instinct, evolution + learning refine
  it).

---

## Sequencing recommendation
Do **inclusive fitness accounting first** — it's low-risk (pure accounting, no determinism churn beyond
metrics) and immediately answers a real question about the current sim. **Lifetime learning** is the
larger, determinism-sensitive change (per-organism live weights, a new trait, save/reload growth);
land its no-op baseline (germline/live split at plasticity 0) before switching learning on.
