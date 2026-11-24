# The action pipeline

Everything that happens in a replay is an `IReplayAction`. There are 21 of them
in [`API/Replay/Actions`](../AWBWApp.Game/API/Replay/Actions).

## The contract

```csharp
public interface IReplayAction
{
    bool SuccessfullySetup { get; set; }

    string GetReadibleName(ReplayController controller, bool shortName);

    void SetupAndUpdate(ReplayController controller, ReplaySetupContext context);
    bool HasVisibleAction(ReplayController controller);

    IEnumerable<ReplayWait> PerformAction(ReplayController controller);
    void UndoAction(ReplayController controller);
}
```

Each action also has a paired `IReplayActionBuilder` that owns the `Code` string
and turns a `JObject` into the action. Builders are instantiated by reflection
(`ReflectionHelper.GetAllUniqueInstancesOfClass`), so adding a new builder class
is all the registration needed.

### Four phases

| Phase | When | Operates on | Rules |
|---|---|---|---|
| **Parse** (`ParseJObjectIntoReplayAction`) | Once, at load | `JObject` | Pure. No controller state exists yet. Throw on anything unrecognised. |
| **Setup** (`SetupAndUpdate`) | Once, for every action, before playback | `ReplaySetupContext` | Record "before" state for undo; mutate the context so later actions see the effect. **Do not touch the scene graph.** |
| **Perform** (`PerformAction`) | When the user steps forward onto it | `GameMap` drawables | Animate. `yield return ReplayWait...` between beats. |
| **Undo** (`UndoAction`) | When the user steps backward off it | `GameMap` drawables | Exact inverse of Perform, using the state captured during Setup. |

`HasVisibleAction` lets the UI skip actions that are entirely under fog for the
current view.

**Setup happens once per action instance, and cannot be repeated.** The "before"
state an action records is written to its own fields, and nothing clears them, so
a second pass over the same action adds to collections that are already full and
throws. Anything that needs the whole replay set up again — a CO variant
changing, say — has to re-read the replay to get fresh action instances rather
than calling the pass twice. Perform and Undo, by contrast, are meant to run many
times, which is why they must be exact inverses.

### Reading an action: `MoveUnitAction`

[MoveAction.cs](../AWBWApp.Game/API/Replay/Actions/MoveAction.cs) is the template
worth copying. Setup:

```csharp
public void SetupAndUpdate(ReplayController controller, ReplaySetupContext context)
{
    if (!context.Units.TryGetValue(Unit.ID, out var unit))
        throw new ReplayMissingUnitException(Unit.ID);

    originalUnit = unit.Clone();          // 1. snapshot for undo
    unit.Overwrite(Unit);                 // 2. apply to the context

    if (context.Buildings.TryGetValue(originalUnit.Position!.Value, out var belowBuilding)
        && belowBuilding.Capture != 20)
    {
        belowBuildingHP = belowBuilding.Capture;   // 3. side effects, also snapshotted
        belowBuilding.Capture = 20;                //    (leaving a tile resets its capture)
    }

    if (Discovered != null)
        context.RegisterDiscoveryAndSetUndo(Discovered);
    ReplayActionHelper.UpdateUnitCargoPositions(context, unit, unit.Position!.Value);
}
```

Note the pattern: *clone before mutate*, and every mutation has a matching stored
original. `UndoAction` then simply replays those originals onto the drawables.

### `ReplayUnit.Overwrite` is a merge, not an assignment

```csharp
HitPoints = other.HitPoints ?? HitPoints;
```

Every field is nullable and only overwrites when present. Action payloads
therefore carry **partial** units — a `Move` sends position, fuel and
`units_moved` but not, say, ammo. This is why `originalUnit` must be a `Clone()`
of the context's unit and not of the action's `Unit`.

The one non-nullable-merge field is `CargoUnits`, which is replaced wholesale
when present.

## Composite actions

`Fire`, `Capt` and `Load` may embed a `Move`. The outer builder delegates:

