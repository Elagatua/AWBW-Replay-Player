using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AWBWApp.Game.Game.COs;
using AWBWApp.Game.Game.Units;

namespace AWBWApp.Game.Game.Damage
{
    /// <summary>
    /// Works out what one unit would do to another, following the formula the community
    /// XHKOCalc spreadsheet encodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one place in the program that computes a game rule rather than reading an
    /// outcome the replay already states, and it exists only to answer a question the replay does
    /// not: what *would* happen. Nothing here is ever used to decide what *did* happen.
    /// </para>
    /// <para>
    /// The formula, with every bonus in tenths:
    /// <c>trunc((base * (1 + attack/10) + luck) * (attackerHP / 10) * (1 - stars/10 * defenderHP/10 - defence/10) + 0.05)</c>
    /// </para>
    /// </remarks>
    public class DamageCalculator
    {
        /// <summary>
        /// Sonja's day to day, the one thing that makes a counterattack differ from an attack.
        /// </summary>
        /// <remarks>
        /// The spreadsheet models no counterattacks at all, so unlike everything else here this
        /// multiplier is not pinned to it. It is applied as firepower, alongside the CO bonuses
        /// and before luck, which is the reading that matches "counterattacks do 1.5x damage".
        /// </remarks>
        private const double sonja_counter_multiplier = 1.5;

        private const string sonja = "Sonja";
        private const string colin = "Colin";
        private const string kindle = "Kindle";
        private const string secondary_weapon_suffix = "-Secondary";

        private readonly DamageChartStorage charts;

        public DamageCalculator(DamageChartStorage charts)
        {
            this.charts = charts;
        }

        public DamageResult Calculate(AttackSituation situation)
        {
            var attacker = situation.Attacker;
            var defender = situation.Defender;

            if (attacker?.Unit == null || defender?.Unit == null)
                return DamageResult.CANNOT_ATTACK;

            if (!tryChooseWeapon(attacker, defender, out var baseDamage, out var usedSecondary))
                return DamageResult.CANNOT_ATTACK;

            var attackModifier = charts.GetAttackModifier(attacker.COState, attacker.Unit.Name, attacker.BaseCOState)
                                 + charts.GetTerrainAttackModifier(attacker.COState, attacker.Terrain, attacker.BaseCOState)
                                 + attacker.ComTowers
                                 + getStateDependentAttackModifier(attacker);

            var defenceModifier = charts.GetDefenceModifier(defender.COState, defender.Unit.Name, defender.BaseCOState);

            //Javier's whole gimmick, and the only entry in that table.
            if (IsIndirect(attacker.Unit))
                defenceModifier += charts.GetDefenceModifierAgainstIndirects(defender.COState, defender.BaseCOState);

            var firepower = 1 + attackModifier * 0.1;

            if (situation.IsCounterAttack && defender.IsCO(sonja))
                firepower *= sonja_counter_multiplier;

            //A unit at 3 hit points hits at three tenths strength.
            var attackerStrength = Math.Clamp(attacker.HitPoints, 0, 10) / 10.0;

            //Terrain protects in proportion to how much of the defender is left, which is why a
            //wounded unit on a mountain is so much easier to finish off than a fresh one.
            var defenderStrength = Math.Clamp(defender.HitPoints, 0, 10);
            var resistance = 1 - defender.TerrainStars * 0.1 * defenderStrength * 0.1 - defenceModifier * 0.1;

            //Bad luck can be worth more than the attack itself: Sonja rolls down to -9, and
            //Jugger's super down to -44, which is more than a weak attack deals in the first
            //place. An attack that goes that badly simply does nothing, rather than healing what
            //it hit.
            int damageForLuck(int luck) =>
                Math.Max(0, (int)Math.Truncate((baseDamage * firepower + luck) * attackerStrength * resistance + 0.05));

            var outcomes = buildOutcomes(charts.GetLuckRolls(attacker.COState, attacker.BaseCOState), damageForLuck);

            return new DamageResult
            {
                CanAttack = true,
                UsedSecondaryWeapon = usedSecondary,
                BaseDamage = baseDamage,
                AttackModifier = attackModifier,
                DefenceModifier = defenceModifier,
                TerrainStars = defender.TerrainStars,
                MinimumDamage = outcomes[0].Damage,
                MaximumDamage = outcomes[^1].Damage,
                AverageDamage = outcomes.Sum(x => x.Damage * x.Probability),
                Outcomes = outcomes
            };
        }

