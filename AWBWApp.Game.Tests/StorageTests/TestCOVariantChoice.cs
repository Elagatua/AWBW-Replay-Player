using System.Collections.Generic;
using System.IO;
using System.Text;
using AWBWApp.Game.API.Replay;
using AWBWApp.Game.Game.COs;
using NUnit.Framework;

namespace AWBWApp.Game.Tests.StorageTests
{
    /// <summary>
    /// Covers the CO a user picks by hand, for the variants a replay cannot tell apart.
    /// </summary>
    [TestFixture]
    public class TestCOVariantChoice
    {
        private const string cos = @"{
            ""Olaf"": { ""Name"": ""Olaf"", ""AWBWID"": 9, ""NormalPower"": { ""PowerStars"": 3 }, ""SuperPower"": { ""PowerStars"": 7 } },
            ""DS Olaf"": { ""Name"": ""DS Olaf"", ""AWBWID"": 9, ""VariantOf"": ""Olaf"", ""NormalPower"": { ""PowerStars"": 3 }, ""SuperPower"": { ""PowerStars"": 7 } },
            ""AW1 Olaf"": { ""Name"": ""AW1 Olaf"", ""AWBWID"": 9, ""VariantOf"": ""Olaf"", ""NormalPower"": { ""PowerStars"": 3 } },
            ""Andy"": { ""Name"": ""Andy"", ""AWBWID"": 1, ""NormalPower"": { ""PowerStars"": 3 }, ""SuperPower"": { ""PowerStars"": 6 } }
        }";

        [Test]
        public void WithNoChoiceTheReplayStillDecides()
        {
            var replay = replayWithOlaf(hasSuperPower: false);
            replay.ApplyCOVariantChoices();

            Assert.That(storage().GetCOPlayedBy(replay.TurnData[0].Players[1]).Name, Is.EqualTo("AW1 Olaf"));
        }

        [Test]
        public void AChoiceWinsOverWhatTheReplayCouldWorkOut()
        {
            var replay = replayWithOlaf(hasSuperPower: true);
            replay.ReplayInfo.Players[1].COVariants = new Dictionary<int, string> { { 9, "DS Olaf" } };
            replay.ApplyCOVariantChoices();

            Assert.That(storage().GetCOPlayedBy(replay.TurnData[0].Players[1]).Name, Is.EqualTo("DS Olaf"));
        }

        /// <summary>
        /// A choice is kept in the replay index, which outlives any one version of this program.
        /// </summary>
        [Test]
        public void AChoiceNamingSomethingUnknownIsIgnored()
        {
            var replay = replayWithOlaf(hasSuperPower: true);
            replay.ReplayInfo.Players[1].COVariants = new Dictionary<int, string> { { 9, "Olaf From The Future" } };
            replay.ApplyCOVariantChoices();

            Assert.That(storage().GetCOPlayedBy(replay.TurnData[0].Players[1]).Name, Is.EqualTo("Olaf"));
        }

        /// <summary>
        /// A choice that belongs to a different CO entirely must not be applied to this one.
        /// </summary>
        [Test]
        public void AChoiceNamingAnotherCOIsIgnored()
        {
            var replay = replayWithOlaf(hasSuperPower: true);
            replay.ReplayInfo.Players[1].COVariants = new Dictionary<int, string> { { 9, "Andy" } };
            replay.ApplyCOVariantChoices();

            Assert.That(storage().GetCOPlayedBy(replay.TurnData[0].Players[1]).Name, Is.EqualTo("Olaf"));
        }

        /// <summary>
        /// Reading a replay again produces only what the file says, so anything the user chose has
        /// to be carried across or it is silently lost.
        /// </summary>
        [Test]
        public void ReParsingAReplayKeepsWhatTheUserChose()
        {
            var old = replayWithOlaf(hasSuperPower: true).ReplayInfo;
            old.UserDefinedName = "My favourite game";
            old.Players[1].COVariants = new Dictionary<int, string> { { 9, "DS Olaf" } };

            var reparsed = replayWithOlaf(hasSuperPower: true).ReplayInfo;
            reparsed.CarryOverUserChoicesFrom(old);

            Assert.That(reparsed.UserDefinedName, Is.EqualTo("My favourite game"));
            Assert.That(reparsed.Players[1].COVariants[9], Is.EqualTo("DS Olaf"));
        }

        private static COStorage storage()
        {
            var co = new COStorage();

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(cos)))
                co.LoadStream(stream);

            return co;
        }

        private static ReplayData replayWithOlaf(bool hasSuperPower)
        {
            var data = new ReplayData();

            data.ReplayInfo.Players = new Dictionary<long, ReplayUser> { { 1, new ReplayUser { ID = 1, COsUsedByPlayer = { 9 } } } };

            data.TurnData.Add(new TurnData
            {
                Day = 1,
                ActivePlayerID = 1,
                Players = new Dictionary<long, ReplayUserTurn>
                {
                    { 1, new ReplayUserTurn { ID = 1, ActiveCOID = 9, RequiredPowerForNormal = 270000, RequiredPowerForSuper = hasSuperPower ? 630000 : null } }
                }
            });

            return data;
        }
    }
}
