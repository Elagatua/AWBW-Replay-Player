using System;
using System.Collections.Generic;
using System.Linq;

namespace AWBWApp.Game.Game.Damage
{
    /// <summary>
    /// What can be worked out about a unit's real hit points from watching the game, as a
    /// probability over every value it could be holding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Advance Wars tracks hit points from 0 to 100 and shows you a tenth of that, rounded up. The
    /// exact figure is hidden until the replay is over - but it is not unknowable. The damage
    /// formula works off the hit points both units <i>display</i>, which anyone watching can see,
    /// so the rolls an attack could have made are public. Only which roll landed is hidden, and the
    /// number shown afterwards is evidence about that.
    /// </para>
    /// <para>
    /// So a tank fresh from a base is exactly 100. Shot by another tank for somewhere in 55 to 58,
    /// it is somewhere in 42 to 45 - not the 50 that reading its displayed 5 off the board would
    /// suggest. That gap is often the difference between a follow-up killing and leaving a sliver.
    /// </para>
    /// <para>
    /// This never reads the exact hit points the replay stores. Doing so would quietly make it
    /// perfect, which is the one thing it must not be.
    /// </para>
    /// </remarks>
    public class HitPointBelief
    {
        public const int MAXIMUM = 100;

        private readonly double[] probabilities;

        private HitPointBelief(double[] probabilities)
        {
            this.probabilities = probabilities;

            Minimum = MAXIMUM;
            Maximum = 0;

            for (var i = 0; i <= MAXIMUM; i++)
            {
                if (probabilities[i] <= 0)
                    continue;

                Minimum = Math.Min(Minimum, i);
                Maximum = Math.Max(Maximum, i);
                Mean += i * probabilities[i];
            }

            if (Maximum < Minimum)
                Minimum = Maximum;
        }

        /// <summary>The lowest hit points this unit could be holding.</summary>
        public int Minimum { get; }

        /// <summary>The highest hit points this unit could be holding.</summary>
        public int Maximum { get; }

        public double Mean { get; }

        /// <summary>Whether the real figure is pinned down exactly, as it is for an untouched unit.</summary>
        public bool IsCertain => Minimum == Maximum;

        /// <summary>
        /// What the board shows for this unit. Every value still in play shares one displayed
        /// figure, because the displayed figure is itself one of the things this is built from.
        /// </summary>
        public int DisplayedHitPoints => ToDisplayed(Maximum);

        public double this[int hitPoints] => hitPoints < 0 || hitPoints > MAXIMUM ? 0 : probabilities[hitPoints];

        public IEnumerable<(int HitPoints, double Probability)> Support
        {
            get
            {
                for (var i = Minimum; i <= Maximum; i++)
                {
                    if (probabilities[i] > 0)
                        yield return (i, probabilities[i]);
                }
            }
        }

        /// <summary>A unit whose hit points are known exactly, such as one just built.</summary>
        public static HitPointBelief Certain(int hitPoints)
        {
            var probabilities = new double[MAXIMUM + 1];
            probabilities[Math.Clamp(hitPoints, 0, MAXIMUM)] = 1;
            return new HitPointBelief(probabilities);
        }

        /// <summary>
        /// A unit nothing is known about beyond the number on the board: anywhere in that tenth,
        /// evenly. The fallback for a unit already on the map when the replay opens.
        /// </summary>
        public static HitPointBelief ForDisplayed(int displayed)
        {
            var probabilities = new double[MAXIMUM + 1];

            if (displayed <= 0)
            {
                probabilities[0] = 1;
                return new HitPointBelief(probabilities);
            }

            var highest = Math.Min(MAXIMUM, displayed * 10);
            var lowest = Math.Max(1, highest - 9);

            for (var i = lowest; i <= highest; i++)
                probabilities[i] = 1.0 / (highest - lowest + 1);

            return new HitPointBelief(probabilities);
        }

        /// <summary>The number the board shows for a unit on the replay's 0 to 100 scale.</summary>
        public static int ToDisplayed(int hitPoints) => (int)Math.Ceiling(Math.Clamp(hitPoints, 0, MAXIMUM) / 10.0);

        /// <summary>
        /// Spreads this belief across everything an attack could have taken off, weighted by how
        /// likely each roll was.
        /// </summary>
        public HitPointBelief WithDamage(IReadOnlyList<DamageOutcome> outcomes)
        {
            if (outcomes == null || outcomes.Count == 0)
                return this;

            var result = new double[MAXIMUM + 1];

            foreach (var (hitPoints, probability) in Support)
            {
                foreach (var outcome in outcomes)
                    result[Math.Max(0, hitPoints - outcome.Damage)] += probability * outcome.Probability;
            }

            return new HitPointBelief(result);
        }

        /// <summary>
        /// Shifts the whole belief by a known amount: a repair, or the fixed damage a CO power
        /// deals. Hitting the ceiling is the one thing that makes a unit's hit points <i>more</i>
        /// certain, since everything at or above 80 repairs to exactly 100.
        /// </summary>
        public HitPointBelief WithChange(int delta)
        {
            if (delta == 0)
                return this;

            var result = new double[MAXIMUM + 1];

            foreach (var (hitPoints, probability) in Support)
                result[Math.Clamp(hitPoints + delta, 0, MAXIMUM)] += probability;

            return new HitPointBelief(result);
        }

        /// <summary>
        /// Rules out everything the board disagrees with, and renormalises what is left. This is
        /// where most of the narrowing happens.
        /// </summary>
        /// <remarks>
        /// Returns a plain reading of the displayed figure when nothing survives. That means the
        /// replay did something this does not model, and the honest answer is to admit to knowing
        /// nothing rather than to insist on a belief the game has already contradicted.
        /// </remarks>
        public HitPointBelief ConditionedOnDisplayed(int displayed)
        {
            //Much the most common case: the board says what this already said, so nothing is
            //learnt and nothing needs storing.
            var alreadyAgrees = true;

            foreach (var (hitPoints, _) in Support)
            {
                if (ToDisplayed(hitPoints) == displayed)
                    continue;

                alreadyAgrees = false;
                break;
            }

            if (alreadyAgrees)
                return this;

            var result = new double[MAXIMUM + 1];
            var total = 0d;

            foreach (var (hitPoints, probability) in Support)
            {
                if (ToDisplayed(hitPoints) != displayed)
                    continue;

                result[hitPoints] = probability;
                total += probability;
            }

            if (total <= 0)
                return ForDisplayed(displayed);

            for (var i = 0; i <= MAXIMUM; i++)
                result[i] /= total;

            return new HitPointBelief(result);
        }

        /// <summary>
        /// Two units joining add their hit points together, capped at full health.
        /// </summary>
        public HitPointBelief JoinedWith(HitPointBelief other)
        {
            var result = new double[MAXIMUM + 1];

            foreach (var (mine, mineProbability) in Support)
            {
                foreach (var (theirs, theirsProbability) in other.Support)
                    result[Math.Clamp(mine + theirs, 0, MAXIMUM)] += mineProbability * theirsProbability;
            }

            return new HitPointBelief(result);
        }

        public override string ToString() =>
            IsCertain ? $"{Minimum}" : $"{Minimum}-{Maximum} (mean {Mean:0.0})";

        /// <summary>
        /// How many values are still in play. One means the figure is pinned down; ten means
        /// nothing has been learnt beyond what the board shows.
        /// </summary>
        public int CandidateCount => Support.Count();
    }
}
