# Damage calculation

This is the **one** place in the program that computes a game rule instead of
reading an outcome the replay already states. It exists to answer a question the
replay cannot: not *what happened*, but *what would happen* if these two units
fought. Nothing here is ever consulted to decide what did happen — see
[game-rules.md](game-rules.md).

Code lives in [`AWBWApp.Game/Game/Damage/`](../AWBWApp.Game/Game/Damage/); the
tables live in
[`AWBWApp.Resources/Json/DamageChart.json`](../AWBWApp.Resources/Json/DamageChart.json).

## Where the numbers come from

`DamageChart.json` is transcribed from **XHKOCalc**, the community multi-hit KO
calculator (by *kilmefirst*, based on work by *Operation Mechstorm*). That
spreadsheet is the reference this project trusts for combat, in preference to the
CO power values in `COs.json` — those are used for movement and vision, are not
consulted by any damage code, and at least Kindle's disagree with the AWBW wiki.

Where AWBW deviates from the cartridge games, the
[AWBW wiki's changes page](https://awbw.fandom.com/wiki/Changes_in_AWBW) is the
authority. The deviations that matter here are already baked into the charts.

**Do not hand-edit `DamageChart.json`.** It is generated. If the charts need to
change, change the spreadsheet and regenerate.

## The formula

With every modifier expressed in **tenths** — `1` means `+10%`:

```
damage = trunc( (base × (1 + attack/10) + luck)
                × (attackerHP / 10)
                × (1 − stars/10 × defenderHP/10 − defence/10)
                + 0.05 )
```

Four things about this are easy to get wrong:

- **`base` is a percentage** and the result is on the **0–100 scale** the replay
  stores, not the 1–10 the game shows. `55` is five and a half hearts. Dividing
  by ten is a presentation decision and is left to the caller.
- **Hit points are the displayed whole numbers**, 1–10, for both sides. A unit
  the replay has at 9.1 hits exactly as hard as an untouched one, and is defended
  exactly as well. The fractional HP work does not feed into this.
- **Terrain protects in proportion to how much of the defender is left.** The
  `stars × defenderHP` product is why a wounded unit on a mountain is so much
  easier to finish off than a fresh one.
- **That `+0.05` before truncation is real.** It is in the spreadsheet and it
  changes outcomes at the boundaries. It is not a rounding nicety to tidy away.

## What feeds each term

| Term | Source |
|---|---|
| `base` | `BaseDamage[weapon][defender]`. A missing cell means that weapon cannot touch that unit *at all* |
| `attack` | `COAttack[coState][unit]` + `COTerrainAttack[coState][terrain]` + com towers |
| `defence` | `CODefence[coState][unit]` + `COVsIndirectDefence[coState]` when the attacker is indirect |
| `stars` | The defender's tile — a building covers the tile beneath it |
| `luck` | A roll from `LuckRolls[coState]` |

A **CO state** is the key the charts file a CO under: `Grit`, `Grit-COP`,
`Grit-SCOP`. Every power row already carries the universal **+10% firepower and
defence** that any active CO power grants in AWBW — do not add it a second time.
A CO with no row at all, `No CO` included, simply has no modifiers.

Com towers are worth **+10% attack each, to the attacker only**. They do not
defend; the spreadsheet is right about this and it is easy to assume otherwise.

## Weapon selection

A unit reaches for its **primary** (the one that needs ammunition) whenever it
has shells *and* the chart says that weapon can hit the target. Failing either,
it falls back on its **secondary** — the machine gun, listed as a separate
`<unit>-Secondary` row. A unit with neither, such as an artillery out of
ammunition, cannot attack.

Units with `MaxAmmo == 0` (infantry, recon) have unlimited ammunition, so an ammo
count of zero means nothing to them.

The chart, not `UnitData.SecondWeapon`, decides whether a secondary exists.
Piperunner carries `SecondWeapon: true` in `Units.json` but has no secondary row,
so a spent piperunner cannot attack.

## Luck

`LuckRolls` maps each luck value to **how many ways it can come up**, not to a
probability. Most COs roll a flat 0–9, but Jugger, Flak and Sonja roll good luck
and bad luck separately, so their spreads are weighted — Jugger's `0` is fifteen
times likelier than his `-14`. Treating these as uniform over the range would be
wrong.

A CO with no entry of its own rolls standard luck.

## Terrain

[`DamageTerrain`](../AWBWApp.Game/Game/Damage/DamageTerrain.cs) maps a tile to the
name the charts use. This is **finer than `TerrainType`**: the charts tell a
bridge from a road (Koal's bonus applies to one and not the other) and a reef
from open sea. Two tiles are separated by things other than their type:

- A **reef** is the only stretch of sea with defence stars.
- A **spent missile silo** is a `Plain` with three stars.
- A **bridge** matches a road in every field but its name, which is why
  `TerrainTile.Name` exists at all.

Only Jake, Kindle, Koal and Lash have a terrain row; everyone else gets nothing.

## The exchange

`ResolveExchange` resolves a whole fight: the attack, the counterattack it
invites, and the spread of hit points each unit is left holding. It takes hit
points on the **0–100 scale**, which is what lets the same fight be resolved
twice — once from what the board shows, once from what the replay really records.

Two rules govern the counterattack:

- **Only direct combat trades blows.** An artillery firing from three tiles away
  takes nothing back, and cannot fire back at something already standing next to
  it. So a counter happens only when *neither* unit is indirect.
- **Nothing hits back from the grave**, and the formula agrees on its own: a unit
  at zero hit points swings at zero strength.

The attacker's resulting spread is a mixture — for each way the attack can land,
the counter is recomputed from whatever the defender has left, because that
changes how hard it hits back.

### Where the attack is fired from

A direct unit almost never fires from where it is standing: it walks up to its
target first, and the tile it stops on is the ground that defends it when the
counter lands. So the terrain the attacker's side of a situation carries is **the
tile the attack is launched from**, not the tile the unit currently occupies.

The gesture lets the cursor choose it.
[`AttackApproach`](../AWBWApp.Game/UI/Replay/AttackApproach.cs) takes whichever
tile beside the target the cursor last crossed, and the indicator draws the line
in two segments so that the bend shows which tile that was. Where the cursor
never crossed one, it falls back to the nearest side of the target.

Indirects are left alone. They cannot move and fire in the same turn, so there is
nowhere for the line to bend, and the attacker's own tile is the right answer.

Nothing here asks whether the unit could really get there — see the top of this
page. A mountain in the way does not make the question less worth answering.

## Hidden hit points

A unit's real hit points run 0–100 and the board shows a tenth of that, rounded
up. The exact figure is hidden until the replay is over — but it is not
*unknowable*, and [`HitPointBelief`](../AWBWApp.Game/Game/Damage/HitPointBelief.cs)
is what can be worked out about it, as a probability over every value it could be.

The reason this works: the damage formula runs off the hit points both units
**display**, which anyone watching can see. So the rolls an attack could have made
are public. Only which roll landed is hidden, and the figure shown afterwards is
evidence about that.

A tank fresh from a base is exactly 100. Shot by another tank for 55–58, it is
somewhere in **42–45** — not the 50 that reading its displayed 5 off the board
would suggest. That gap is often the difference between a follow-up killing and
leaving a sliver.

The tile info popup can show this too: **HP in Tile Info** offers the nominal
figure the board shows, the possible range (two rows, most and least), or the
true figure the replay records.

[`HitPointBeliefTracker`](../AWBWApp.Game/Game/Damage/HitPointBeliefTracker.cs)
carries this through the setup pass. Three things narrow a belief and one widens
it:

- **An attack** spreads it across that attack's damage rolls, and across the
  counter's where one was invited. The counter follows the same rule as the
  exchange above: no counter when either side is indirect. Taking one that never
  happened is worse than taking none at all, because it lands the belief
  somewhere the board contradicts, and the only way out of a contradiction is to
  forget the unit entirely.
- **The displayed figure afterwards** rules out everything it contradicts.
- **A repair** shifts it by exactly 20 and clamps at 100 — so anything from 81 up
  lands exactly on full health, which is the only operation that *removes*
  uncertainty. It is a plain addition with no rounding, which is worth stating
  because the home games round up first: across ten real replays, repairs left
  units on every last digit, not just on zero.
- **Anything else that moves a unit's hit points** throws the belief away and
  starts again from the displayed figure.

That last one is not a shortcut, it is the safety net, and it is load-bearing.
Small changes do not shift the number on the board at all — a Mega Tank in the
bundled replay quietly drops from 100 to 98 — so ruling values out would never
catch them. Anything the tracker cannot account for, it admits to.

For the same reason the tracker **refuses to model an attack when it does not
know the terrain**. A damage spread computed against the wrong ground is worse
than none: it is confidently wrong, and conditioning on the displayed figure only
catches errors large enough to cross a tenth.

### Rounded inside a turn, exact at its head

A replay reports hit points two different ways, and mixing them up is the trap
that this feature already fell into once.

- **Inside a turn**, every action reports the figure the board shows: a whole
  number, rounded up. A unit really holding 78 is reported as `8`.
- **A turn's opening snapshot** carries the real value, `7.8`.

So any comparison that spans the boundary has to round *both* sides first.
Comparing them raw makes every damaged unit in the game look like it changed on
every turn, which fires the safety net above, which throws away everything that
had been worked out. The symptom is a tracker that always answers with the whole
tenth — which reads as one that is simply not very clever, rather than as a bug.
`TestAnAttackNarrowsTheRangeAndTheNextTurnKeepsIt` pins it.

### The one reading of the real figure

The snapshot is also the only place the replay writes the true hit points down,
and the tracker reads them for exactly one purpose: to notice that a belief is
**already wrong**, and throw it away.

It never works anything out from them. The difference is the whole trap below —
inference would make this silently perfect, while refusing to hold a contradicted
belief only stops it lying. It earns its place because the tooltip prints the
tracked range and the true figure *on the same line*: a range that does not
contain the number beside it is not a cautious answer, it is a visibly wrong one.

### The trap

This runs in the setup pass, where the exact hit points are sitting in the same
context it reads from. Only two things are ever taken off a unit: the figure the
board shows, and *that* it changed. Never by how much. One careless line would
make the tracker silently perfect, and perfect output looks like success.

So `TestSceneHitPointBeliefs` tests it from the adversarial direction. It asserts
**containment** — every figure the replay privately records falls inside the range
allowed — and then, because a cheating tracker would pass that perfectly, it also
asserts the tracker is honestly *uncertain* about most damaged units. The exact
arithmetic is pinned separately by `TestHitPointBelief`.

Both halves earned their keep: the containment test failed on its first three runs
and found three real bugs, including the Mega Tank above and a repair the tracker
was recording in the wrong place.

## State-dependent bonuses

Two CO abilities cannot live in a chart, because they depend on how the match is
going rather than on who is fighting. Both are read from the replay:

| Ability | What it does |
|---|---|
| **Colin — Power of Money** | +3% attack per 1000 funds held, under the super only |
| **Kindle — High Society** | +3% attack per **city** held, on top of her +130% urban bonus, under the super only |

Kindle counts **cities, not all properties** — an AWBW-specific change from the
home games, per the wiki's changes page.

Colin's scales **continuously** (`funds / 1000 × 3%`), so a partial thousand pays
out its share — 17,500 in the bank is +52.5%, not +51%. Confirmed against the
calculator on the AWBW website.

## What is not modelled

**Sonja — Counter Break.** Her super makes the defender strike *first*, so the
attacker swings at already-reduced hit points. That is an ordering change rather
than a modifier, and `ResolveExchange` resolves strictly attack-then-counter.

**Pipe seams** attack using the Neotank row and are not affected by luck. Seams
are buildings rather than units, so nothing reaches this code with one yet.

### The one number not pinned to the spreadsheet

Sonja's day-to-day gives counterattacks **1.5× damage**. The spreadsheet models
no counterattacks at all, so unlike everything else here that multiplier has no
reference value behind it. It is applied as firepower, alongside the CO bonuses
and before luck, which is the reading that matches *"counterattacks do 1.5x
damage"* — but it is a reading, and worth verifying against a real replay before
anyone relies on it.

## Regenerating the chart

The extractor is not checked in; the spreadsheet is the source. An ODS file is a
zip of XML, so the formulas can be read directly rather than guessed at from
cached values — which is how the formula above was recovered. Unzip
`content.xml` and read:

| Sheet | Rows | Holds |
|---|---|---|
| `Data` | 2–34 | Base damage: attackers down column B, defenders across row 2 |
| `Data` | 47–60 | Luck ranges; the weighted distributions run from row 46 across column G on |
| `Data` | 62–79 | Terrain defence stars — checked against `BaseDefence`, never copied |
| `dmgcharts` | 3–86 | CO attack, in tenths |
| `dmgcharts` | 89–101 | CO terrain bonuses |
| `defchart` | 3–86 | CO defence |
| `defchart` | 90–92 | Javier against indirects |
| `Detailed stats` | row 4, cols Z–AI | The formula itself, once per luck value |

Unit names need mapping on the way in: the spreadsheet says `Medium Tk`,
`Megatank`, `B-Ship`, `B-Boat`, `B-Bomb`, `Missiles`, `Rockets`; this codebase
says `Md.Tank`, `Mega Tank`, `Battleship`, `Black Boat`, `Black Bomb`, `Missile`,
`Rocket`.
[`TestDamageCalculator`](../AWBWApp.Game.Tests/StorageTests/TestDamageCalculator.cs)
fails loudly if that mapping ever drifts.

## Tests

`TestDamageCalculator` pins the maths to the worked example XHKOCalc ships with:
a Sami tank at 5HP holding one com tower, firing at an Andy tank at full health in
a forest. The spreadsheet's own rolls for that attack are
`22, 22, 22, 23, 23, 24, 24, 24, 25, 25`. If a transcription ever drifts, that is
the test that catches it.
