using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AWBWApp.Game.Game.COs
{
    /// <summary>
    /// Every CO this program knows, indexed by name and by AWBW id.
    /// </summary>
    /// <remarks>
    /// An AWBW id does not identify a CO on its own. A tournament can rework one without giving
    /// it an id of its own, so several COs may answer to the same id: the one AWBW plays by
    /// default, plus any variants that stand in for it. Lookups by id return the default, which
    /// is what a replay means unless something else says otherwise; <see cref="GetCOsSharingAWBWId"/>
    /// gives every CO that id could be.
    /// </remarks>
    public class COStorage
    {
        private readonly Dictionary<int, COData> coByAWBWId = new Dictionary<int, COData>();
        private readonly Dictionary<string, COData> coByName = new Dictionary<string, COData>();
        private readonly Dictionary<int, List<COData>> variantsByAWBWId = new Dictionary<int, List<COData>>();

        public void LoadStream(Stream jsonStream)
        {
            using (var reader = new StreamReader(jsonStream))
            {
                JsonConvert.PopulateObject(reader.ReadToEnd(), coByName);
            }

            foreach (var co in coByName.Values.Where(x => !x.IsVariant))
                coByAWBWId.Add(co.AWBWID, co);

            foreach (var co in coByName.Values.Where(x => x.IsVariant))
            {
                if (!coByName.TryGetValue(co.VariantOf, out var baseCO))
                    throw new Exception($"'{co.Name}' is a variant of '{co.VariantOf}', which does not exist.");

                if (baseCO.IsVariant)
                    throw new Exception($"'{co.Name}' is a variant of '{co.VariantOf}', which is itself a variant.");

                //A replay only ever reports the base CO's id, so a variant that does not share it
                //could never be matched to the games it was played in.
                if (baseCO.AWBWID != co.AWBWID)
                    throw new Exception($"'{co.Name}' has AWBW id {co.AWBWID}, but stands in for '{co.VariantOf}', whose id is {baseCO.AWBWID}.");

                if (!variantsByAWBWId.TryGetValue(co.AWBWID, out var variants))
                    variantsByAWBWId[co.AWBWID] = variants = new List<COData>();

                //Losing a power is the one difference a replay reports, so two variants of the
                //same CO that both lost one could never be told apart. See GetCOPlayed.
                if (co.SuperPower == null && baseCO.SuperPower != null && variants.Any(x => x.SuperPower == null))
                    throw new Exception($"'{co.Name}' is not the only variant of '{co.VariantOf}' without a super power, so neither can be recognised.");

                variants.Add(co);
            }
        }

        /// <summary>
        /// The CO that was actually played, as far as the replay can say.
        /// </summary>
        /// <param name="id">The CO id the replay reports.</param>
        /// <param name="hasSuperPower">Whether the replay gave that player a super power to charge.</param>
        /// <remarks>
        /// A variant usually looks exactly like the CO it stands in for: same id, same name in the
        /// action stream, same star costs. The one exception is a variant that lost a power, which
        /// AWBW reports by marking its requirement absent. Where a replay says a CO has no super
        /// power, the variant without one is the CO that was played.
        /// <para>
        /// Variants that keep their base's powers, such as the DS reworks, are indistinguishable
        /// here and have to be chosen by hand. Falling back to the base CO is the same answer this
        /// program gave before it knew variants existed.
        /// </para>
        /// </remarks>
        /// <summary>
        /// <see cref="GetCOPlayed"/> for an id this build may not know, such as a CO AWBW has
        /// added since.
        /// </summary>
        public bool TryGetCOPlayed(int id, bool hasSuperPower, out COData co)
        {
            if (!coByAWBWId.ContainsKey(id))
            {
                co = null;
                return false;
            }

            co = GetCOPlayed(id, hasSuperPower);
            return true;
        }

        public COData GetCOPlayed(int id, bool hasSuperPower)
        {
            var co = GetCOByAWBWId(id);

            if (hasSuperPower || co.SuperPower == null)
                return co;

            if (!variantsByAWBWId.TryGetValue(id, out var variants))
                return co;

            return variants.FirstOrDefault(x => x.SuperPower == null) ?? co;
        }

        /// <summary>
        /// The CO a replay means when it reports this id, ignoring any variants that stand in for it.
        /// </summary>
        public COData GetCOByAWBWId(int id) => coByAWBWId[id];

        public bool TryGetCOByAWBWId(int id, out COData co) => coByAWBWId.TryGetValue(id, out co);

        public COData GetCOByName(string name) => coByName[name];

        public bool TryGetCOByName(string name, out COData co)
        {
            //A player can have no CO at all, so asking about one is not a mistake.
            if (name == null)
            {
                co = null;
                return false;
            }

            return coByName.TryGetValue(name, out co);
        }

        /// <summary>
        /// Every CO that a replay reporting this id could actually be: the one AWBW plays by
        /// default, followed by any variants standing in for it.
        /// </summary>
        public List<COData> GetCOsSharingAWBWId(int id)
        {
            var cos = new List<COData> { GetCOByAWBWId(id) };

            if (variantsByAWBWId.TryGetValue(id, out var variants))
                cos.AddRange(variants);

            return cos;
        }

        /// <summary>
        /// The ids of every CO AWBW plays by default. Variants share these ids rather than adding
        /// to them.
        /// </summary>
        public List<int> GetAllCOIDs()
        {
            return coByAWBWId.Keys.ToList();
        }

        /// <summary>
        /// Every CO this storage holds, variants included.
        /// </summary>
        public List<COData> GetAllCOs()
        {
            return coByName.Values.ToList();
        }
    }
}
