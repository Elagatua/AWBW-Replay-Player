using System.Collections.Generic;
using System.Linq;
using AWBWApp.Game.Helpers;
using AWBWApp.Game.UI.Replay;
using NUnit.Framework;
using osu.Framework.Allocation;
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

        private List<string> readTooltip() =>
            popup.ChildrenOfType<SpriteText>().Select(x => x.Text.ToString()).ToList();

        private void setUpReplay(string attackerUnit, string defenderUnit, bool buildingUnderDefender, bool mountainBelowDefender = false)
        {
            AddStep($"Load {attackerUnit} against {defenderUnit}", () =>
            {
                var data = CreateBasicReplayData(2);
                var turn = CreateBasicTurnData(data);
                data.TurnData.Add(turn);

                var attacker = CreateBasicReplayUnit(0, 0, attackerUnit, new Vector2I(1, 1));
                var defender = CreateBasicReplayUnit(1, 1, defenderUnit, new Vector2I(2, 1));

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
