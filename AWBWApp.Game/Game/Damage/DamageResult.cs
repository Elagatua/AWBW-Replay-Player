using System.Collections.Generic;

namespace AWBWApp.Game.Game.Damage
{
    /// <summary>
    /// What an attack would do, across every luck roll the attacking CO can make.
    /// </summary>
    /// <remarks>
    /// Damage is in the 0 to 100 scale the replay stores, not the 1 to 10 the game shows: 55 here
    /// is five and a half hearts. Dividing by ten is a presentation decision, so it is left to the
    /// caller.
    /// </remarks>
    public class DamageResult
    {
        public static readonly DamageResult CANNOT_ATTACK = new DamageResult();

        /// <summary>Whether the attacker has any weapon that can touch the defender at all.</summary>
        public bool CanAttack { get; init; }

        /// <summary>Whether the attacker fell back on its machine gun, having run out of ammunition.</summary>
        public bool UsedSecondaryWeapon { get; init; }

        /// <summary>The chart value before any modifier, as a percentage.</summary>
        public int BaseDamage { get; init; }

        /// <summary>The attacker's total bonus in tenths, CO, terrain and com towers together.</summary>
        public double AttackModifier { get; init; }

        /// <summary>The defender's total bonus in tenths, from its CO. Terrain is counted separately.</summary>
        public double DefenceModifier { get; init; }

        /// <summary>The defence stars under the defender.</summary>
        public int TerrainStars { get; init; }

        public int MinimumDamage { get; init; }

        public int MaximumDamage { get; init; }

        public double AverageDamage { get; init; }

        /// <summary>
        /// Each distinct amount of damage that can come out, with how likely it is, ordered from
        /// least to most. A CO with no luck spread has a single outcome.
        /// </summary>
        public IReadOnlyList<DamageOutcome> Outcomes { get; init; } = new List<DamageOutcome>();
    }

    public readonly struct DamageOutcome
    {
        public readonly int Damage;
        public readonly double Probability;

        public DamageOutcome(int damage, double probability)
        {
            Damage = damage;
            Probability = probability;
        }
    }
}
