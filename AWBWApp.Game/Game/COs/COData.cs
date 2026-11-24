using System.Collections.Generic;
using AWBWApp.Game.Game.Logic;
using AWBWApp.Game.Game.Tile;
using osu.Framework.Graphics.Primitives;

namespace AWBWApp.Game.Game.COs
{
    public class COData
    {
        public string Name { get; set; }
        public int AWBWID { get; set; }
        public string Tooltip;

        /// <summary>
        /// Names the CO this one stands in for, or null for the COs AWBW plays by default.
        /// </summary>
        /// <remarks>
        /// Tournaments can rework a CO without giving it an id of its own: the Mini Amarriner
        /// Cup's "DS Olaf" is still co_id 9 in the replay, and replaces Olaf for the games it is
        /// played in. A variant therefore shares its base's <see cref="AWBWID"/>, and a replay on
        /// its own usually cannot say which of the two was played — see <see cref="COStorage"/>.
        /// </remarks>
        public string VariantOf { get; set; }

        /// <summary>
        /// Whether this CO stands in for another rather than being one AWBW plays by default.
        /// </summary>
        public bool IsVariant => VariantOf != null;

        public COPower DayToDayPower;
        public COPower NormalPower;
        public COPower SuperPower;
    }

    public class COPower
    {
        public List<UnitPowerIncrease> PowerIncreases;
        public float UnitPriceMultiplier = 1;
        public int PropertyFundIncrease;
        public bool HiddenHP;
        public bool AttackFirst;
        public bool SeeIntoHiddenTiles;
        public int SightIncrease;
        public int PowerStars;
        public int AirFuelUsageDecrease;
        public float CounterAttackPower = 1;
        public Vector2I? LuckRange;

        public int? MoveCostPerTile;
        public WeatherType WeatherWithNoMovementAffect;
        public WeatherType WeatherWithAdditionalMovementAffect;
    }

    public class UnitPowerIncrease
    {
        public HashSet<string> AffectedUnits;
        public float PowerIncrease;
        public float DefenseIncrease;
        public int RangeIncrease;
        public TerrainType ConditionalTerrain;
    }
}
