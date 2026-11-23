# The AWBW replay format

Parsed by [`AWBWJsonReplayParser`](../AWBWApp.Game/API/Replay/AWBWJsonReplayParser.cs)
(current format) and [`AWBWXmlReplayParser`](../AWBWApp.Game/API/Replay/AWBWXmlReplayParser.cs)
(legacy `.awbw` XML, kept for old downloads).

## Container

A replay is a **zip** holding **two gzip-compressed members**:

| Member | Conventional name | Content |
|---|---|---|
| Game state | `<gameId>` | 1 PHP-serialised `awbwGame` object per turn, newline separated |
| Actions | `a<gameId>` | 1 line per turn, newline separated |

The parser **identifies members by their content prefix, not their filename**
([AWBWJsonReplayParser.cs:77](../AWBWApp.Game/API/Replay/AWBWJsonReplayParser.cs#L77)):

```csharp
private const string turn_start_text   = "O:8:\"awbwGame\":";   // -> game state file
// anything starting with "p:"                                   // -> action file
```

Entries whose path contains `__MACOSX`, or whose compressed length is 0, are
skipped. Two members of the same kind is an error ("contains multiple replays").
A gzip member that fails to decompress but starts with `1f 8b` raises
`CorruptedReplayException`.

`ParseReplayZip` sets `ReplayInfo.ReplayVersion = 2`. A bare single gzip file
parsed through `ParseReplayFile` gets version `1` and has **no actions at all** —
only turn snapshots. Several behaviours are gated on `ReplayVersion >= 2`.

## Game state file

One `awbwGame` object per line, each a **start-of-turn snapshot** of the whole
board. A 34-turn game has 34 lines and exactly one trailing newline.

```
O:8:"awbwGame":36:{s:2:"id";i:1680558;s:3:"day";i:1; ... }
O:8:"awbwGame":36:{s:2:"id";i:1680558;s:3:"day";i:1; ... }
...
```

Nested object types:

| Prefix | Meaning |
|---|---|
| `O:8:"awbwGame":<n>:{...}` | one turn |
| `O:10:"awbwPlayer":<n>:{...}` | a player's per-turn record |
| `O:12:"awbwBuilding":<n>:{...}` | one building |
| `O:8:"awbwUnit":<n>:{...}` | one unit |

### PHP serialisation rules that matter

- `s:<n>:"..."` — **`<n>` is a UTF-8 *byte* count, not a character count.**
  `readString` ([AWBWJsonReplayParser.cs:1418](../AWBWApp.Game/API/Replay/AWBWJsonReplayParser.cs#L1418))
  walks characters while subtracting their encoded byte length. Any tool that
  rewrites a string in a replay must recompute the byte length.
- `a:<n>:{...}` — `<n>` is the element count and must match exactly.
- `O:<len>:"<class>":<fieldCount>:{...}` — `fieldCount` must match.
- Integers are `i:`, floats are `d:`, booleans `b:0`/`b:1`, null is `N;`.
  **Hit points are `d:`** even when whole (`d:10`), which trips naive regexes
  that only look for `i:`.

### Fields the parser refuses to see change mid-game

`readTurn` and `readPlayers` throw `Data '<x>' changed per turn when not
expected.` if any of these differ between turns:

Game level: `id`, `password`, `creator`, `maps_id`, `funds` (per building),
`starting_funds`, `weather_type`, `fog`, `use_powers`, `official`, `league`,
`team`, `start_date`, `capture_win`, `type`.
Per player: `id`, `users_id`, `team`, `countries_id`, `order`, plus the set of
COs that player uses (`co_id` / `tags_co_id`)
([AWBWJsonReplayParser.cs:210–744](../AWBWApp.Game/API/Replay/AWBWJsonReplayParser.cs#L210)).

An unrecognised key anywhere throws `Replay contained unknown entry: <key>`. The
format is enumerated exhaustively; a new AWBW field breaks parsing loudly rather
than being ignored.

## Action file

One line per turn:

```
p:<playerId>;d:<day>;a:a:3:{i:0;i:<playerId>;i:1;i:<turnNo>;i:2;a:<count>:{i:0;s:<byteLen>:"<json>";i:1;s:...;}}
```

- The `a:` after `d:<day>;` is a literal prefix; the real array declaration is the
  `a:3:{` that follows — hence the parser constant `action_start_text = "a:a:3:{"`.
- Member `i:0` repeats the player id, `i:1` is the turn number, `i:2` is the
  array of actions.
- Action indices must be `0..count-1` **in order**, or the parser throws
  `Out of Order actions`
  ([AWBWJsonReplayParser.cs:1371](../AWBWApp.Game/API/Replay/AWBWJsonReplayParser.cs#L1371)).
- Each action is a JSON document stored as a PHP string, so its byte length must
  be right.

A parse failure inside one action is wrapped as
`Failed to parse Replay Action #i on Turn t. Day d, Active Player p` — useful,
because it names the exact action.

## How the two files are matched

Not by index. For each action line the parser scans `TurnData` for the first turn
whose `ActivePlayerID` **and** `Day` both match
([AWBWJsonReplayParser.cs:1316](../AWBWApp.Game/API/Replay/AWBWJsonReplayParser.cs#L1316)):

```csharp
foreach (var turn in replayData.TurnData)
{
    turnIdx++;
    if (turn.ActivePlayerID != playerID || turn.Day != day) continue;
    turnData = turn;
    break;
}
```

So an added or reordered action line binds to whichever snapshot matches that
`(player, day)` pair — and in a tag match, where two turns can share a day, the
*first* match wins.

## Action JSON

Every action is `{"action": "<Code>", ...}`. `ReplayActionDatabase` resolves the
code to an `IReplayActionBuilder` by reflection over all implementations
([ReplayActionDatabase.cs:15](../AWBWApp.Game/API/Replay/ReplayActionDatabase.cs#L15)).
An unknown code throws.

| Code | Builder | Notes |
|---|---|---|
| `Move` | `MoveActionBuilder` | The base movement action |
| `Fire` | `AttackUnitActionBuilder` | Wraps an optional `Move` |
| `Capt` | `CaptureBuildingActionBuilder` | Wraps an optional `Move` |
| `Build` | `BuildUnitActionBuilder` | |
| `Load` | `LoadUnitActionBuilder` | Wraps an optional `Move` |
| `Unload` | `UnloadUnitActionBuilder` | Accepts a wrapped `Move`, but AWBW does not emit one — see below |
| `Supply`, `Repair`, `Join`, `Delete`, `Hide`, `Unhide`, `Explode`, `Launch`, `AttackSeam` | | |
| `Power` | `PowerActionBuilder` | CO power / super power |
| `Tag` | `TagActionBuilder` | Tag CO switch |
| `End` | `EndTurnActionBuilder` | Always last; see below |
| `Resign`, `Eliminated`, `GameOver` | | |

### Wrapping

Composite actions embed their sub-action verbatim and delegate parsing:

```json
{"action":"Fire","Move":{"action":"Move", ...},"Fire":{"combatInfoVision": ...}}
```

`CaptureBuildingAction.SetupAndUpdate` calls `MoveUnit?.SetupAndUpdate(...)`
first, then applies its own effect — and `UndoAction` reverses that order.

`Fire`, `Capt`, `Load` and `Unload` all *accept* a wrapped `Move`. In practice
AWBW emits one for the first three but **not** for `Unload`: a transport that
moves and then unloads produces two consecutive actions, a `Move` followed by an
`Unload`. Code that walks the stream looking for "the move that belongs to this
unload" has to look at the previous action, not inside this one.

### `End` is two different actions

`EndTurnActionBuilder` inspects `updatedInfo.event`
([EndTurnAction.cs:28](../AWBWApp.Game/API/Replay/Actions/EndTurnAction.cs#L28)):

- `"NextTurn"` → an `EndTurnAction` carrying `nextPId`, `day`, `nextFunds`,
  `nextWeather`, `supplied`, `repaired`.
- `"GameOver"` → it returns a **`GameOverAction` instead**, built from `message`
  and `playersElim` (`"Y"` = eliminated → loser, anything else → winner).
- Anything else throws.

So the cleanest way to end a synthesised replay is an `End` with
`event: "GameOver"`.

## Player-keyed payloads

Most sub-objects are keyed by audience, resolved by
`ReplayActionHelper.GetPlayerSpecificDataFromJObject`
([ReplayActionHelper.cs:16](../AWBWApp.Game/API/Replay/ReplayActionHelper.cs#L16)):

```
jObject["<playerId>"]  ??  jObject["<teamName>"]  ??  jObject["global"]
```

This is how AWBW ships each observer a fog-appropriate view of the same event. A
`Move`'s `unit` and `paths` are read **only for the active player**; a `Fire`
reads both the attacker's and the defender's `combatInfoVision` and throws
`Replay contains fight that player has no vision on` if either `hasVision` is
false.

`PowerAction` is the exception: it iterates **every** player in the replay and
looks up `unitReplace` for each, so a `unitReplace` object missing a key for some
player will fail. Supplying the same list under every player id is the safe form.

## Fog data

Two separate mechanisms, often confused:

1. **`hasVision` / `vision` / `combatInfoVision`** — per-audience flags that
   decide whether a given observer sees an event. Mostly consumed to decide
   whether to play an animation.
2. **`discovered`** — what the acting player newly learned. Parsed by
   `DiscoveryCollection` ([DiscoverAction.cs:9](../AWBWApp.Game/API/Replay/Actions/DiscoverAction.cs#L9)).

`DiscoveryCollection` reads **only the `"buildings"` key**. Any `units` present in
a `discovered` block are ignored entirely. The effect of a discovery is limited
to per-team building knowledge (`BuildingKnowledge` in setup,
`DrawableBuilding.TeamToTile` at playback) — i.e. which country's version of a
property a given team believes is there.

A non-empty JSON **array** for `discovered` throws (`Non-Empty Array for
Discovery`); empty array and `null` are both accepted as "nothing".

## Hit points in the data

The single most surprising property of the format:

| Location | Representation |
|---|---|
| State snapshot (`units_hit_points`) | PHP double, one decimal: `d:9.4` |
| Action stream (`"units_hit_points"`) | integer: `9` |

AWBW tracks HP internally on a 0–100 scale and writes it back as *x*.*x* out of
10. The action stream's integer is `ceil()` of the exact value, which is also
what the game UI shows. The replay file therefore reveals information the
original players could not see during the match — but only at turn boundaries,
since mid-turn you only have the integers the actions report.

`<= 0` means the unit is gone; see [game-rules.md](game-rules.md#hit-points).

## Writing a modified replay

Checklist, learned the hard way:

- Re-gzip each member and re-zip; member names are cosmetic but the content
  prefixes are not.
- Recompute every `s:<n>:` byte length you touched, and every `a:<n>:` count.
- Keep action indices contiguous from `0`.
- Keep exactly one trailing newline on each of the two files.
- Bump the game id in **both** members if you want it to coexist with the
  original (`ReplayManager` stores by `<gameId>.zip`). A global integer-context
  replace of the old id is safe as long as the digit count is unchanged —
  otherwise surrounding string lengths shift.
- A new turn generally needs **no new state line**: the final turn's snapshot is
  never compared against anything, because `FinishTurnAndCheckForDesyncs` runs
  for turn *i* against turn *i+1* and so never runs for the last turn.
