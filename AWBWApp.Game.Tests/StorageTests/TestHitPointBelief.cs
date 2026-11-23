using System.Collections.Generic;
using System.Linq;
using AWBWApp.Game.Game.Damage;
using NUnit.Framework;

namespace AWBWApp.Game.Tests.StorageTests
{
    /// <summary>
    /// The arithmetic behind working out a unit's hidden hit points from what the board shows.
    /// </summary>
    [TestFixture]
    public class TestHitPointBelief
    {
        [Test]
        public void AUnitThatHasNeverBeenTouchedIsKnownExactly()
        {
            var belief = HitPointBelief.Certain(100);

            Assert.That(belief.IsCertain, Is.True);
            Assert.That(belief.Minimum, Is.EqualTo(100));
            Assert.That(belief.Maximum, Is.EqualTo(100));
            Assert.That(belief.DisplayedHitPoints, Is.EqualTo(10));
        }

        [Test]
        public void KnowingOnlyTheDisplayedFigureMeansKnowingTheWholeTenth()
        {
            var belief = HitPointBelief.ForDisplayed(8);

            Assert.That(belief.Minimum, Is.EqualTo(71));
            Assert.That(belief.Maximum, Is.EqualTo(80));
            Assert.That(belief.CandidateCount, Is.EqualTo(10));
            Assert.That(belief[75], Is.EqualTo(0.1).Within(0.0001));
        }

        /// <summary>
        /// The example the whole feature exists for. A tank straight off a base is exactly 100.
        /// Another tank shoots it for somewhere in 55 to 58, and the board then shows 5. Reading
        /// that 5 off the board would say 50; it is really somewhere in 42 to 45.
        /// </summary>
        [Test]
        public void AnAttackNarrowsFarBelowWhatTheBoardShows()
        {
            var belief = HitPointBelief.Certain(100)
                                       .WithDamage(evenly(55, 56, 57, 58))
                                       .ConditionedOnDisplayed(5);

            Assert.That(belief.Minimum, Is.EqualTo(42));
            Assert.That(belief.Maximum, Is.EqualTo(45));
            Assert.That(belief.CandidateCount, Is.EqualTo(4));

            //And it is well under the 50 that the displayed figure alone would suggest.
            Assert.That(belief.Maximum, Is.LessThan(50));

            foreach (var (_, probability) in belief.Support)
                Assert.That(probability, Is.EqualTo(0.25).Within(0.0001));
        }

        /// <summary>
        /// Rolls that would have left a different number on the board are ruled out, and what
        /// survives is weighted up to account for it.
        /// </summary>
        [Test]
        public void OutcomesTheBoardContradictsAreRuledOut()
        {
            //55 and 56 leave 45 and 44, which show as 5. 65 leaves 35, which shows as 4.
            var belief = HitPointBelief.Certain(100)
                                       .WithDamage(evenly(55, 56, 65))
                                       .ConditionedOnDisplayed(5);

            Assert.That(belief.CandidateCount, Is.EqualTo(2));
            Assert.That(belief.Minimum, Is.EqualTo(44));
            Assert.That(belief.Maximum, Is.EqualTo(45));

            //Two of three rolls survive, and between them they now carry all of the probability.
            Assert.That(belief.Support.Sum(x => x.Probability), Is.EqualTo(1).Within(0.0001));
            Assert.That(belief[45], Is.EqualTo(0.5).Within(0.0001));
        }

        /// <summary>
        /// Repairs are the one thing that makes a unit's hit points <i>more</i> certain: AWBW
        /// repairs in steps of exactly 20 and never past 100, so anything from 81 upwards lands
        /// exactly on full health.
        /// </summary>
        [Test]
        public void RepairingIntoTheCeilingRemovesAllDoubt()
        {
            var belief = HitPointBelief.ForDisplayed(9).WithChange(20);

            Assert.That(belief.IsCertain, Is.True);
            Assert.That(belief.Minimum, Is.EqualTo(100));
        }

        [Test]
        public void RepairingShortOfTheCeilingKeepsTheSameDoubt()
        {
            var belief = HitPointBelief.ForDisplayed(5).WithChange(20);

            Assert.That(belief.Minimum, Is.EqualTo(61));
            Assert.That(belief.Maximum, Is.EqualTo(70));
            Assert.That(belief.CandidateCount, Is.EqualTo(10));
        }

        [Test]
        public void LearningNothingNewChangesNothingAtAll()
        {
            var belief = HitPointBelief.Certain(100);

            //The same instance back, so that following a long replay does not pile up copies of
            //a belief nothing has happened to.
            Assert.That(belief.ConditionedOnDisplayed(10), Is.SameAs(belief));
        }

        /// <summary>
        /// If the replay does something this does not model, the honest answer is to fall back to
        /// the displayed figure rather than insist on a belief the game has contradicted.
        /// </summary>
        [Test]
        public void ABeliefTheGameContradictsIsAbandoned()
        {
            var belief = HitPointBelief.Certain(100).ConditionedOnDisplayed(4);

            Assert.That(belief.Minimum, Is.EqualTo(31));
            Assert.That(belief.Maximum, Is.EqualTo(40));
        }

        [Test]
        public void JoiningAddsTheTwoTogether()
        {
            var belief = HitPointBelief.Certain(40).JoinedWith(HitPointBelief.Certain(30));

            Assert.That(belief.IsCertain, Is.True);
            Assert.That(belief.Minimum, Is.EqualTo(70));

            //And never past full health.
            Assert.That(HitPointBelief.Certain(80).JoinedWith(HitPointBelief.Certain(80)).Minimum, Is.EqualTo(100));
        }

        [Test]
        public void DamageNeverTakesAUnitBelowNothing()
        {
            var belief = HitPointBelief.Certain(10).WithDamage(evenly(50, 60));

            Assert.That(belief.Minimum, Is.EqualTo(0));
            Assert.That(belief.Maximum, Is.EqualTo(0));
            Assert.That(belief.DisplayedHitPoints, Is.EqualTo(0));
        }

        [Test]
        public void ProbabilitiesAlwaysAddUp()
        {
            var belief = HitPointBelief.Certain(100);

            foreach (var damage in new[] { evenly(20, 25, 30), evenly(11, 12), evenly(5, 6, 7, 8, 9) })
            {
                belief = belief.WithDamage(damage);
                Assert.That(belief.Support.Sum(x => x.Probability), Is.EqualTo(1).Within(0.0001));

                belief = belief.ConditionedOnDisplayed(belief.DisplayedHitPoints);
                Assert.That(belief.Support.Sum(x => x.Probability), Is.EqualTo(1).Within(0.0001));
            }
        }

        private static IReadOnlyList<DamageOutcome> evenly(params int[] damages) =>
            damages.Select(x => new DamageOutcome(x, 1.0 / damages.Length)).ToList();
    }
}
