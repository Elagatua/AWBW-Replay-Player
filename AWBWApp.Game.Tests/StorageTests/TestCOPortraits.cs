using System.Collections.Generic;
using AWBWApp.Game.Game.COs;
using AWBWApp.Resources;
using NUnit.Framework;
using osu.Framework.IO.Stores;

namespace AWBWApp.Game.Tests.StorageTests
{
    /// <summary>
    /// Every CO's portrait is looked up by its name, so a CO added to COs.json without matching
    /// art draws nothing at all and says nothing about why.
    /// </summary>
    [TestFixture]
    public class TestCOPortraits
    {
        [Test]
        public void EveryCOHasASmallPortrait()
        {
            var dllStore = new DllResourceStore(typeof(AWBWAppResources).Assembly);

            var coStorage = new COStorage();

            using (var stream = dllStore.GetStream("Json/COs.json"))
                coStorage.LoadStream(stream);

            var missing = new List<string>();

            foreach (var co in coStorage.GetAllCOs())
            {
                //"No CO" is a stand-in for a player without one, and has no portrait to show.
                if (co.Name == "No CO")
                    continue;

                using var portrait = dllStore.GetStream($"Textures/CO/{co.Name}-Small.png");

                if (portrait == null)
                    missing.Add(co.Name);
            }

            Assert.That(missing, Is.Empty, $"No portrait for: {string.Join(", ", missing)}");
        }

        /// <summary>
        /// The full body art is only ever asked for by the name the replay itself reports, which
        /// is always the CO AWBW plays by default, so variants need none of their own.
        /// </summary>
        [Test]
        public void EveryDefaultCOHasFullArt()
        {
            var dllStore = new DllResourceStore(typeof(AWBWAppResources).Assembly);

            var coStorage = new COStorage();

            using (var stream = dllStore.GetStream("Json/COs.json"))
                coStorage.LoadStream(stream);

            var missing = new List<string>();

            foreach (var co in coStorage.GetAllCOs())
            {
                if (co.Name == "No CO" || co.IsVariant)
                    continue;

                using var art = dllStore.GetStream($"Textures/CO/{co.Name}-Full.png");

                if (art == null)
                    missing.Add(co.Name);
            }

            Assert.That(missing, Is.Empty, $"No full art for: {string.Join(", ", missing)}");
        }
    }
}
