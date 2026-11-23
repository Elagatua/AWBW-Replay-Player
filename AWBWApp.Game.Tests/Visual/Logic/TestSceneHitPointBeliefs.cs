using System;
using System.Collections.Generic;
using AWBWApp.Game.API.Replay;
using AWBWApp.Game.API.Replay.Actions;
using AWBWApp.Game.Game.Damage;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Primitives;

namespace AWBWApp.Game.Tests.Visual.Logic
{
    /// <summary>
    /// Checks the hidden hit point tracking against a real replay, from the adversarial direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tracker is meant to work out what a person watching the game could know, which means it
    /// must never be <i>right</i> by accident. It runs in the setup pass, where the exact hit
    /// points are sitting in the same context it reads from, so one careless line would make it
    /// silently perfect - and perfect output would look like success.
    /// </para>
    /// <para>
    /// So the test asserts containment rather than accuracy: every figure the replay privately
    /// records must fall inside the range the tracker allows. A bug that reads the real value
    /// passes this, which is why the companion assertion checks the tracker is usually *not*
    /// certain. Precision is pinned separately and exactly by TestHitPointBelief.
    /// </para>
    /// </remarks>
    [TestFixture]
    public partial class TestSceneHitPointBeliefs : BaseActionsTestScene
    {
        [Resolved]
        private TestReplayDecoder replayDecoder { get; set; }

        private ReplayData replayData;

        [Test]
        public void TestTheTruthIsAlwaysInsideWhatIsBelieved()
        {
            AddStep("Set up a real replay", () =>
            {
                replayData = replayDecoder.GetReplayInStorage("Json/Replays/478996");
                ReplayController.RunSetupActionsForTest(replayData);
            });

            AddAssert("Every recorded figure is one the tracker allows", () =>
            {
                var checks = 0;

                for (var turn = 0; turn < replayData.TurnData.Count; turn++)
                {
                    foreach (var unit in replayData.TurnData[turn].ReplayUnit)
                    {
                        if (unit.Value.HitPoints == null)
                            continue;

                        var belief = ReplayController.GetHitPointBelief(unit.Key, turn, -1);

                        if (belief == null)
                            continue;

                        //The snapshots are the only place the replay writes the real figure down.
                        var real = (int)Math.Round(unit.Value.HitPoints.Value * 10);
                        checks++;

                        if (real >= belief.Minimum && real <= belief.Maximum)
                            continue;

                        throw new Exception($"Unit {unit.Key} really had {real} hit points on turn {turn}, "
                                            + $"but the tracker only allowed {belief}.");
                    }
                }

                //A replay that checked nothing would pass vacuously.
                Assert.That(checks, Is.GreaterThan(100));
                return true;
            });
        }

        /// <summary>
        /// The other half of the trap. A tracker that cheated by reading the real hit points would
        /// satisfy the containment check perfectly, so this insists it is honestly uncertain about
        /// units it cannot have pinned down.
        /// </summary>
        [Test]
        public void TestTheTrackerIsNotSecretlyPerfect()
        {
            AddStep("Set up a real replay", () =>
            {
                replayData = replayDecoder.GetReplayInStorage("Json/Replays/478996");
                ReplayController.RunSetupActionsForTest(replayData);
            });

            AddAssert("Damaged units are not pinned down exactly", () =>
            {
                var damaged = 0;
                var certain = 0;

                for (var turn = 0; turn < replayData.TurnData.Count; turn++)
                {
                    foreach (var unit in replayData.TurnData[turn].ReplayUnit)
                    {
                        if (unit.Value.HitPoints == null || unit.Value.HitPoints >= 10)
                            continue;

                        var belief = ReplayController.GetHitPointBelief(unit.Key, turn, -1);

                        if (belief == null)
                            continue;

                        damaged++;

                        if (belief.IsCertain)
                            certain++;
                    }
                }

                Assert.That(damaged, Is.GreaterThan(20), "the replay should contain damaged units to check");

                //A handful can legitimately be pinned down - a unit repaired into the ceiling, for
                //one - but a tracker reading the answer would have every single one of them.
                Assert.That(certain, Is.LessThan(damaged / 2),
                    $"{certain} of {damaged} damaged units were known exactly, which is too many to be honest");

                return true;
            });
        }

