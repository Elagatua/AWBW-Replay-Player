using System.IO;
using System.Linq;
using System.Text;
using AWBWApp.Game.Game.COs;
using AWBWApp.Resources;
using NUnit.Framework;
using osu.Framework.IO.Stores;

namespace AWBWApp.Game.Tests.StorageTests
{
    /// <summary>
    /// Covers COs that stand in for another rather than having an AWBW id of their own, such as
    /// the Mini Amarriner Cup's reworks of the standard roster.
    /// </summary>
    [TestFixture]
    public class TestCOVariants
    {
        private const string base_co = @"""Olaf"": { ""Name"": ""Olaf"", ""AWBWID"": 9, ""NormalPower"": { ""PowerStars"": 3 }, ""SuperPower"": { ""PowerStars"": 7 } }";
        private const string variant_co = @"""DS Olaf"": { ""Name"": ""DS Olaf"", ""AWBWID"": 9, ""VariantOf"": ""Olaf"", ""NormalPower"": { ""PowerStars"": 3 }, ""SuperPower"": { ""PowerStars"": 7 } }";
        private const string no_super_co = @"""AW1 Olaf"": { ""Name"": ""AW1 Olaf"", ""AWBWID"": 9, ""VariantOf"": ""Olaf"", ""NormalPower"": { ""PowerStars"": 3 } }";

        [Test]
        public void AVariantSharesItsBasesIdWithoutDisplacingIt()
        {
            var storage = load($"{{ {base_co}, {variant_co} }}");

            Assert.That(storage.GetCOByAWBWId(9).Name, Is.EqualTo("Olaf"), "An id on its own should still mean the CO AWBW plays by default.");
            Assert.That(storage.GetCOByName("DS Olaf").IsVariant, Is.True);
            Assert.That(storage.GetCOByName("Olaf").IsVariant, Is.False);
            Assert.That(storage.GetAllCOIDs(), Is.EquivalentTo(new[] { 9 }), "A variant shares an id rather than adding one.");
        }

        [Test]
        public void EveryCOAnIdCouldMeanIsListedWithTheDefaultFirst()
        {
            var storage = load($"{{ {base_co}, {variant_co} }}");

            Assert.That(storage.GetCOsSharingAWBWId(9).Select(x => x.Name), Is.EqualTo(new[] { "Olaf", "DS Olaf" }));
        }

        [Test]
        public void AnIdWithNoVariantsStillListsItsOwnCO()
        {
            var storage = load($"{{ {base_co} }}");

            Assert.That(storage.GetCOsSharingAWBWId(9).Select(x => x.Name), Is.EqualTo(new[] { "Olaf" }));
        }

        [Test]
        public void AVariantOfSomethingUnknownIsRejected()
        {
            Assert.That(() => load($"{{ {variant_co} }}"), Throws.Exception.With.Message.Contains("does not exist"));
        }

        /// <summary>
        /// A replay only reports the base CO's id, so a variant carrying a different one could
        /// never be matched to the games it was played in.
        /// </summary>
        [Test]
        public void AVariantThatDoesNotShareItsBasesIdIsRejected()
        {
            var mismatched = @"""DS Olaf"": { ""Name"": ""DS Olaf"", ""AWBWID"": 99, ""VariantOf"": ""Olaf"" }";

            Assert.That(() => load($"{{ {base_co}, {mismatched} }}"), Throws.Exception.With.Message.Contains("whose id is 9"));
        }

        [Test]
        public void AVariantOfAVariantIsRejected()
        {
            var nested = @"""AW1 Olaf"": { ""Name"": ""AW1 Olaf"", ""AWBWID"": 9, ""VariantOf"": ""DS Olaf"" }";

            Assert.That(() => load($"{{ {base_co}, {variant_co}, {nested} }}"), Throws.Exception.With.Message.Contains("itself a variant"));
        }

        [Test]
        public void TwoCOsCannotClaimTheSameIdWithoutOneStandingInForTheOther()
        {
            var clash = @"""Nolaf"": { ""Name"": ""Nolaf"", ""AWBWID"": 9 }";

            Assert.That(() => load($"{{ {base_co}, {clash} }}"), Throws.Exception);
        }

        /// <summary>
        /// A variant that lost its super power is the one a replay can recognise by itself.
        /// </summary>
        [Test]
        public void ACOWithNoSuperPowerResolvesToTheVariantThatHasNone()
        {
            var storage = load($"{{ {base_co}, {variant_co}, {no_super_co} }}");

            Assert.That(storage.GetCOPlayed(9, hasSuperPower: true).Name, Is.EqualTo("Olaf"));
            Assert.That(storage.GetCOPlayed(9, hasSuperPower: false).Name, Is.EqualTo("AW1 Olaf"));
        }

        /// <summary>
        /// Falling back to the base CO is the answer this program gave before it knew variants
        /// existed, so an id we have no variant for is no worse off than it was.
        /// </summary>
        [Test]
        public void ACOWithNoSuperPowerAndNoVariantFallsBackToItself()
        {
            var storage = load($"{{ {base_co} }}");

            Assert.That(storage.GetCOPlayed(9, hasSuperPower: false).Name, Is.EqualTo("Olaf"));
        }

        [Test]
        public void TwoVariantsOfOneCOCannotBothLoseTheirSuperPower()
        {
            var second = @"""Also AW1 Olaf"": { ""Name"": ""Also AW1 Olaf"", ""AWBWID"": 9, ""VariantOf"": ""Olaf"", ""NormalPower"": { ""PowerStars"": 3 } }";

            Assert.That(() => load($"{{ {base_co}, {no_super_co}, {second} }}"), Throws.Exception.With.Message.Contains("neither can be recognised"));
        }

        /// <summary>
        /// Von Bolt has no normal power and no variants. Asking which CO was played must not
        /// mistake him for something that lost a power.
        /// </summary>
        [Test]
        public void ACOThatSimplyHasNoSuperPowerIsLeftAlone()
        {
            var onlyNormal = @"""Lonely"": { ""Name"": ""Lonely"", ""AWBWID"": 31, ""NormalPower"": { ""PowerStars"": 3 } }";

            var storage = load($"{{ {onlyNormal} }}");

            Assert.That(storage.GetCOPlayed(31, hasSuperPower: false).Name, Is.EqualTo("Lonely"));
        }

        /// <summary>
        /// Every CO in the shipped data must be well formed by the rules above.
        /// </summary>
        [Test]
        public void TheShippedDataLoads()
        {
            var dllStore = new DllResourceStore(typeof(AWBWAppResources).Assembly);

            var storage = new COStorage();

            using (var stream = dllStore.GetStream("Json/COs.json"))
                storage.LoadStream(stream);

            foreach (var co in storage.GetAllCOs())
            {
                if (!co.IsVariant)
                    continue;

                Assert.That(storage.GetCOsSharingAWBWId(co.AWBWID), Does.Contain(co), $"{co.Name} is missing from the COs its id could mean.");
                Assert.That(storage.GetCOByAWBWId(co.AWBWID), Is.Not.EqualTo(co), $"{co.Name} stands in for another CO, so it should not be what its id means on its own.");
            }
        }

        private static COStorage load(string json)
        {
            var storage = new COStorage();

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                storage.LoadStream(stream);

            return storage;
        }
    }
}
