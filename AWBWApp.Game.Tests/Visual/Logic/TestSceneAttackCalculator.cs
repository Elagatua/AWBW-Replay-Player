using System.Collections.Generic;
using System.Linq;
using AWBWApp.Game.Helpers;
using AWBWApp.Game.UI.Replay;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osuTK;

namespace AWBWApp.Game.Tests.Visual.Logic
{
    /// <summary>
    /// Builds the attack calculator tooltip against a real replay.
    /// </summary>
    /// <remarks>
    /// The gesture that opens this is only reachable by a person holding a key over the board, so
    /// without these steps nothing exercises the drawables at all. They are deliberately shallow -
    /// the sums are covered headlessly by TestDamageCalculator - and exist to catch the tooltip
    /// throwing while it builds itself.
    /// </remarks>
    [TestFixture]
    public partial class TestSceneAttackCalculator : BaseActionsTestScene
    {
        private AttackCalculatorPopup popup;

        [Resolved]
        private NearestNeighbourTextureStore textureStore { get; set; }

        [Resolved]
        private AWBWConfigManager config { get; set; }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("Create the popup", () =>
            {
                if (popup != null)
                    Remove(popup, true);

                Add(popup = new AttackCalculatorPopup());
            });
        }

        /// <summary>
        /// The square power icons are their own textures, separate from the wide text banners
        /// the player list uses. A missing one would show as nothing at all rather than throw.
        /// </summary>
        [Test]
        public void TestThePowerIconsAreThere()
        {
            AddAssert("The CO power icon exists", () => textureStore.Get("UI/NormalPowerIcon") != null);
            AddAssert("The super CO power icon exists", () => textureStore.Get("UI/SuperPowerIcon") != null);
        }

        [Test]
        public void TestTwoUnitsTradingBlows()
        {
            setUpReplay("Tank", "Infantry", buildingUnderDefender: false);
            showAttack();

            AddAssert("The tooltip has something in it", () => popup.DrawWidth > 0 && popup.DrawHeight > 0);
        }

        /// <summary>
        /// A unit on a property takes its terrain sprite and defence stars from the building rather
        /// than the tile underneath, which is a separate path through the header.
        /// </summary>
        [Test]
        public void TestDefenderStandingOnABuilding()
        {
            setUpReplay("Tank", "Infantry", buildingUnderDefender: true);
            showAttack();

            AddAssert("The tooltip has something in it", () => popup.DrawWidth > 0);
        }

        /// <summary>
        /// Nothing trades blows with an indirect, so the defender column has no counter to show.
        /// </summary>
        [Test]
        public void TestIndirectAttackerTakesNoCounter()
        {
            setUpReplay("Artillery", "Tank", buildingUnderDefender: false);
            showAttack();

            AddAssert("The tooltip has something in it", () => popup.DrawWidth > 0);
        }

        /// <summary>
        /// An APC has no weapon at all, so neither side can do anything to the other.
        /// </summary>
        [Test]
        public void TestAUnitThatCannotAttackAtAll()
        {
            setUpReplay("APC", "Tank", buildingUnderDefender: false);
            showAttack();

            AddAssert("The tooltip has something in it", () => popup.DrawWidth > 0);
        }

        /// <summary>
        /// A direct attacker is measured from the tile it walks to, not the one it set off from,
        /// which is a different tile going into both the situation and the header's terrain.
        /// </summary>
        [Test]
        public void TestAttackingFromATileTheUnitIsNotStandingOn()
        {
            setUpReplay("Tank", "Infantry", buildingUnderDefender: false);

            //The defender sits at (2, 1), so this is a walk around to the far side of it.
            showAttack(new Vector2I(3, 1));

            AddAssert("The tooltip has something in it", () => popup.DrawWidth > 0);
        }

        /// <summary>
        /// The ring and its lines, hidden and shown again while the fade out is still running.
        /// </summary>
        /// <remarks>
        /// Flicking the modifier key, or dragging the cursor off the board and back, does exactly
        /// this. Two steps rather than one, because the two transforms have to start on different
        /// frames for them to overlap at all.
        /// </remarks>
        [Test]
        public void TestTheIndicatorSurvivesAFlicker()
        {
            AttackCalculatorIndicator indicator = null;

            AddStep("Put an indicator on the board", () =>
            {
                if (indicator != null)
                    Remove(indicator, true);

                Add(indicator = new AttackCalculatorIndicator());
            });

            AddStep("Show it", () => showIndicator(indicator));
            AddUntilStep("It is there", () => indicator.Alpha >= 1);

            AddStep("Hide it", () => indicator.HideAttack());
            AddStep("Show it again before it has finished going", () => showIndicator(indicator));

            AddUntilStep("It is still there", () => indicator.Alpha >= 1);
        }

