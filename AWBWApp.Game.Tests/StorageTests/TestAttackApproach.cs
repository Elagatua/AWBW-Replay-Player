using AWBWApp.Game.UI.Replay;
using NUnit.Framework;
using osu.Framework.Graphics.Primitives;

namespace AWBWApp.Game.Tests.StorageTests
{
    /// <summary>
    /// Which tile a direct attack is measured from. The gesture that feeds this needs a person
    /// holding a key over the board, so the choosing is kept out here where it can be checked.
    /// </summary>
    [TestFixture]
    public class TestAttackApproach
    {
        private static readonly Vector2I map_size = new Vector2I(10, 10);

        [Test]
        public void TheLastTileBesideTheTargetWins()
        {
            var trail = new[]
            {
                new Vector2I(1, 5),
                new Vector2I(2, 5),
                //Beside the target, but left behind for another one.
                new Vector2I(5, 4),
                new Vector2I(5, 6),
                //The target itself, where the cursor has come to rest.
                new Vector2I(5, 5)
            };

            Assert.That(AttackApproach.ChooseAttackFrom(new Vector2I(1, 5), new Vector2I(5, 5), map_size, trail),
                Is.EqualTo(new Vector2I(5, 6)));
        }

        /// <summary>
        /// Walking straight onto something already next to you is point blank, and the attacker's
        /// own tile is a tile beside the target like any other.
        /// </summary>
        [Test]
        public void AnAttackerAlreadyBesideTheTargetFiresFromWhereItStands()
        {
            var trail = new[] { new Vector2I(4, 5), new Vector2I(5, 5) };

            Assert.That(AttackApproach.ChooseAttackFrom(new Vector2I(4, 5), new Vector2I(5, 5), map_size, trail),
                Is.EqualTo(new Vector2I(4, 5)));
        }

        [Test]
        public void WithNothingBesideTheTargetCrossedItWalksInFromTheNearestSide()
        {
            var trail = new[] { new Vector2I(1, 1), new Vector2I(5, 5) };

            Assert.That(AttackApproach.ChooseAttackFrom(new Vector2I(1, 5), new Vector2I(5, 5), map_size, trail),
                Is.EqualTo(new Vector2I(4, 5)));
        }

        [Test]
        public void AnEmptyTrailStillChoosesATile()
        {
            Assert.That(AttackApproach.ChooseAttackFrom(new Vector2I(5, 1), new Vector2I(5, 5), map_size, null),
                Is.EqualTo(new Vector2I(5, 4)));
        }

        /// <summary>
        /// A target in the corner has sides that are not on the board at all, and standing on one
        /// of those would read terrain out of a tile that does not exist.
        /// </summary>
        [Test]
        public void TheFallbackStaysOnTheBoard()
        {
            var chosen = AttackApproach.ChooseAttackFrom(new Vector2I(4, 4), new Vector2I(0, 0), map_size, null);

            Assert.That(chosen.X, Is.InRange(0, map_size.X - 1));
            Assert.That(chosen.Y, Is.InRange(0, map_size.Y - 1));
            Assert.That(AttackApproach.IsAdjacent(chosen, new Vector2I(0, 0)), Is.True);
        }

        [Test]
        public void DiagonalsAreNotAdjacent()
        {
            Assert.That(AttackApproach.IsAdjacent(new Vector2I(1, 1), new Vector2I(2, 2)), Is.False);
            Assert.That(AttackApproach.IsAdjacent(new Vector2I(1, 1), new Vector2I(1, 1)), Is.False);
            Assert.That(AttackApproach.IsAdjacent(new Vector2I(1, 1), new Vector2I(1, 2)), Is.True);
        }
    }
}