        /// <summary>
        /// A worked attack, start to finish, on ground the tracker can see: the spread an attack
        /// leaves behind, and that crossing into the next turn does not throw it away.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the case the other scenes in this file cannot reach. They run without a map, and
        /// the tracker refuses to model an attack when it cannot tell what the units are standing
        /// on, so the only narrowing they ever exercise is ruling values out.
        /// </para>
        /// <para>
        /// The turn boundary is the trap. A replay reports hit points inside a turn already rounded
        /// up, and carries the exact figure in its turn snapshots, so an untouched unit appears to
        /// change the moment a new turn starts. Taking that at face value wiped every worked-out
        /// range in the game once per turn, and the only visible symptom was ranges that were
        /// always exactly ten wide - which looks like a tracker that is simply not very clever.
        /// </para>
        /// </remarks>
        [Test]
        public void TestAnAttackNarrowsTheRangeAndTheNextTurnKeepsIt()
        {
            AddStep("A tank shoots an APC, then the turn ends", () =>
            {
                var data = CreateBasicReplayData(2);

                var first = CreateBasicTurnData(data);
                data.TurnData.Add(first);

                var tank = CreateBasicReplayUnit(0, 0, "Tank", new Vector2I(1, 1));
                var apc = CreateBasicReplayUnit(1, 1, "APC", new Vector2I(2, 1));

                first.ReplayUnit.Add(tank.ID, tank);
                first.ReplayUnit.Add(apc.ID, apc);

                var attack = new AttackUnitAction
                {
                    Attacker = tank.Clone(),
                    Defender = apc.Clone(),
                    PowerChanges = new List<AttackUnitAction.COPowerChange>()
                };

                //What the replay reports mid-turn: the figure the board shows, nothing finer.
                attack.Attacker.Ammo = tank.Ammo - 1;
                attack.Defender.HitPoints = 3;

                first.Actions.Add(attack);

                //And what its snapshot carries at the head of the next turn: the real figure.
                var second = CreateBasicTurnData(data, 1);
                data.TurnData.Add(second);

                var tankAfter = tank.Clone();
                var apcAfter = apc.Clone();
                apcAfter.HitPoints = 2.7f;

                second.ReplayUnit.Add(tankAfter.ID, tankAfter);
                second.ReplayUnit.Add(apcAfter.ID, apcAfter);

                ReplayController.RunSetupActionsForTest(data, CreateBasicMap(5, 5));
            });

            AddAssert("The attack leaves a range narrower than the tenth it shows", () =>
            {
                var belief = ReplayController.GetHitPointBelief(1, 0, 0);

                Assert.That(belief, Is.Not.Null);

                //A tank hits an APC for 75, give or take luck, off a plain's single defence star.
                //Everything it could have left lands in the twenties, and showing 3 rules out the
                //three lowest of those.
                Assert.That(belief.Minimum, Is.EqualTo(25));
                Assert.That(belief.Maximum, Is.EqualTo(30));
                return true;
            });

            AddAssert("The next turn keeps it", () =>
            {
                var belief = ReplayController.GetHitPointBelief(1, 1, -1);

                Assert.That(belief, Is.Not.Null);
                Assert.That(belief.Minimum, Is.EqualTo(25), "the turn boundary widened the range back out to the whole tenth");
                Assert.That(belief.Maximum, Is.EqualTo(30));

                //And the figure the replay was privately holding all along is inside it.
                Assert.That(belief[27], Is.GreaterThan(0));
                return true;
            });
        }

        /// <summary>
        /// Nothing fires back at an indirect, so a unit that shoots one comes away untouched and
        /// still known exactly.
        /// </summary>
        /// <remarks>
        /// Worth a test of its own because getting this wrong is invisible from the outside. A
        /// counter that never happened puts the attacker somewhere the board contradicts, and the
        /// way out of a contradiction is to forget everything known about the unit - so the tracker
        /// answers with the whole tenth and looks merely unhelpful rather than wrong.
        /// </remarks>
        [Test]
        public void TestNothingFiresBackAtAnIndirect()
        {
            AddStep("A tank shoots an artillery", () =>
            {
                var data = CreateBasicReplayData(2);

                var turn = CreateBasicTurnData(data);
                data.TurnData.Add(turn);

                var tank = CreateBasicReplayUnit(0, 0, "Tank", new Vector2I(1, 1));
                var artillery = CreateBasicReplayUnit(1, 1, "Artillery", new Vector2I(2, 1));

                turn.ReplayUnit.Add(tank.ID, tank);
                turn.ReplayUnit.Add(artillery.ID, artillery);

                var attack = new AttackUnitAction
                {
                    Attacker = tank.Clone(),
                    Defender = artillery.Clone(),
                    PowerChanges = new List<AttackUnitAction.COPowerChange>()
                };

                attack.Attacker.Ammo = tank.Ammo - 1;
                attack.Defender.HitPoints = 3;

                turn.Actions.Add(attack);

                ReplayController.RunSetupActionsForTest(data, CreateBasicMap(5, 5));
            });

            AddAssert("The tank is still known to be whole", () =>
            {
                var belief = ReplayController.GetHitPointBelief(0, 0, 0);

                Assert.That(belief, Is.Not.Null);
                Assert.That(belief.IsCertain, Is.True, $"the tank came away believed to be {belief}");
                Assert.That(belief.Minimum, Is.EqualTo(HitPointBelief.MAXIMUM));
                return true;
            });
        }

        /// <summary>
        /// Nothing the tracker produces may be empty or contradictory, however odd the replay gets.
        /// </summary>
        [Test]
        public void TestEveryBeliefIsWellFormed()
        {
            AddStep("Set up a real replay", () =>
            {
                replayData = replayDecoder.GetReplayInStorage("Json/Replays/478996");
                ReplayController.RunSetupActionsForTest(replayData);
            });

            AddAssert("Every belief holds together", () =>
            {
                var seen = new HashSet<long>();

                for (var turn = 0; turn < replayData.TurnData.Count; turn++)
                {
                    foreach (var unit in replayData.TurnData[turn].ReplayUnit)
                    {
                        var belief = ReplayController.GetHitPointBelief(unit.Key, turn, -1);

                        if (belief == null)
                            continue;

                        seen.Add(unit.Key);

                        Assert.That(belief.Minimum, Is.LessThanOrEqualTo(belief.Maximum));
                        Assert.That(belief.Minimum, Is.GreaterThanOrEqualTo(0));
                        Assert.That(belief.Maximum, Is.LessThanOrEqualTo(HitPointBelief.MAXIMUM));
                        Assert.That(belief.CandidateCount, Is.GreaterThan(0));

                        var total = 0d;

                        foreach (var (_, probability) in belief.Support)
                        {
                            Assert.That(probability, Is.GreaterThan(0));
                            total += probability;
                        }

                        Assert.That(total, Is.EqualTo(1).Within(0.0001));
                    }
                }

                Assert.That(seen.Count, Is.GreaterThan(10));
                return true;
            });
        }
    }
}