        /// <summary>
        /// The gesture has been seen to stop drawing itself for a whole session, while the tooltip
        /// it drives carried on working. The cause is not known.
        /// </summary>
        /// <remarks>
        /// So this does not reproduce it - it stands in for it. Whatever took the indicator off
        /// screen, the gesture has to be able to put it back, which it cannot do if it decides
        /// there is nothing to do by remembering it once asked. Hiding it from outside is the
        /// cheapest way to write down that requirement.
        /// </remarks>
        [Test]
        public void TestTheIndicatorCanBeBroughtBackAfterSomethingElseHidesIt()
        {
            AttackCalculatorIndicator indicator = null;

            AddStep("Put an indicator on the board", () =>
            {
                if (indicator != null)
                    Remove(indicator, true);

                Add(indicator = new AttackCalculatorIndicator());
            });

            AddStep("Show it", () => showIndicator(indicator));
            AddUntilStep("It is there", () => indicator.Alpha >= 1);

            AddStep("Something takes it off screen", () => indicator.Alpha = 0);
            AddStep("Use the gesture again", () => showIndicator(indicator));

            AddUntilStep("It comes back", () => indicator.Alpha >= 1);
        }

        private static void showIndicator(AttackCalculatorIndicator indicator) =>
            indicator.ShowAttack(new Vector2I(1, 1), new Vector2I(1, 1), new Vector2I(2, 1), true);

        [Test]
        public void TestHidingAndShowingAgain()
        {
            setUpReplay("Tank", "Infantry", buildingUnderDefender: false);
            showAttack();

            AddStep("Hide it", () => popup.HideAttack());
            AddUntilStep("It fades out", () => popup.Alpha <= 0);

            showAttack();
        }

        /// <summary>
        /// Moving the attacker around its target changes the ground it would fire from, and with it
        /// every number below. The tooltip only rebuilds itself when something it depends on has
        /// changed, so it has to notice that this did.
        /// </summary>
        [Test]
        public void TestWalkingRoundToTheOtherSideOfTheTarget()
        {
            setUpReplay("Tank", "Infantry", buildingUnderDefender: false, mountainBelowDefender: true);

            //Firing from beside the defender, standing on a plain.
            showAttack(new Vector2I(1, 1));

            var fromThePlain = new List<string>();
            AddStep("Read what it says", () => fromThePlain.AddRange(readTooltip()));

            //And from the mountain on its other side, which is four defence stars rather than one.
            showAttack(new Vector2I(2, 2));

            AddAssert("It says something different from the mountain", () =>
            {
                var fromTheMountain = readTooltip();

                Assert.That(fromTheMountain, Is.Not.EqualTo(fromThePlain),
                    "the tooltip did not notice the attacker had moved to different ground");

                return true;
            });
        }

        /// <summary>
        /// What the tooltip is actually showing. A hidden section keeps its drawables, so without
        /// the visibility walk this would read out the headings of sections nobody can see.
        /// </summary>
        private List<string> readTooltip() =>
            popup.ChildrenOfType<SpriteText>().Where(isVisible).Select(x => x.Text.ToString()).ToList();

