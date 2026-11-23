using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AWBWApp.Game.Game.Damage
{
    /// <summary>
    /// The combat tables, transcribed from the community XHKOCalc spreadsheet.
    /// </summary>
    /// <remarks>
    /// Attack and defence figures are in tenths, so 1 means +10%. Base damage is a straight
    /// percentage. Nothing here is interpreted: see <see cref="DamageCalculator"/> for that.
    /// </remarks>
    public class DamageChartStorage
    {
        private const string standard_luck = "Standard";

        private DamageChartData data;

        public void LoadStream(Stream jsonStream)
        {
            using (var reader = new StreamReader(jsonStream))
                data = JsonConvert.DeserializeObject<DamageChartData>(reader.ReadToEnd());
        }

        /// <summary>
        /// What a weapon does to a unit before any modifier, or null where that weapon cannot
        /// touch that unit at all. A blank cell in the chart means exactly that.
        /// </summary>
        public int? GetBaseDamage(string weapon, string defendingUnit)
        {
            if (weapon == null || !data.BaseDamage.TryGetValue(weapon, out var row))
                return null;

            return row.TryGetValue(defendingUnit, out var damage) ? damage : null;
        }

        /// <summary>Every weapon the base damage chart has a row for, secondary weapons included.</summary>
        public IEnumerable<string> AttackerNames => data.BaseDamage.Keys;

        /// <summary>Everything the base damage chart has a column for.</summary>
        public IEnumerable<string> DefenderNames => data.BaseDamage.Values.SelectMany(x => x.Keys).Distinct();

        public double GetAttackModifier(string coState, string unit) => lookup(data.COAttack, coState, unit);

        public double GetDefenceModifier(string coState, string unit) => lookup(data.CODefence, coState, unit);

        /// <summary>
        /// The bonus a CO gets for attacking out of a given terrain. Only Jake, Kindle, Koal and
        /// Lash have one; every other CO has no row and so gets nothing.
        /// </summary>
        public double GetTerrainAttackModifier(string coState, string terrain) => lookup(data.COTerrainAttack, coState, terrain);

        /// <summary>Javier's defence against indirect fire. No other CO has one.</summary>
        public double GetDefenceModifierAgainstIndirects(string coState)
        {
            if (coState == null)
                return 0;

            return data.COVsIndirectDefence.TryGetValue(coState, out var value) ? value : 0;
        }

        /// <summary>
        /// Every luck value a CO can roll, mapped to the number of ways it can come up. Most COs
        /// roll a flat 0 to 9, but Jugger, Flak and Sonja roll two dice and so are weighted.
        /// </summary>
        public IReadOnlyDictionary<string, int> GetLuckRolls(string coState)
        {
            if (coState != null && data.LuckRolls.TryGetValue(coState, out var rolls))
                return rolls;

            return data.LuckRolls[standard_luck];
        }

        private static double lookup(IReadOnlyDictionary<string, Dictionary<string, double>> table, string row, string column)
        {
            //A CO with no row at all - "No CO", or a CO with no terrain bonus - simply has no modifier.
            if (row == null || column == null || !table.TryGetValue(row, out var values))
                return 0;

            return values.TryGetValue(column, out var value) ? value : 0;
        }
    }

    public class DamageChartData
    {
        public Dictionary<string, Dictionary<string, int>> BaseDamage { get; set; }
        public Dictionary<string, Dictionary<string, double>> COAttack { get; set; }
        public Dictionary<string, Dictionary<string, double>> CODefence { get; set; }
        public Dictionary<string, Dictionary<string, double>> COTerrainAttack { get; set; }
        public Dictionary<string, double> COVsIndirectDefence { get; set; }
        public Dictionary<string, Dictionary<string, int>> LuckRolls { get; set; }
    }
}
