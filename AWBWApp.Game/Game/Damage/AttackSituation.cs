using AWBWApp.Game.Game.COs;
using AWBWApp.Game.Game.Units;

namespace AWBWApp.Game.Game.Damage
{
    /// <summary>
    /// One side of a fight: the unit, and everything about its owner's position that changes how
    /// hard it hits or how well it holds up.
    /// </summary>
    /// <remarks>
    /// Hit points here are the whole numbers the game shows, 1 to 10, not the 0 to 100 the replay
    /// stores. That is what the damage formula takes, and it is why a unit the replay has at 9.1
    /// hits exactly as hard as an untouched one.
    /// </remarks>
    public class CombatantState
    {
        public UnitData Unit { get; set; }

        /// <summary>Hit points as displayed, 1 to 10.</summary>
        public int HitPoints { get; set; } = 10;

        public int Ammo { get; set; }

        /// <summary>The CO's name, or null where there is none.</summary>
        public string CO { get; set; }

        public ActiveCOPower Power { get; set; }

        /// <summary>Com towers held. Each is worth +10% attack, and nothing to defence.</summary>
        public int ComTowers { get; set; }

        /// <summary>Cities held. Only Kindle's super cares, and in AWBW it counts cities alone.</summary>
        public int Cities { get; set; }

        /// <summary>Funds in hand. Only Colin's super cares.</summary>
        public int Funds { get; set; }

        /// <summary>
        /// The terrain this unit is standing on, as <see cref="DamageTerrain"/> names it. Only
        /// Jake, Kindle, Koal and Lash draw anything from it.
        /// </summary>
        public string Terrain { get; set; }

        /// <summary>The defence stars of the tile this unit is standing on.</summary>
        public int TerrainStars { get; set; }

        /// <summary>
        /// The key the charts file this CO under. Every CO has three rows, one per power state,
        /// and each power row already carries the universal +10% that any active power grants.
        /// </summary>
        public string COState
        {
            get
            {
                if (string.IsNullOrEmpty(CO))
                    return null;

                switch (Power)
                {
                    case ActiveCOPower.Normal:
                        return CO + "-COP";

                    case ActiveCOPower.Super:
                        return CO + "-SCOP";

                    default:
                        return CO;
                }
            }
        }

        public CombatantState WithHitPoints(int hitPoints)
        {
            return new CombatantState
            {
                Unit = Unit,
                HitPoints = hitPoints,
                Ammo = Ammo,
                CO = CO,
                Power = Power,
                ComTowers = ComTowers,
                Cities = Cities,
                Funds = Funds,
                Terrain = Terrain,
                TerrainStars = TerrainStars
            };
        }
    }

    /// <summary>
    /// Everything an attack's outcome depends on, gathered in one place.
    /// </summary>
    public class AttackSituation
    {
        public CombatantState Attacker { get; set; }

        public CombatantState Defender { get; set; }

        /// <summary>
        /// Whether this is the defender hitting back rather than the opening attack. It changes
        /// nothing except under Sonja, whose counterattacks hit half again as hard.
        /// </summary>
        public bool IsCounterAttack { get; set; }

        /// <summary>
        /// The same fight with the roles swapped, which is what a counterattack is.
        /// </summary>
        public AttackSituation Reversed() =>
            new AttackSituation
            {
                Attacker = Defender,
                Defender = Attacker,
                IsCounterAttack = !IsCounterAttack
            };
    }
}