```csharp
var moveAction = Database.ParseJObjectIntoReplayAction(moveData, replayData, turnData);
action.MoveUnit = moveAction as MoveUnitAction;
```

and the outer action calls through in setup/perform, reversing the order in undo.
`Fire`, `Capt`, `Load` and `Unload` all support this. `Unload` is the odd one out
in practice: AWBW emits the transport's movement as a **separate preceding `Move`
action**, so an unload that follows a move is two actions, not one, even though
the builder would accept the wrapped form.

`Resign` is a thin shim — it hands `jObject["Resign"]` to the `Eliminated`
builder ([ResignAction.cs:11](../AWBWApp.Game/API/Replay/Actions/ResignAction.cs#L11))
and therefore **drops any sibling `GameOver` object** in the same payload. A
replay that ends in a resignation often ends up displaying
`Match ended in Draw!` as a result (see below).

## End-of-replay handling

After the setup loop, and only for `ReplayVersion >= 2`:

```csharp
if (replayData.ReplayInfo.ReplayVersion >= 2)
    setupContext.AddGameOverAction();
```

`AddGameOverAction` ([ReplaySetupContext.cs:128](../AWBWApp.Game/Game/Logic/ReplaySetupContext.cs#L128))
appends a synthetic draw unless the final action implements `IActionCanEndGame`
*and* returns true from `EndsGame()`:

```csharp
if (CurrentTurn.Actions[^1] is IActionCanEndGame lastAction && lastAction.EndsGame())
    return;
```

`IActionCanEndGame` is implemented by `AttackUnitAction`, `CaptureBuildingAction`,
`EliminatedAction` and friends, each deferring to an embedded `EliminatedAction`.

## Failure handling

The setup loop wraps each **turn** (not each action) in a try/catch:

```csharp
catch (Exception e)
{
    Logger.Error(e, $"Failed to setup turn {i}.");
}
```

Consequences:

- An exception at action *j* leaves actions *j..n* with
  `SuccessfullySetup == false`. During playback, `GoToNextAction` sees the flag
  and **jumps to the next turn** rather than running them.
- The turn appears truncated with no user-visible error. Always check the log.
- Exceptions thrown during *parse* are not caught this way — they abort the load.

## Adding a new action

1. Create `API/Replay/Actions/MyThingAction.cs` with a builder exposing
   `Code => "MyThing"` and an action class implementing `IReplayAction`.
   Reflection picks it up; no registration list to edit.
2. In the builder, throw on anything unexpected. Silent tolerance here produces
   desyncs much later that are very hard to trace.
3. In `SetupAndUpdate`, clone before you mutate; store everything undo needs on
   the action instance.
4. If the action can end the game, implement `IActionCanEndGame`.
5. If it changes funds, power, property value or unit value, update both the
   context dictionaries (`FundsValuesForPlayers`, `PowerValuesForPlayers`,
   `PropertyValuesForPlayers`) **and** the matching `PlayerInfo` bindables during
   perform/undo. Mismatches surface as `Desync occured on turn N`.
6. Add a visual test scene under `AWBWApp.Game.Tests/Visual/Logic/Actions`,
   modelled on `TestSceneMoveAction`; `BaseActionsTestScene` has helpers for
   fabricating replay data without a real file.

## Desyncs

`FinishTurnAndCheckForDesyncs` compares the simulated context to the next turn's
snapshot and produces an `EndTurnDesync` listing changed/missing/added units,
mismatched buildings, property values, power values and funds.

It is both a diagnostic and a mechanism: `UndoDesync` is applied when stepping
backwards across a turn boundary, so a desync record is how the player
reconciles its simulation with the authoritative snapshot. Turn-start funds for
the next player are expected to differ and are special-cased.

Unit comparison deliberately compares **`ceil(HitPoints)`**, not the raw float
([ReplayUnit.cs:135](../AWBWApp.Game/API/Replay/ReplayUnit.cs#L135)), so
fractional drift never registers as a desync.
