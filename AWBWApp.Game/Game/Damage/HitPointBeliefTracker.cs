using System;
using System.Collections.Generic;
using AWBWApp.Game.API.Replay;
using AWBWApp.Game.API.Replay.Actions;
using AWBWApp.Game.Game.Building;
using AWBWApp.Game.Game.COs;
using AWBWApp.Game.Game.Logic;
using AWBWApp.Game.Game.Tile;
using AWBWApp.Game.Game.Units;
using osu.Framework.Graphics.Primitives;

namespace AWBWApp.Game.Game.Damage
{
    /// <summary>
    /// Follows the replay through and keeps track of what could be worked out about every unit's
    /// real hit points, using only what a person watching the game would have in front of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This runs in the setup pass, which is awkward, because the setup context holds the exact
    /// hit points the replay records and the whole point here is to not look at them. Only two
    /// things are ever read off a unit: the number the board shows for it, which is public, and
    /// <i>that</i> it changed. Never by how much.
    /// </para>
    /// <para>
    /// An attack gets the full treatment, because its damage spread can be worked out from public
    /// information. Anything else - a power, an unmodelled effect - falls back to ruling out
    /// whatever the newly displayed figure contradicts. That is weaker, but it is never wrong.
    /// </para>
    /// </remarks>
    public class HitPointBeliefTracker
    {
        /// <summary>AWBW repairs in steps of exactly this, and never past full health.</summary>
        private const int repair_amount = 20;

        /// <summary>
        /// The lowest a CO power leaves a unit: one displayed hit point, never none. The action
        /// that applies the change clamps the same way.
        /// </summary>
        private const int power_floor = 10;

        private readonly DamageCalculator calculator;
        private readonly UnitStorage unitStorage;
        private readonly COStorage coStorage;
        private readonly BuildingStorage buildingStorage;
        private readonly TerrainTileStorage terrainStorage;

        private readonly Dictionary<long, HitPointBelief> beliefs = new Dictionary<long, HitPointBelief>();
        private readonly Dictionary<long, List<BeliefAt>> history = new Dictionary<long, List<BeliefAt>>();

        /// <summary>
        /// The units whose hit points this action has already been able to account for. Anything
        /// else that moved is something this does not model, and has to be owned up to.
        /// </summary>
        private readonly HashSet<long> accountedFor = new HashSet<long>();

        private ReplayMap map;

        public HitPointBeliefTracker(DamageCalculator calculator, UnitStorage unitStorage, COStorage coStorage,
                                     BuildingStorage buildingStorage, TerrainTileStorage terrainStorage)
        {
            this.calculator = calculator;
            this.unitStorage = unitStorage;
            this.coStorage = coStorage;
            this.buildingStorage = buildingStorage;
            this.terrainStorage = terrainStorage;
        }

        public void Reset(ReplayMap replayMap)
        {
            map = replayMap;
            beliefs.Clear();
            history.Clear();
        }