        /// <summary>
        /// Resolves the whole exchange: the attack, then the counterattack from whatever the
        /// defender has left, and the spread of hit points each unit is left holding.
        /// </summary>
        /// <param name="situation">The fight, whose hit points are ignored in favour of the two below.</param>
        /// <param name="attackerHitPoints">The attacker's hit points on the replay's 0 to 100 scale.</param>
        /// <param name="defenderHitPoints">The defender's hit points on the replay's 0 to 100 scale.</param>
        public AttackExchange ResolveExchange(AttackSituation situation, int attackerHitPoints, int defenderHitPoints)
        {
            var attackerStart = Math.Clamp(attackerHitPoints, 0, 100);
            var defenderStart = Math.Clamp(defenderHitPoints, 0, 100);

            var opening = new AttackSituation
            {
                Attacker = situation.Attacker.WithHitPoints(ToDisplayHitPoints(attackerStart)),
                Defender = situation.Defender.WithHitPoints(ToDisplayHitPoints(defenderStart)),
                IsCounterAttack = false
            };

            var attack = Calculate(opening);

            if (!attack.CanAttack)
            {
                return new AttackExchange
                {
                    Attack = attack,
                    AttackerOutcomes = single(ToDisplayHitPoints(attackerStart)),
                    DefenderOutcomes = single(ToDisplayHitPoints(defenderStart))
                };
            }

            //Advance Wars only lets a unit hit back in direct combat. An artillery firing from
            //three tiles away takes nothing in return, and cannot fire back at something already
            //standing next to it.
            var counterPossible = !IsIndirect(situation.Attacker.Unit) && !IsIndirect(situation.Defender.Unit);

            var attackerOutcomes = new Dictionary<int, double>();
            var defenderOutcomes = new Dictionary<int, double>();
            var expectedCounterDamage = 0d;
            var countered = false;
            var minimumCounter = int.MaxValue;
            var maximumCounter = 0;

            foreach (var outcome in attack.Outcomes)
            {
                var defenderLeft = Math.Max(0, defenderStart - outcome.Damage);
                add(defenderOutcomes, ToDisplayHitPoints(defenderLeft), outcome.Probability);

                //Nothing hits back from the grave, and the formula agrees: a unit at zero hit
                //points swings at zero strength.
                if (!counterPossible || defenderLeft <= 0)
                {
                    add(attackerOutcomes, ToDisplayHitPoints(attackerStart), outcome.Probability);
                    continue;
                }

                var counterSituation = opening.Reversed();
                counterSituation.Attacker = counterSituation.Attacker.WithHitPoints(ToDisplayHitPoints(defenderLeft));

                var counter = Calculate(counterSituation);

                if (!counter.CanAttack)
                {
                    add(attackerOutcomes, ToDisplayHitPoints(attackerStart), outcome.Probability);
                    continue;
                }

                countered = true;
                minimumCounter = Math.Min(minimumCounter, counter.MinimumDamage);
                maximumCounter = Math.Max(maximumCounter, counter.MaximumDamage);

                foreach (var counterOutcome in counter.Outcomes)
                {
                    var probability = outcome.Probability * counterOutcome.Probability;

                    add(attackerOutcomes, ToDisplayHitPoints(Math.Max(0, attackerStart - counterOutcome.Damage)), probability);
                    expectedCounterDamage += counterOutcome.Damage * probability;
                }
            }

            return new AttackExchange
            {
                Attack = attack,
                HasCounterAttack = countered,
                ExpectedCounterDamage = expectedCounterDamage,
                MinimumCounterDamage = countered ? minimumCounter : 0,
                MaximumCounterDamage = maximumCounter,
                AttackerOutcomes = sorted(attackerOutcomes),
                DefenderOutcomes = sorted(defenderOutcomes)
            };
        }

        /// <summary>
        /// Resolves the exchange across everything each unit's hit points could be, weighted by
        /// how likely each is.
        /// </summary>
        /// <remarks>
        /// Every value a belief still allows shares one displayed figure, so the damage rolls are
        /// the same throughout and only the subtraction differs. The counterattack does not follow
        /// suit - it is launched from whatever the defender has left, which is exactly what varies
        /// here - so each starting pair has to be resolved on its own and the results mixed.
        /// </remarks>
        public AttackExchange ResolveExchange(AttackSituation situation, HitPointBelief attacker, HitPointBelief defender)
        {
            var attackerOutcomes = new Dictionary<int, double>();
            var defenderOutcomes = new Dictionary<int, double>();

            var attack = DamageResult.CANNOT_ATTACK;
            var countered = false;
            var expectedCounterDamage = 0d;
            var minimumCounter = int.MaxValue;
            var maximumCounter = 0;

            foreach (var (attackerHitPoints, attackerProbability) in attacker.Support)
            {
                foreach (var (defenderHitPoints, defenderProbability) in defender.Support)
                {
                    var weight = attackerProbability * defenderProbability;
                    var exchange = ResolveExchange(situation, attackerHitPoints, defenderHitPoints);

                    attack = exchange.Attack;

                    if (exchange.HasCounterAttack)
                    {
                        countered = true;
                        expectedCounterDamage += exchange.ExpectedCounterDamage * weight;
                        minimumCounter = Math.Min(minimumCounter, exchange.MinimumCounterDamage);
                        maximumCounter = Math.Max(maximumCounter, exchange.MaximumCounterDamage);
                    }

                    foreach (var outcome in exchange.AttackerOutcomes)
                        add(attackerOutcomes, outcome.HitPoints, outcome.Probability * weight);

                    foreach (var outcome in exchange.DefenderOutcomes)
                        add(defenderOutcomes, outcome.HitPoints, outcome.Probability * weight);
                }
            }

            return new AttackExchange
            {
                Attack = attack,
                HasCounterAttack = countered,
                ExpectedCounterDamage = expectedCounterDamage,
                MinimumCounterDamage = countered ? minimumCounter : 0,
                MaximumCounterDamage = maximumCounter,
                AttackerOutcomes = sorted(attackerOutcomes),
                DefenderOutcomes = sorted(defenderOutcomes)
            };
        }

