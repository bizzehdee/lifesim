# LifeSim — The Rules

A plain-language tour of the laws that govern the world — the high-level "rules of the game".

By default everything is **reproducible**: the same seed and settings replay exactly, tick for tick,
on any machine and at any thread count. (You can opt into **entropy mode**, where the map is still
seeded but each run's *life* — behaviour, mutation, combat, events — is drawn fresh, so the same seed
gives the same world with different history every time.) Either way, organism behaviour itself is
genuinely random: actions are sampled, mutation is random, combat is a roll — nothing is scripted.

---

## 1. The world
- The map is a grid of tiles reconstructed from the world **seed** — no two seeds look alike, and terrain is never stored, only regenerated.
- Every tile belongs to one of four **biomes**, each with its own temperature, friction (movement cost), and food: **Grassland** (balanced), **Desert** (hot, sparse food), **Swamp** (wet, rich, sticky), **Ice Sheet** (freezing, harshest — only a minimal food trickle). Deserts and ice sheets yield only a little energy — not enough for a fat generalist, but enough that a cold-adapted, frugal lineage can scrape a living there (see rules 3 and the metabolic-efficiency trait). Temperatures are **smoothed across biome borders into gradients** rather than hard walls, so an organism at a margin can adapt into a neighbouring biome incrementally.
- Each tile holds a **ground energy pool** that regrows toward its biome's cap every tick. This is the base of the food web.

## 2. Organisms
- An organism occupies **one tile**. It has a **genome** (its inherited traits), a **brain** (an evolvable neural network), and an **energy budget**.
- The founding population is a **varied gene pool** — each founder is spawned with randomised traits (kept viable for its starting biome), not identical clones — so the world is diverse and unpredictable from the first tick.
- Founders' brains are normally random, but you can **seed brain "types"**: at setup, mix in any number of hand-authored personalities (selfish, fearless, …) written as a short weighted-preference script that compiles into a starting brain. A type is only a *starting point* — from tick 0 it mutates and competes exactly like a random brain, so survival of the fittest, not the script, decides which types win. Each lineage keeps a cosmetic label of the type it descended from, so you can watch the mix shift.
- Energy runs from 0 to a ceiling (100 by default). **Energy hits zero → the organism dies** and is removed. There is no other automatic death unless aging is switched on (see rule 7).
- Traits include body **size**, **speed**, a **thermal comfort band** (centre ± width), **sensing radii** and **acuity**, **metabolic efficiency** (see rule 3), **generosity**, and a multicellular **body plan** (see rule 9).

## 3. Energy is a strict budget
Every action and condition is an energy transaction. Each tick an organism pays for:
- **Base metabolism** — proportional to its body mass.
- **Thermal stress** — free inside its comfort band, growing the further the tile's temperature strays outside it.
- **Sensory tax** — perception is expensive; wider/sharper senses cost more (moving-agent vision costs more than static terrain sensing).
- **Movement** — proportional to distance and speed², scaled by biome friction.
- **Crowding** — a small cost per neighbour in its 3×3 block, so packed regions self-thin (a soft carrying capacity).

**Metabolic efficiency is an evolvable trait.** A lineage can evolve to spend less energy on its own upkeep, sensing, and movement — approaching, but never reaching, zero (a thermodynamic floor). The trade-off follows the real rate–yield law: a frugal metabolism **extracts less usable energy per graze**, so frugality pays off where food is scarce (deserts, ice) but loses to fast full-yield generalists in rich grassland and swamp. Founders start at baseline and must evolve frugality up.

It **gains** energy only by feeding (rule 5).

## 4. The tick — how time passes
Every tick resolves in a fixed order: **sense → decide → act → pay metabolism → remove the dead → regrow ground → commit newborns → record stats**. When two organisms want the same thing, the lower id resolves first. This strict ordering is what makes runs reproducible.

