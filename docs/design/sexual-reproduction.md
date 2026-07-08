# Design plan: sexual reproduction as an unlockable trait

A planned extension that lets **sexual reproduction (biparental recombination)** evolve *into* a world
that starts fully asexual — the same "earned by selection" pattern already used for metabolic
efficiency, armour, evasion, toxicity, and plasticity. Not implemented yet; this is the design.

Like every feature it must respect the hard constraints: **determinism** (byte-identical replay /
save-reload / any thread count), **WASM/AOT/trimming friendliness** in the Core, and **no
objective/fitness function bolted onto selection** — sex has to pay for itself in surviving
descendants, or it doesn't spread.

Founders start asexual (the trait at its `Min`, exactly like the other evolved-in traits), so a
world with sex switched off is the exact behaviour we ship today: a safe no-op baseline.

> **Implementation status (2026-07-08).** Phases 1–3 below are implemented (tasks SR-1…SR-8): the
> `Sexuality` trait, the pure `Genome.Blend` / `NeatCrossover.Recombine` primitives, the `Mating` PRNG
> stream, the serial-phase mate-finding + two-parent birth path, the `SecondParentId` lineage record,
> and the sexual-vs-asexual observability. Crossover is applied in the serial intent phase rather than
> at birth-commit (it's pure and PRNG-free, so the location is behaviourally irrelevant and mutation
> still runs in birth-commit). Phase 5 (SR-9) is also done: a founding type can be seeded sexual from
> the setup screen (`BrainTypeSpec.Sexuality`), so sexual and asexual populations can be pitted directly.
> Only phase 4 — the deeper Red Queen tuning experiment — remains open; a first pass showed sex occurs
> but isn't strongly selected under the default economy (reported, not tuned toward).

---

## Why this is a good fit for the architecture

The engine turns out to be unusually friendly to this, because the hard prerequisites already exist:

- **NEAT innovation ids make brain crossover a standard, well-defined operation.** Every
  `ConnectionGene` carries an `InnovationId` (`src/LifeSim.Core/Neat/ConnectionGene.cs`). Aligning two
  brains by innovation id — matching genes vs disjoint/excess — is textbook NEAT recombination. We'd be
  *using* markers we already maintain, not adding new machinery.
- **The genome is a flat record**, so trait recombination is a field-by-field blend — trivial.
- **The germline/live split** (from lifetime learning) gives crossover a clean target: we recombine two
  **germlines** (`Organism.Germline`), never the learned live weights. Darwinian, consistent with the
  learning model.
- **"Unlocks as a trait" is a paved road.** Five traits already start at `bounds.X.Min` and only pay off
  when selection favours them; `GenomeMutator.Drift` already walks every trait in a fixed order. A new
  trait slots into the exact same plumbing (see the trait-plumbing checklist below).
- **Reproduction is already a discrete action plus a dedicated birth-commit phase.** `ResolveReproduce`
  reserves a `PendingBirth` in the serial intent phase; the mutation & birth-commit phase applies
  `GenomeMutator.Mutate` + `NeatMutator.Mutate` in ascending offspring-id order
  (`SimulationWorld.cs:426`). A two-parent path is a localized change to those two spots, not a rewrite.

---

## The reproduction model (the design decision)

**Offspring are a 50/50 mix of both parents, with a little mutation on top.** Concretely:

### Trait crossover — arithmetic blend
For every continuous genome field, the child value is the **mean of the two parents' values**
(`(a + b) / 2`). This is a true, deterministic 50/50 — no PRNG draw, no "which parent won" bias — and it
matches the intuition of a child sitting *between* its parents. Applied to the whole `Genome` record
field-by-field (including `Sexuality` itself and, when multicellular, the cell weights). `CellCount`
gets the same biased-toward-parents treatment it has today, taking the **max** of the two parents' cell
counts as the base before the existing `BiasedOffspringCellCount` nudge, so multicellularity isn't
diluted by mating with a unicellular partner.