        /// <summary>
        /// The whole number of hit points the game shows for a unit the replay has on its 0 to 100
        /// scale. Always rounded up, so anything still alive shows at least one.
        /// </summary>
        public static int ToDisplayHitPoints(int hitPoints) => (int)Math.Ceiling(Math.Clamp(hitPoints, 0, 100) / 10.0);

        /// <summary>
        /// A unit that fires from further than the tile next to it. These are the units Javier
        /// defends against and Grit shoots with, and the ones that never trade blows.
        /// </summary>
        public static bool IsIndirect(UnitData unit) => unit.AttackRange.X > 1;

        /// <summary>
        /// The two bonuses no chart can carry, because they depend on how the match is going
        /// rather than on who is fighting.
        /// </summary>
        private static double getStateDependentAttackModifier(CombatantState state)
        {
            if (state.Power != ActiveCOPower.Super)
                return 0;

            //Colin's Power of Money: +3% for every 1000 funds in the bank, scaling continuously -
            //a partial thousand pays out its share, as the website's own calculator does.
            if (state.IsCO(colin))
                return state.Funds / 1000.0 * 0.3;

            //Kindle's High Society: +3% for every city held, on top of her urban bonus. AWBW
            //counts cities alone here, where the home games counted every property.
            if (state.IsCO(kindle))
                return state.Cities * 0.3;

            return 0;
        }

        /// <summary>
        /// Picks between the ammunition weapon and the machine gun, and reports whether either can
        /// touch the defender.
        /// </summary>
        /// <remarks>
        /// A unit reaches for its primary whenever it has shells and the chart says that weapon can
        /// hit the target. Failing either, it falls back on a secondary if it has one - and a unit
        /// with neither, such as an artillery out of ammunition, simply cannot attack.
        /// </remarks>
        private bool tryChooseWeapon(CombatantState attacker, CombatantState defender, out int baseDamage, out bool usedSecondary)
        {
            usedSecondary = false;

            var hasAmmunition = attacker.Unit.MaxAmmo <= 0 || attacker.Ammo > 0;

            var primary = hasAmmunition ? charts.GetBaseDamage(attacker.Unit.Name, defender.Unit.Name) : null;

            if (primary != null)
            {
                baseDamage = primary.Value;
                return true;
            }

            var secondary = charts.GetBaseDamage(attacker.Unit.Name + secondary_weapon_suffix, defender.Unit.Name);

            if (secondary != null)
            {
                baseDamage = secondary.Value;
                usedSecondary = true;
                return true;
            }

            baseDamage = 0;
            return false;
        }

        /// <summary>
        /// Collapses the luck rolls into one entry per distinct amount of damage. Several rolls
        /// usually land on the same number, and a CO like Jugger weights its rolls unevenly.
        /// </summary>
        private static List<DamageOutcome> buildOutcomes(IReadOnlyDictionary<string, int> rolls, Func<int, int> damageForLuck)
        {
            var weightByDamage = new Dictionary<int, int>();
            var total = 0;

            foreach (var (luck, weight) in rolls)
            {
                var damage = damageForLuck(int.Parse(luck, CultureInfo.InvariantCulture));

                weightByDamage.TryGetValue(damage, out var existing);
                weightByDamage[damage] = existing + weight;
                total += weight;
            }

            return weightByDamage.OrderBy(x => x.Key)
                                 .Select(x => new DamageOutcome(x.Key, x.Value / (double)total))
                                 .ToList();
        }

        private static void add(IDictionary<int, double> outcomes, int hitPoints, double probability)
        {
            outcomes.TryGetValue(hitPoints, out var existing);
            outcomes[hitPoints] = existing + probability;
        }

        private static List<HitPointOutcome> sorted(Dictionary<int, double> outcomes) =>
            outcomes.OrderBy(x => x.Key).Select(x => new HitPointOutcome(x.Key, x.Value)).ToList();

        private static List<HitPointOutcome> single(int hitPoints) =>
            new List<HitPointOutcome> { new HitPointOutcome(hitPoints, 1) };
    }
}
