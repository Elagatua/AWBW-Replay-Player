using System.Linq;
using AWBWApp.Game.Game.Damage;
using AWBWApp.Game.UI.Replay;
using NUnit.Framework;
using osu.Framework.Graphics.Shapes;
using osuTK.Graphics;

namespace AWBWApp.Game.Tests.StorageTests
{
    /// <summary>
    /// The colour the attack calculator reads a probability out in. Pinned because an inverted
    /// gradient still looks deliberate: every bar is coloured, none of them is obviously wrong,
    /// and the only tell is that the likely outcomes are the ones in red.
    /// </summary>
    [TestFixture]
    public class TestOutcomeLikelihoodColour
    {
        [Test]
        public void TheEndsAreRedAndGreen()
        {
            var never = AttackCalculatorColumn.ForLikelihood(0);
            var certain = AttackCalculatorColumn.ForLikelihood(1);

            Assert.That(never.R, Is.GreaterThan(never.G), "nothing-ever-happens should be red");
            Assert.That(certain.G, Is.GreaterThan(certain.R), "a certainty should be green");
        }

        [Test]
        public void EvenOddsSitBetweenThem()
        {
            var even = AttackCalculatorColumn.ForLikelihood(0.5);

            //Yellow is both at once, which is what makes it the turning point.
            Assert.That(even.R, Is.GreaterThan(0.8f));
            Assert.That(even.G, Is.GreaterThan(0.7f));
            Assert.That(even.B, Is.LessThan(0.4f));
        }

        [Test]
        public void ItRunsOneWayTheWholeLength()
        {
            var previous = AttackCalculatorColumn.ForLikelihood(0).G;

            for (var step = 1; step <= 20; step++)
            {
                var green = AttackCalculatorColumn.ForLikelihood(step / 20.0).G;

                Assert.That(green, Is.GreaterThanOrEqualTo(previous), $"the gradient doubled back at {step / 20.0}");
                previous = green;
            }
        }

        [Test]
        public void NonsenseStaysOnTheScale()
        {
            Assert.That(AttackCalculatorColumn.ForLikelihood(-1), Is.EqualTo(AttackCalculatorColumn.ForLikelihood(0)));
            Assert.That(AttackCalculatorColumn.ForLikelihood(2), Is.EqualTo(AttackCalculatorColumn.ForLikelihood(1)));
        }

        /// <summary>
        /// A near-certain wreck is the one row the gradient does not get to have, since green is
        /// the last thing it should be.
        /// </summary>
        [Test]
        public void AWreckIsRedHoweverLikelyItIs()
        {
            foreach (var probability in new[] { 0.01, 0.5, 1.0 })
            {
                var bar = new OutcomeBar(new HitPointOutcome(0, probability));
                var boxes = bar.Children.OfType<Box>().ToList();

                Assert.That(boxes.Any(x => (Color4)x.Colour.TopLeft == AttackCalculatorColumn.DESTROYED), Is.True,
                    $"a wreck at {probability} was not drawn in red");

                Assert.That(boxes.Any(x => (Color4)x.Colour.TopLeft == AttackCalculatorColumn.ForLikelihood(probability)), Is.False,
                    $"a wreck at {probability} took the gradient's colour");
            }
        }
    }
}