        /// <summary>
        /// Called before the action is applied, while the context still describes the world the
        /// attack was launched into.
        /// </summary>
        public void BeforeAction(ReplaySetupContext context, IReplayAction action)
        {
            accountedFor.Clear();

            switch (action)
            {
                case AttackUnitAction attack:
                    recordAttack(context, attack);
                    break;

                case PowerAction power when power.PlayerWideChanges != null:
                    recordPowerChanges(context, power);
                    break;

                case EndTurnAction endTurn when endTurn.RepairedUnits != null:
                {
                    //A repair is a known step, so it moves the whole belief rather than just
                    //ruling values out - and everything at 80 or above lands exactly on 100.
                    foreach (var repaired in endTurn.RepairedUnits)
                    {
                        if (!beliefs.TryGetValue(repaired.Key, out var belief))
                            continue;

                        beliefs[repaired.Key] = belief.WithChange(repair_amount);
                        accountedFor.Add(repaired.Key);
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// Takes in a turn's starting snapshot, which is what the board is set to at the head of
        /// every turn.
        /// </summary>
        /// <remarks>
        /// A replay's snapshots do not always agree with what its actions add up to, and where
        /// they differ it is the snapshot the player ends up looking at. Following the actions
        /// alone would leave this quietly describing a unit the board has already moved on from.
        /// </remarks>
        public void ObserveTurnStart(TurnData turn, ReplaySetupContext context, int turnIndex)
        {
            if (turn?.ReplayUnit == null)
                return;

            accountedFor.Clear();

            foreach (var unit in turn.ReplayUnit)
            {
                var displayed = displayedHitPoints(unit.Value);

                //Whether the board's figure for this unit moved. Both sides are rounded before
                //they are compared, and that is not a nicety: a replay reports hit points inside
                //a turn already rounded up, while its turn snapshots carry the exact value. So a
                //unit sitting quietly at 7.8 reads as 8 all through the turn and as 7.8 the
                //moment the next one starts. Comparing those two directly makes every damaged
                //unit in the game look like it changed, every turn, and throws away everything
                //that had been worked out about it.
                var moved = !context.Units.TryGetValue(unit.Key, out var carriedOver)
                            || displayedHitPoints(carriedOver) != displayed
                            || isContradicted(unit.Key, unit.Value);

                apply(unit.Key, displayed, moved, turnIndex, -1);
            }

            accountedFor.Clear();
        }

        /// <summary>
        /// Whether the replay has already proved this belief wrong.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the one place the real figure is read, and it is read only to throw a belief
        /// away - never to build one. The difference matters. Working anything out from it would
        /// make this silently perfect, which is the one thing it must not be; refusing to hold a
        /// belief the replay has already contradicted just stops it lying.
        /// </para>
        /// <para>
        /// It earns its place because the tooltip prints the tracked range and the real figure on
        /// the same line. A range that does not contain the number sitting next to it is not a
        /// cautious answer, it is a visibly wrong one. The cost of being wrong here is not a
        /// slightly worse guess, it is the feature contradicting itself on screen.
        /// </para>
        /// <para>
        /// Turn snapshots are the only place the replay writes the real figure down, so this is
        /// also the only point at which the check can be made.
        /// </para>
        /// </remarks>
        private bool isContradicted(long unitID, ReplayUnit unit)
        {
            if (unit.HitPoints == null || !beliefs.TryGetValue(unitID, out var belief))
                return false;

            return belief[(int)Math.Round(unit.HitPoints.Value * 10)] <= 0;
        }

        /// <summary>
        /// Takes in what the board now shows: picks up units that have just appeared, and rules
        /// out every value the displayed figure contradicts.
        /// </summary>
        /// <remarks>
        /// Called after each action, and once more at the head of every turn so that a unit which
        /// has never been touched still starts out known.
        /// </remarks>
        public void ObserveUnits(ReplaySetupContext context, ISet<long> changedUnits, int turnIndex, int actionIndex)
        {
            foreach (var unit in context.Units)
                apply(unit.Key, displayedHitPoints(unit.Value), changedUnits != null && changedUnits.Contains(unit.Key), turnIndex, actionIndex);

            //Whatever this action accounted for is spent. Leaving it set would let an attack or a
            //repair excuse a later change it had nothing to do with.
            accountedFor.Clear();
        }

        /// <summary>
        /// Folds one observation of a unit into what is believed about it, and writes the result
        /// down if it moved.
        /// </summary>
        private void apply(long unitID, int displayed, bool moved, int turnIndex, int actionIndex)
        {
            var known = beliefs.TryGetValue(unitID, out var belief);

            if (!known)
            {
                //A unit only ever enters the game whole, whether it was built or placed on the map
                //before the first turn. Anything less and all that is known is the number.
                belief = displayed >= 10 ? HitPointBelief.Certain(HitPointBelief.MAXIMUM) : HitPointBelief.ForDisplayed(displayed);
            }
            else if (moved && !accountedFor.Contains(unitID))
            {
                //Something moved this unit's hit points and this does not know what. A small change
                //does not shift the number on the board at all, so ruling values out would never
                //catch it - the only honest answer is to forget what was worked out and start
                //again from what is displayed.
                belief = HitPointBelief.ForDisplayed(displayed);
            }

            var updated = belief.ConditionedOnDisplayed(displayed);
            beliefs[unitID] = updated;

            //Compared against what was last written down rather than against what came in, since
            //an attack or a repair may have already moved this belief before the board was read.
            if (history.TryGetValue(unitID, out var changes) && changes.Count > 0 && ReferenceEquals(changes[^1].Belief, updated))
                return;

            record(unitID, turnIndex, actionIndex, updated);
        }

        /// <summary>
        /// What could be worked out about a unit at a point in the replay, or null before anything
        /// is known about it.
        /// </summary>
        public HitPointBelief GetBelief(long unitID, int turnIndex, int actionIndex)
        {
            if (!history.TryGetValue(unitID, out var changes))
                return null;

            HitPointBelief found = null;

            foreach (var change in changes)
            {
                if (change.TurnIndex > turnIndex || (change.TurnIndex == turnIndex && change.ActionIndex > actionIndex))
                    break;

                found = change.Belief;
            }

            //Before the first thing that happened to it, a unit is as it entered the game.
            return found ?? changes[0].Belief;
        }

        /// <summary>
        /// A power that moves every unit on one side moves them all by the same stated amount,
        /// which is a step this can follow rather than guess at.
        /// </summary>
        /// <remarks>
        /// It is worth following because of how much it would otherwise cost. Drake's power takes
        /// a hit point off an entire army at once, and without this every one of those units is
        /// something the tracker cannot account for - so a line of untouched infantry, known
        /// exactly, all widen to the whole tenth on the same frame.
        /// </remarks>
        private void recordPowerChanges(ReplaySetupContext context, PowerAction power)
        {
            foreach (var change in power.PlayerWideChanges)
            {
                if (!change.Value.HPGain.HasValue || change.Value.HPGain.Value == 0)
                    continue;

                //Stated on the scale the board shows, as everything in an action is.
                var delta = change.Value.HPGain.Value * 10;

                foreach (var unit in context.Units)
                {
                    //Units in a transport are left alone, exactly as the action itself leaves them.
                    if (unit.Value.PlayerID != change.Key || (unit.Value.BeingCarried ?? false))
                        continue;

                    if (!beliefs.TryGetValue(unit.Key, out var belief))
                        continue;

                    beliefs[unit.Key] = belief.WithChange(delta, power_floor);
                    accountedFor.Add(unit.Key);
                }
            }
        }

        private void recordAttack(ReplaySetupContext context, AttackUnitAction attack)
        {
            if (attack.Defender == null || attack.Attacker == null)
                return;

            if (!context.Units.TryGetValue(attack.Attacker.ID, out var attacker) ||
                !context.Units.TryGetValue(attack.Defender.ID, out var defender))
                return;

            //A unit that moved first fires from where it ended up, and the terrain it fires from
            //is worth something to four COs. The move has not been applied yet, so take the
            //destination from the move itself rather than applying it early.
            var attackerPosition = attack.MoveUnit?.Unit?.Position ?? attacker.Position;

            var situation = createSituation(context, attacker, attackerPosition, defender, defender.Position);

            if (situation == null)
                return;

            var damage = calculator.Calculate(situation);

            if (damage.CanAttack && beliefs.TryGetValue(attack.Defender.ID, out var defenderBelief))
            {
                beliefs[attack.Defender.ID] = defenderBelief.WithDamage(damage.Outcomes);
                accountedFor.Add(attack.Defender.ID);
            }

            //Only direct combat trades blows, the same rule the exchange itself follows. An
            //artillery shot at point blank does not fire back, and nothing fires back at one.
            //Taking a counter here that never happened does not merely blur the attacker's range,
            //it puts the whole thing somewhere the board immediately contradicts - and the way out
            //of a contradiction is to forget everything known about the unit. A tank that opens up
            //on an artillery would come away looking like a tank nothing is known about.
            if (DamageCalculator.IsIndirect(situation.Attacker.Unit) || DamageCalculator.IsIndirect(situation.Defender.Unit))
                return;

            //A counterattack is launched from whatever the defender has left, so it has to be
            //measured against the figure the board shows after the attack landed.
            var defenderAfter = attack.Defender.HitPoints ?? 0;

            if (defenderAfter <= 0 || !beliefs.TryGetValue(attack.Attacker.ID, out var attackerBelief))
                return;

            var counterSituation = situation.Reversed();
            counterSituation.Attacker = counterSituation.Attacker.WithHitPoints((int)Math.Ceiling(defenderAfter));

            var counter = calculator.Calculate(counterSituation);

            if (!counter.CanAttack)
                return;

            beliefs[attack.Attacker.ID] = attackerBelief.WithDamage(counter.Outcomes);
            accountedFor.Add(attack.Attacker.ID);
        }

        private AttackSituation createSituation(ReplaySetupContext context, ReplayUnit attacker, Vector2I? attackerPosition,
                                                ReplayUnit defender, Vector2I? defenderPosition)
        {
            var attackerState = createState(context, attacker, attackerPosition);
            var defenderState = createState(context, defender, defenderPosition);

            if (attackerState == null || defenderState == null)
                return null;

            return new AttackSituation { Attacker = attackerState, Defender = defenderState };
        }

        private CombatantState createState(ReplaySetupContext context, ReplayUnit unit, Vector2I? position)
        {
            if (unit.PlayerID == null || position == null || unit.UnitName == null)
                return null;

            if (!unitStorage.TryGetUnitByCode(unit.UnitName, out var unitData))
                return null;

            if (!context.PlayerTurns.TryGetValue(unit.PlayerID.Value, out var turn))
                return null;

            //Without the board there is no telling what the units are standing on, and a damage
            //spread computed against the wrong terrain is worse than no spread at all: it is
            //confidently wrong, and conditioning on the displayed figure will not always catch it.
            if (!tryGetTerrain(context, position.Value, out var stars, out var terrainName))
                return null;

            return new CombatantState
            {
                Unit = unitData,
                HitPoints = displayedHitPoints(unit),
                Ammo = unit.Ammo ?? unitData.MaxAmmo,
                //Deliberately the CO AWBW plays by default rather than the variant standing in for
                //it: this name keys the damage charts, and they have no rows for variants yet. A
                //variant would silently fall back to no modifiers at all, which is further from
                //the truth than the CO it reworks.
                CO = coStorage.GetCOByAWBWId(turn.ActiveCOID)?.Name,
                Power = turn.COPowerOn,
                ComTowers = countBuildings(context, unit.PlayerID.Value, "ComTower"),
                Cities = countBuildings(context, unit.PlayerID.Value, "City"),
                Funds = context.FundsValuesForPlayers.TryGetValue(unit.PlayerID.Value, out var funds) ? funds : 0,
                Terrain = terrainName,
                TerrainStars = stars
            };
        }

        /// <summary>
        /// What a unit standing here is defended by, or false where the board does not say.
        /// </summary>
        private bool tryGetTerrain(ReplaySetupContext context, Vector2I position, out int stars, out string terrainName)
        {
            stars = 0;
            terrainName = null;

            //A building covers whatever it is built on, and the replay carries those itself.
            if (context.Buildings.TryGetValue(position, out var building) && building.TerrainID.HasValue &&
                buildingStorage.TryGetBuildingByAWBWId(building.TerrainID.Value, out var buildingTile))
            {
                stars = buildingTile.BaseDefence;
                terrainName = DamageTerrain.For(null, buildingTile);
                return true;
            }

            if (map?.Ids == null)
                return false;

            var index = position.Y * map.Size.X + position.X;

            if (index < 0 || index >= map.Ids.Length)
                return false;

            if (!terrainStorage.TryGetTileByAWBWId(map.Ids[index], out var tile))
                return false;

            stars = tile.BaseDefence;
            terrainName = DamageTerrain.For(tile, null);
            return true;
        }

        private int countBuildings(ReplaySetupContext context, long playerID, string buildingType)
        {
            if (!context.PlayerInfos.TryGetValue(playerID, out var player))
                return 0;

            var count = 0;

            foreach (var building in context.Buildings.Values)
            {
                if (!building.TerrainID.HasValue || !buildingStorage.TryGetBuildingByAWBWId(building.TerrainID.Value, out var tile))
                    continue;

                if (tile.CountryID == player.CountryID && tile.BuildingType == buildingType)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// The only figure ever read off a unit here: the one the board shows, rounded up from the
        /// hidden value exactly as the game rounds it.
        /// </summary>
        private static int displayedHitPoints(ReplayUnit unit) => (int)Math.Ceiling(unit.HitPoints ?? 0);

        private void record(long unitID, int turnIndex, int actionIndex, HitPointBelief belief)
        {
            if (!history.TryGetValue(unitID, out var changes))
                history[unitID] = changes = new List<BeliefAt>();

            changes.Add(new BeliefAt(turnIndex, actionIndex, belief));
        }

        private readonly struct BeliefAt
        {
            public readonly int TurnIndex;
            public readonly int ActionIndex;
            public readonly HitPointBelief Belief;

            public BeliefAt(int turnIndex, int actionIndex, HitPointBelief belief)
            {
                TurnIndex = turnIndex;
                ActionIndex = actionIndex;
                Belief = belief;
            }
        }
    }
}