        private static bool isVisible(Drawable drawable)
        {
            for (var part = drawable; part != null; part = part.Parent)
            {
                if (part.Alpha <= 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// How much this tooltip says is a setting of its own, with four answers: nothing but the
        /// board, what a person watching could work out, what the replay really recorded, or the
        /// last two together so one can be read against the other.
        /// </summary>
        [Test]
        public void TestHowMuchItSaysIsASetting()
        {
            //An infantry against a damaged tank, so that the defender survives with a spread to
            //compare. Anything heavier kills it outright, and one certain outcome looks the same
            //however it was worked out.
            setUpReplay("Infantry", "Tank", buildingUnderDefender: false, damagedDefender: true);

            AddStep("Ask for both", () => config.SetValue(AWBWSetting.CalculatorHitPoints, CalculatorHitPointDisplay.Both));
            showAttack();

            AddAssert("Both sections are there", () =>
            {
                var text = readTooltip();

                Assert.That(text.Any(x => x == "Using calculated HP values"), Is.True);
                Assert.That(text.Any(x => x == "Using true HP"), Is.True);
                return true;
            });

            AddStep("Ask for the true figures alone", () => config.SetValue(AWBWSetting.CalculatorHitPoints, CalculatorHitPointDisplay.True));
            showAttack();

            AddAssert("Only the true one is left", () =>
            {
                var text = readTooltip();

                Assert.That(text.Any(x => x == "Using true HP"), Is.True);
                Assert.That(text.Any(x => x == "Using calculated HP values"), Is.False, "the worked-out section was still there");
                Assert.That(text.Any(x => x.Contains("HP range")), Is.False, "a range was still shown beside it");
                return true;
            });

            AddStep("Ask for what could be worked out", () => config.SetValue(AWBWSetting.CalculatorHitPoints, CalculatorHitPointDisplay.Possible));
            showAttack();

            AddAssert("Only the worked-out one is left", () =>
            {
                var text = readTooltip();

                Assert.That(text.Any(x => x == "Using calculated HP values"), Is.True);
                Assert.That(text.Any(x => x == "Using true HP"), Is.False, "the replay's own figures were still shown");
                Assert.That(text.Any(x => x.Contains("True HP")), Is.False);
                Assert.That(text.Any(x => x.Contains("HP range")), Is.True, "the worked-out range went with them");
                return true;
            });

            AddStep("Ask for nothing but the board", () => config.SetValue(AWBWSetting.CalculatorHitPoints, CalculatorHitPointDisplay.Nominal));
            showAttack();

            AddAssert("Neither is shown", () =>
            {
                var text = readTooltip();

                Assert.That(text.Any(x => x.Contains("True HP")), Is.False);
                Assert.That(text.Any(x => x.Contains("HP range")), Is.False, "a range was still worked out");

                //And the section no longer claims to have calculated anything.
                Assert.That(text.Any(x => x == "Ending HP"), Is.True);
                return true;
            });

            AddStep("Put the setting back", () => config.SetValue(AWBWSetting.CalculatorHitPoints, CalculatorHitPointDisplay.Both));
        }

        private void setUpReplay(string attackerUnit, string defenderUnit, bool buildingUnderDefender, bool mountainBelowDefender = false, bool damagedDefender = false)
        {
            AddStep($"Load {attackerUnit} against {defenderUnit}", () =>
            {
                var data = CreateBasicReplayData(2);
                var turn = CreateBasicTurnData(data);
                data.TurnData.Add(turn);

                var attacker = CreateBasicReplayUnit(0, 0, attackerUnit, new Vector2I(1, 1));
                var defender = CreateBasicReplayUnit(1, 1, defenderUnit, new Vector2I(2, 1));

                //Damaged, so that there is a range to put a true figure beside in the first place.
                if (damagedDefender)
                    defender.HitPoints = 5;

                turn.ReplayUnit.Add(attacker.ID, attacker);
                turn.ReplayUnit.Add(defender.ID, defender);

                if (buildingUnderDefender)
                {
                    //34 is the neutral city, which every map has.
                    var building = CreateBasicReplayBuilding(2, new Vector2I(2, 1), 34);
                    turn.Buildings.Add(building.Position, building);
                }

                var map = CreateBasicMap(5, 5);

                //Four defence stars against the plain everything else sits on.
                if (mountainBelowDefender)
                    map.Ids[2 * 5 + 2] = 2;

                ReplayController.LoadReplay(data, map);
            });

            AddUntilStep("Wait for both units", () => HasUnit(0) && HasUnit(1));
        }

        private void showAttack(Vector2I? attackFrom = null)
        {
            AddStep("Point the calculator at them", () =>
            {
                ReplayController.Map.TryGetDrawableUnit(0, out var attacker);
                ReplayController.Map.TryGetDrawableUnit(1, out var defender);

                popup.ShowAttack(ReplayController, attacker, defender, attackFrom ?? attacker.MapPosition, ToScreenSpace(new Vector2(50, 50)));
            });

            AddUntilStep("It fades in", () => popup.Alpha > 0);
        }
    }
}