> Alternative considered: **uniform per-gene random pick** (classic NEAT — flip a coin per field, take
> one parent's value). That's more "Mendelian" and preserves parental variance better, but it (a) spends
> PRNG per field and (b) isn't the "50/50 of the parents" the design calls for. We go with the blend;
> if variance collapse becomes a problem we can revisit per-field picking behind a config switch.

### Brain crossover — align by innovation id
Recombine the two germline `NeatGenome`s by innovation id:
- **Matching genes** (same `InnovationId` in both parents): child weight = **average of the two
  weights**; `Enabled` = true unless *both* parents have it disabled (with a small deterministic re-enable
  policy — see open questions). Node `State` is irrelevant (offspring brains are reset at birth anyway).
- **Disjoint / excess genes** (present in only one parent): **include them all** — the union of both
  parents' topology. With no fitness function we can't justify "inherit structure from the fitter
  parent," and the union keeps the 50/50 spirit (each parent contributes its unique structure). Node
  genes are the union of both parents' node sets.
- The result is then run through the **existing `NeatMutator.Mutate`** in the birth-commit phase — that's
  the "little randomness thrown in," and it reuses the exact mutation path (and innovation-id allocator)
  asexual offspring already use. No separate mutation code.

### The "little randomness"
No new mutation mechanism. After crossover produces the blended genome + recombined germline, the
normal mutation pass runs (`GenomeMutator.Mutate` for traits, `NeatMutator.Mutate` for the brain), so
sexual offspring mutate on exactly the same terms as asexual ones. Crossover changes *what gets
mutated*, not *how* mutation works.

---

## How the trait gates behaviour

New evolvable trait **`Sexuality`** ∈ [0, 1], founders at `bounds.Sexuality.Min` (= 0). It's a
*propensity to reproduce sexually*, resolved when a Reproduce action fires:

1. Organism decides to Reproduce (unchanged — the brain still chooses the action).
2. With the organism's `Sexuality` as a probability (one draw on a **new named PRNG stream**), it
   *attempts* sexual reproduction; otherwise it clones as today.
3. **Mate search**: scan the 4-neighbourhood (same N/S/E/W order as the free-tile search) for an adjacent
   organism that is *also willing* (its own `Sexuality` passes, it can reproduce, off cooldown, has
   energy). Deterministic tie-break: lowest organism id.
4. **If a mate is found**: both parents pay a (possibly shared) reproduction cost, the offspring genome +
   germline are produced by crossover (above), placed on a free tile as usual.
5. **If no willing mate is adjacent**: fall back to asexual reproduction (clone), so the action still
   succeeds and `Sexuality` degrades gracefully in a sparse world rather than wasting the turn.

Founders at `Sexuality = 0` never enter this path → today's behaviour exactly.

---

## The genuinely hard parts (and the honest risks)

1. **Mate-finding in the serial intent phase.** Asexual repro needs only a free adjacent tile; sexual
   repro needs a *willing, ready partner*, and both parents mutate shared state (energy, cooldown,
   occupancy). This must stay in the **serial** intent-resolution pass, resolved in ascending organism-id
   order, with the mate chosen deterministically (lowest id among willing neighbours). A partner already
   "consumed" as a mate earlier this tick must be marked so it isn't double-booked. This is the fiddliest
   piece.
2. **Will sex even be selected?** This is the deep uncertainty, not a bug to fix. Sex carries the
   *two-fold cost* (you need a partner and pass on only half your genes), so in a *stable* world asexual
   clones usually dominate. Sex pays under *changing* pressure — and we have the ingredients: plagues,
   blights, climate anomalies, and the toxin/armour predator-prey arms race. That's Red Queen territory,
   so there's a real chance recombination is favoured during turbulent epochs. But like plasticity it may
   be only mildly favoured, or need the environment cranked to repay it. **The experiment is the point**;
   we should not tune selection *toward* sex (that would be a disguised fitness function).
3. **Determinism budget.** A new PRNG stream (mate-choice / sexual-vs-asexual roll), sorted iteration in
   mate search, and the extra mutation-stream consumption from a second parent all shift seed-sensitive
   tests — expected, and handled the same way every prior trait was (equivalence-based flagship tests
   hold; calibration/seed-replay expectations re-baselined). Trait blend + brain crossover draw **no**
   randomness themselves; only the mate roll and the existing mutation pass do.
4. **Lineage model gains a second parent.** `LineageEntry` today has a single `long? ParentId` +
   `long LineageId`. Add a nullable **second parent id** (additive, back-compatible); keep `LineageId`
   following one parent (say the initiator) so the existing single-parent lineage tree / descendant score
   still works, with the co-parent recorded alongside for kin accounting. Relatedness is phenotype-based
   (`Kinship`), so it keeps working unchanged.

---

## Determinism / safety summary

- **Crossover is pure** (trait blend = arithmetic mean; brain align-by-innovation-id = deterministic set
  ops). No PRNG.
- **One new PRNG draw** per reproduction attempt (sexual-vs-asexual + mate choice), on a dedicated named
  stream, in the serial intent phase — never in a parallel phase.
- **Mate search** iterates a fixed neighbour order and breaks ties by ascending id; a per-tick "already
  mated" guard prevents double-booking, updated only in the serial phase.
- **Save/reload**: `Sexuality` becomes a serialized genome field (optional, back-compatible); the second
  parent id becomes an optional lineage field. No live-state growth beyond that (offspring brains are
  reset at birth, as now).

---

## Data-model changes

- `Genome`: add `Sexuality` (property + `Clamped` + `MidRange`(=`Min`) + `Random`(=`Min`, **no** PRNG
  draw so genesis consumption is unchanged) + `GenomeMutator.Drift` in source order).
- `SimulationConfig`: `TraitBounds.Sexuality = Range(0, 1)`; a `ReproductionConfig` knob or two —
  whether the cost is split across parents, and the mate-search radius (default: adjacent only).
- `PrngStream`: add a `Reproduction` (or `Mating`) stream to `PrngStreams`, seeded via `SplitMix64`
  decorrelation like the others.
- `NeatCrossover` (new, `src/LifeSim.Core/Neat/`): pure `Recombine(NeatGenome a, NeatGenome b)` aligning
  by innovation id — the one genuinely new algorithm, unit-testable in isolation.
- `GenomeCrossover` (new, or a static on `Genome`): pure `Blend(Genome a, Genome b)` — field-by-field
  mean.
- `LineageEntry`: add `long? SecondParentId` (ctor param, default null); snapshot `second_parent_id`
  optional field.
- `Organism` / `PendingBirth`: carry both parents' germlines + genomes into the birth-commit phase.
- Snapshot / schema: `sexuality` on the genome block; `second_parent_id` on the lineage block — both
  optional, back-compatible (mirrors how `metabolic_efficiency` / `founding_type` were added).
- Metrics: mean `Sexuality`; a **sexual-vs-asexual births** counter (the readout that answers "is sex
  being selected?"); trait average + histogram (standard trait plumbing).

### Trait-plumbing checklist (same as every prior trait)
Genome property + `Clamped` + `MidRange` + `Random` + `GenomeMutator.Drift` + `TraitBounds.Range` +
`GenomeSnapshot` (property/`From`/`ToGenome`) + JSON schema + `BuildMetrics` (sum var, bucket array,
accumulation, average, histogram) + `SimulationMetrics.TraitAverages` + `MetricsExporter` (header +
aligned value) + `OrganismInspectorViewModel` TraitReading + `LineageDetailViewModel` TraitReading +
`GlobalStatistics` stat row. (The trait-count assertions in `WorldViewTests` will need bumping, as with
every trait added so far.)

---

## Phasing

1. **No-op scaffold.** Add the `Sexuality` trait (all plumbing, founders at 0) and the pure
   `NeatCrossover.Recombine` + `Genome.Blend` functions with full unit tests — but keep reproduction
   asexual (the trait is inert). Behaviour byte-identical to today; determinism tests green. This is the
   safe landing, exactly like the plasticity=0 germline/live split.
2. **Wire the sexual path.** Sexual-vs-asexual roll + deterministic mate search in the serial intent
   phase; two-parent `PendingBirth`; crossover in the birth-commit phase; second parent id on the
   lineage; new PRNG stream. Re-baseline seed-sensitive tests. At this point sex *can* happen but
   founders still start at 0, so it only appears once `Sexuality` mutates up.
3. **Observability.** Mean `Sexuality` over time, sexual-vs-asexual birth counts, and — the real payoff —
   a way to watch whether sex rises during turbulent (event-heavy) epochs and falls in calm ones (the
   Red Queen signal). Inspector shows an organism's `Sexuality` and, for offspring, both parents.
4. **The experiment (optional tuning, not balance-forcing).** Run seeded worlds with heavy event
   pressure vs calm ones and see whether recombination is selected. If it never is even under strong
   Red Queen pressure, that's a *finding*, reported honestly — not something to fix by rewarding sex.
5. **UI: seed by reproduction mode? (optional).** Could let a founding "type" start sexual, the way
   founding brain-types work today — but only if step 4 shows sex is interesting enough to be worth
   seeding.

---

## Open questions

- **Cost of sex.** Split the reproduction cost across the two parents, or charge each the full cost? The
  latter models the two-fold cost more starkly (and makes sex harder to select — arguably more honest).
  A config knob, defaulted and documented, not silently chosen.
- **Disabled-gene re-enable policy.** Classic NEAT re-enables a gene disabled in one parent with some
  probability; to keep crossover PRNG-free we'd either always inherit `Enabled = (a.Enabled ||
  b.Enabled)` or make the re-enable a deterministic rule. Pick the simplest that behaves; document it.
- **Blend vs per-gene pick.** The blend collapses parental variance faster than Mendelian assortment
  would. If populations converge too readily, expose per-field random inheritance as an alternative — but
  that costs PRNG and departs from the "50/50" brief, so only if measured to matter.
- **Assortative mating / compatibility.** Should mates be gated by relatedness or trait similarity (a
  crude speciation signal), or is "any willing adjacent organism" enough? Start with the simplest (any
  willing neighbour); revisit only if it produces nonsense hybrids.
- **Interaction with founding brain-types.** A cross between two different seeded types produces a genuine
  hybrid brain — interesting, but muddies the `FoundingType` label (which breeds true today). Decide
  whether a hybrid keeps the initiator's label or gains a "hybrid" marker.

---

## Sequencing recommendation

Land **phase 1 (no-op scaffold + pure crossover functions with unit tests) first** — zero behavioural
change, zero determinism churn beyond the new inert trait, and it de-risks the one genuinely new
algorithm (`NeatCrossover`) in isolation before it touches the tick loop. Phase 2 is the
determinism-sensitive step (new PRNG stream, serial mate-finding, two-parent births); treat it the way
the lifetime-learning switch-on was treated. Everything after that is observation and experiment — and
whether sex is actually selected is a result to discover, not a target to hit.
