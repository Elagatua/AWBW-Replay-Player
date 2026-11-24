using System.Linq;
using AWBWApp.Game.UI.Components;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;

namespace AWBWApp.Game.Tests.Replays
{
    /// <summary>
    /// Covers the power meter's handling of COs that only have one of the two powers.
    /// </summary>
    [TestFixture]
    public class TestPowerProgress
    {
        [Test]
        public void TestCOWithBothPowers()
        {
            var progress = new PowerProgress(270000, 540000);

            Assert.That(progress.PowerRequiredForNormal, Is.EqualTo(270000));
            Assert.That(progress.PowerRequiredForSuper, Is.EqualTo(540000));
            assertSegmentsAreLaidOut(progress, 6);
        }

        /// <summary>
        /// The AW1-era COs, such as Clone Andy and AW1 Drake, have no super power. AWBW reports
        /// that as a negative co_max_spower, which the parser maps onto null.
        /// </summary>
        [Test]
        public void TestCOWithNoSuperPower()
        {
            var progress = new PowerProgress(270000, null);

            Assert.That(progress.PowerRequiredForNormal, Is.EqualTo(270000));
            Assert.That(progress.PowerRequiredForSuper, Is.EqualTo(270000), "A CO with no super power should not be able to reach one.");
            Assert.That(progress.TooltipText.ToString(), Does.Not.Contain("Super"));
            assertSegmentsAreLaidOut(progress, 3);
        }

        /// <summary>
        /// AWBW's own marker for a missing super power, straight from the replay. Before this was
        /// understood it laid the meter out with negative bar widths.
        /// </summary>
        [Test]
        public void TestNegativeSuperPowerIsTreatedAsAbsent()
        {
            var progress = new PowerProgress(540000, -900000);

            Assert.That(progress.PowerRequiredForNormal, Is.EqualTo(540000));
            Assert.That(progress.PowerRequiredForSuper, Is.EqualTo(540000));
            Assert.That(progress.TooltipText.ToString(), Does.Not.Contain("Super"));
            assertSegmentsAreLaidOut(progress, 6);
        }

        /// <summary>
        /// Von Bolt has no normal power, which AWBW marks by making both requirements equal.
        /// </summary>
        [Test]
        public void TestCOWithNoNormalPower()
        {
            var progress = new PowerProgress(900000, 900000);

            Assert.That(progress.PowerRequiredForNormal, Is.EqualTo(0));
            Assert.That(progress.PowerRequiredForSuper, Is.EqualTo(900000));
            Assert.That(progress.TooltipText.ToString(), Does.Not.Contain("Normal"));
            assertSegmentsAreLaidOut(progress, 10);
        }

        private static void assertSegmentsAreLaidOut(PowerProgress progress, int expectedCount)
        {
            var segments = ((Container)progress).Children.ToList();

            Assert.That(segments, Has.Count.EqualTo(expectedCount));

            for (int i = 0; i < segments.Count; i++)
            {
                Assert.That(segments[i].Width, Is.EqualTo(1f / expectedCount).Within(0.0001f), $"Segment {i} has a bad width.");
                Assert.That(segments[i].X, Is.EqualTo(i / (float)expectedCount).Within(0.0001f), $"Segment {i} is in the wrong place.");
            }
        }
    }
}
