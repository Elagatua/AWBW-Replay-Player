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
