using System.Collections.Generic;

namespace AWBWApp.Game.Game.Damage
{
    /// <summary>
    /// A whole fight: the attack, the counterattack it invites, and where both units are likely
    /// to end up once the dust settles.
    /// </summary>
    public class AttackExchange
    {
        /// <summary>The opening attack. Its <see cref="DamageResult.CanAttack"/> speaks for the exchange.</summary>
        public DamageResult Attack { get; init; } = DamageResult.CANNOT_ATTACK;

        /// <summary>
        /// Whether the defender hits back at all. It does not when either unit fires from range,
        /// when it has no weapon that can reach the attacker, or when the attack always kills it.
        /// </summary>
        public bool HasCounterAttack { get; init; }

        /// <summary>
        /// The counterattack's damage averaged over the whole exchange, so rolls that kill the
        /// defender outright contribute nothing. On the replay's 0 to 100 scale.
        /// </summary>
        public double ExpectedCounterDamage { get; init; }

        /// <summary>
        /// The least and most the counterattack can do across the rolls where it happens at all.
        /// Both zero when it never happens.
        /// </summary>
        public int MinimumCounterDamage { get; init; }

        public int MaximumCounterDamage { get; init; }

        /// <summary>
        /// Where the attacker is left, as displayed hit points against probability, least first.
        /// Only the counterattack can move this.
        /// </summary>
        public IReadOnlyList<HitPointOutcome> AttackerOutcomes { get; init; } = new List<HitPointOutcome>();

        /// <summary>Where the defender is left, as displayed hit points against probability, least first.</summary>
        public IReadOnlyList<HitPointOutcome> DefenderOutcomes { get; init; } = new List<HitPointOutcome>();

        /// <summary>The damage the attack deals on average, on the replay's 0 to 100 scale.</summary>
        public double ExpectedDamage => Attack.AverageDamage;
    }

    public readonly struct HitPointOutcome
    {
        /// <summary>Hit points as the game would show them, 0 to 10. Zero means destroyed.</summary>
        public readonly int HitPoints;

        public readonly double Probability;

        public HitPointOutcome(int hitPoints, double probability)
        {
            HitPoints = hitPoints;
            Probability = probability;
        }
    }
}