## 5. Sensing, brains, and actions
- **Sensing** gives each organism a snapshot of its surroundings — local biome, temperature, its own energy, nearby organisms (and how *related* they are), reproductive readiness, and a global stress signal. Low acuity blurs these readings with noise.
- The **brain** is a recurrent neural network that turns senses into one chosen action. Its structure is **not fixed** — it starts minimal (senses wired straight to actions) and **evolves new neurons and connections** over generations. Deeper, cleverer networks only persist if they help their lineage survive.
- The 15 possible **actions**: move (N/S/E/W), harvest (self or a neighbour tile), idle, reproduce, and share (N/S/E/W).

## 6. Feeding and combat
- **Harvest an empty tile** → graze its ground energy.
- **Harvest an occupied tile** → attempt predation. The kill chance is `attacker_mass / (attacker_mass + victim_mass)` — bigger bodies win more often but never with certainty. A kill transfers most of the victim's energy; a failed attack costs the attacker a retaliation penalty.

## 7. Life cycle & evolution
1. **Reproduction is asexual**: it costs energy proportional to body mass, is gated by a cooldown, and places the offspring on a free adjacent tile.
2. Offspring **inherit the parent's genome and brain, with mutation** — traits drift a little, and the brain may gain or lose structure.
3. There is **no designer-set fitness**. What survives and breeds, spreads. Selection is entirely emergent from the energy economy.
4. Every organism gets a **name** and a tracked **lineage** (parent, generation depth, descendants).
5. **Senescence** (aging) is on by default: past an onset age, an organism pays a metabolic tax that grows with age, so no lineage is immortal. It can be switched off per world.

## 8. Environmental shocks
Rare, world-level events punish over-specialisation and monocultures:
- **Resource blight** — ground energy stops regrowing in a biome for a while.
- **Density plague** — crowded regions bleed energy, hitting dense clone-clusters hardest.
- **Climatic anomaly** — a heatwave or ice age shifts temperatures, turning safe ground lethal.

## 9. Cooperation (optional — on by default)
- Organisms can **sense how related** a neighbour is and **share energy** with it. Sharing loses a little in transfer (altruism is genuinely costly).
- **Generosity is an evolvable trait** — lineages can drift toward hoarding or over-sharing, whichever pays off.
- Sharing is **relatedness-scaled**: close kin are helped usually (not always), strangers rarely (but possibly). Cooperation tends to emerge inside families, not between strangers.

## 10. Multicellularity (optional — on by default)
- A body can evolve to be made of **many cells** (it still occupies one tile). Cells specialise into six jobs: **Germ** (reproduction), **Feeder** (food yield), **Store** (energy capacity), **Defender** (combat + thermal resistance), **Mover** (cheaper movement), **Sensor** (sharper senses).
- **The square-cube law limits size**: upkeep grows with a body's volume (∝ cells) but feeding is limited by its surface — so a bigger body runs an ever-larger per-cell deficit and starves… *unless it divides its labour*.
- **Division of labour outweighs the square-cube law**: a body drawing on several *distinct* specialist types runs far cheaper, feeds closer to its full size, and reproduces almost as cheaply as a single cell — so well-differentiated bodies can grow large while lopsided or generalist ones stay small.
- Only bodies with enough **germ** cells can reproduce; a body that abandons germ becomes sterile **soma**.
- **Bigger bodies graze a wider footprint**: a larger body skims ground energy from surrounding tiles (its reach grows with cell count), so it pulls energy from more of the surface. Because it drains a whole area, it crowds out smaller neighbours and tends to **hold territory** — an emergent consequence, not a scripted rule.
- **More cells also mean more brain**: a larger body runs extra neural-processing steps per tick, so it thinks more deeply before acting.
- Founders start as single cells; offspring of multicellular parents **tend toward multicellularity**, so once the transition happens it reinforces itself.

---

## What you can set per world
Seed, map size, starting population, and any tuning constant — plus toggles for **cooperation**, **senescence**, and **multicellularity**, and the number of **threads** the engine uses (which never changes the result, only the speed).
