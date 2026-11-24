using System.Collections.Generic;
using System.Linq;
using AWBWApp.Game.Game.COs;
using AWBWApp.Game.Game.Damage;
using AWBWApp.Game.Game.Units;
using AWBWApp.Resources;
using NUnit.Framework;
using osu.Framework.IO.Stores;

namespace AWBWApp.Game.Tests.StorageTests
{
    /// <summary>
    /// Pins the damage maths to the community XHKOCalc spreadsheet the charts were taken from.
    /// </summary>
    /// <remarks>
    /// The headline case is the worked example the spreadsheet itself ships with, so if the
    /// transcription of the charts ever drifts these numbers are what catches it.
    /// </remarks>
    [TestFixture]
    public class TestDamageCalculator
    {
        private DamageChartStorage charts;
        private UnitStorage units;
        private DamageCalculator calculator;

        [SetUp]
        public void SetUp()
        {
            var dllStore = new DllResourceStore(typeof(AWBWAppResources).Assembly);

            charts = new DamageChartStorage();
            units = new UnitStorage();

            using (var stream = dllStore.GetStream("Json/DamageChart.json"))
                charts.LoadStream(stream);
            using (var stream = dllStore.GetStream("Json/Units.json"))
                units.LoadStream(stream);

            calculator = new DamageCalculator(charts);
        }

        /// <summary>
        /// The example saved into XHKOCalc: a Sami tank at 5HP holding one com tower, firing at an
        /// Andy tank at full health sitting in a forest. The spreadsheet's own damage rolls for
        /// that attack run 22, 22, 22, 23, 23, 24, 24, 24, 25, 25 across the ten luck values.
        /// </summary>
        [Test]
        public void MatchesTheSpreadsheetsWorkedExample()
        {
            var result = calculator.Calculate(new AttackSituation
            {
                Attacker = new CombatantState
                {
                    Unit = units.GetUnitByCode("Tank"),
                    HitPoints = 5,
                    Ammo = 9,
                    CO = "Sami",
                    ComTowers = 1
                },
                Defender = new CombatantState
                {
                    Unit = units.GetUnitByCode("Tank"),
                    HitPoints = 10,
                    CO = "Andy",
                    TerrainStars = 2
                }
            });

            Assert.That(result.CanAttack, Is.True);
            Assert.That(result.BaseDamage, Is.EqualTo(55));

            //Sami's -10% against vehicles, cancelled out by the one com tower.
            Assert.That(result.AttackModifier, Is.EqualTo(0));
            Assert.That(result.DefenceModifier, Is.EqualTo(0));

            Assert.That(result.MinimumDamage, Is.EqualTo(22));
            Assert.That(result.MaximumDamage, Is.EqualTo(25));
            Assert.That(result.AverageDamage, Is.EqualTo(23.4).Within(0.0001));

            Assert.That(result.Outcomes.Select(x => x.Damage), Is.EqualTo(new[] { 22, 23, 24, 25 }));
            Assert.That(result.Outcomes.Select(x => x.Probability), Is.EqualTo(new[] { 0.3, 0.2, 0.3, 0.2 }).Within(0.0001));
        }

        [Test]
        public void ProbabilitiesAlwaysSumToOne()
        {
            foreach (var co in new[] { "Andy", "Jugger", "Flak", "Nell", "Sonja", "Rachel" })
            {
                foreach (var power in new[] { ActiveCOPower.None, ActiveCOPower.Normal, ActiveCOPower.Super })
                {
                    var result = calculator.Calculate(situation("Tank", "Infantry", attackerCO: co, attackerPower: power));

                    Assert.That(result.Outcomes.Sum(x => x.Probability), Is.EqualTo(1).Within(0.0001),
                        $"{co} with {power} did not add up");
                }
            }
        }

        /// <summary>
        /// Jugger and Flak roll good luck and bad luck separately, so their spread is weighted
        /// rather than flat. Standard luck is the flat 0 to 9 everyone else gets.
        /// </summary>
        [Test]
        public void LuckSpreadsMatchTheCharts()
        {
            Assert.That(luckBounds("Standard"), Is.EqualTo((0, 9)));
            Assert.That(luckBounds("Jugger"), Is.EqualTo((-14, 29)));
            Assert.That(luckBounds("Jugger-SCOP"), Is.EqualTo((-44, 94)));
            Assert.That(luckBounds("Flak"), Is.EqualTo((-9, 24)));
            Assert.That(luckBounds("Sonja"), Is.EqualTo((-9, 9)));
            Assert.That(luckBounds("Nell-SCOP"), Is.EqualTo((0, 99)));

            //A CO with no entry of its own simply rolls standard luck.
            Assert.That(luckBounds("Andy-SCOP"), Is.EqualTo((0, 9)));

            //Jugger's worst roll is the only way to land on it; the middle is far likelier.
            var jugger = charts.GetLuckRolls("Jugger");
            Assert.That(jugger["-14"], Is.EqualTo(1));
            Assert.That(jugger["0"], Is.EqualTo(15));
        }

        /// <summary>
        /// A unit out of ammunition falls back on its machine gun, which is far weaker against
        /// anything armoured. A unit without one cannot attack at all.
        /// </summary>
        [Test]
        public void RunningOutOfAmmunitionFallsBackToTheSecondaryWeapon()
        {
            var withAmmo = calculator.Calculate(situation("Tank", "Recon", ammo: 9));
            Assert.That(withAmmo.UsedSecondaryWeapon, Is.False);
            Assert.That(withAmmo.BaseDamage, Is.EqualTo(85));

            var without = calculator.Calculate(situation("Tank", "Recon", ammo: 0));
            Assert.That(without.UsedSecondaryWeapon, Is.True);
            Assert.That(without.BaseDamage, Is.EqualTo(40));

            //An artillery carries nothing but its gun.
            Assert.That(calculator.Calculate(situation("Artillery", "Infantry", ammo: 0)).CanAttack, Is.False);

            //Infantry have unlimited ammunition, so zero means nothing to them.
            Assert.That(calculator.Calculate(situation("Infantry", "Infantry", ammo: 0)).CanAttack, Is.True);
        }

        [Test]
        public void WeaponsThatCannotReachAUnitReportNoAttack()
        {
            //There is no row in the chart for infantry shooting at a battleship.
            Assert.That(calculator.Calculate(situation("Infantry", "Battleship")).CanAttack, Is.False);

            //And an APC has no weapon whatsoever.
            Assert.That(calculator.Calculate(situation("APC", "Infantry")).CanAttack, Is.False);
        }

        /// <summary>
        /// The four COs who draw a bonus from the ground they stand on. Lash's is always the
        /// terrain's own defence stars, and doubles under her super.
        /// </summary>
        [Test]
        public void TerrainBonusesMatchTheCharts()
        {
            Assert.That(charts.GetTerrainAttackModifier("Lash", DamageTerrain.Mountain), Is.EqualTo(4));
            Assert.That(charts.GetTerrainAttackModifier("Lash-SCOP", DamageTerrain.Mountain), Is.EqualTo(8));
            Assert.That(charts.GetTerrainAttackModifier("Lash", DamageTerrain.City), Is.EqualTo(3));

            Assert.That(charts.GetTerrainAttackModifier("Kindle", DamageTerrain.City), Is.EqualTo(4));
            Assert.That(charts.GetTerrainAttackModifier("Kindle-COP", DamageTerrain.City), Is.EqualTo(8));
            Assert.That(charts.GetTerrainAttackModifier("Kindle-SCOP", DamageTerrain.City), Is.EqualTo(13));

            //A missile silo is the one piece of urban terrain Kindle gets nothing from.
            Assert.That(charts.GetTerrainAttackModifier("Kindle-SCOP", DamageTerrain.Silo), Is.EqualTo(0));

            Assert.That(charts.GetTerrainAttackModifier("Jake", DamageTerrain.Plains), Is.EqualTo(1));
            Assert.That(charts.GetTerrainAttackModifier("Jake-SCOP", DamageTerrain.Plains), Is.EqualTo(4));

            //Koal's roads stop at the water's edge.
            Assert.That(charts.GetTerrainAttackModifier("Koal", DamageTerrain.Road), Is.EqualTo(1));
            Assert.That(charts.GetTerrainAttackModifier("Koal", DamageTerrain.Bridge), Is.EqualTo(0));

            //Nobody else has a terrain row at all.
            Assert.That(charts.GetTerrainAttackModifier("Andy", DamageTerrain.City), Is.EqualTo(0));
        }

        [Test]
        public void TerrainBonusReachesTheCalculation()
        {
            var onPlains = calculator.Calculate(situation("Tank", "Infantry", attackerCO: "Jake", attackerTerrain: DamageTerrain.Plains));
            var onRoad = calculator.Calculate(situation("Tank", "Infantry", attackerCO: "Jake", attackerTerrain: DamageTerrain.Road));

            Assert.That(onPlains.AttackModifier, Is.EqualTo(1));
            Assert.That(onRoad.AttackModifier, Is.EqualTo(0));
        }

        /// <summary>
        /// The two bonuses no chart could carry, because they depend on how the match is going.
        /// Both only apply under the super.
        /// </summary>
        [Test]
        public void ColinAndKindleScaleWithTheMatchState()
        {
            //+3% per 1000 funds, counting partial thousands, so 17,500 is +52.5%.
            var colin = calculator.Calculate(situation("Tank", "Infantry", attackerCO: "Colin", attackerPower: ActiveCOPower.Super, funds: 17500));
            Assert.That(colin.AttackModifier, Is.EqualTo(5.25).Within(0.0001));

            //Without the super, his funds mean nothing.
            var colinIdle = calculator.Calculate(situation("Tank", "Infantry", attackerCO: "Colin", funds: 17500));
            Assert.That(colinIdle.AttackModifier, Is.EqualTo(-1));

            //+3% per city, plus the +130% from standing on one, plus the universal power boost.
            var kindle = calculator.Calculate(situation("Tank", "Infantry", attackerCO: "Kindle", attackerPower: ActiveCOPower.Super,
                attackerTerrain: DamageTerrain.City, cities: 10));
            Assert.That(kindle.AttackModifier, Is.EqualTo(1 + 13 + 3).Within(0.0001));
        }

        /// <summary>
        /// Javier is the only CO with a bonus against one class of attacker rather than one unit.
        /// </summary>
        [Test]
        public void JavierOnlyDefendsAgainstIndirects()
        {
            var fromArtillery = calculator.Calculate(situation("Artillery", "Tank", defenderCO: "Javier"));
            var fromTank = calculator.Calculate(situation("Tank", "Tank", defenderCO: "Javier"));

            //Javier defends everything by +10%, and indirect fire by a further +20% on top.
            Assert.That(fromTank.DefenceModifier, Is.EqualTo(1));
            Assert.That(fromArtillery.DefenceModifier, Is.EqualTo(3));

            Assert.That(DamageCalculator.IsIndirect(units.GetUnitByCode("Artillery")), Is.True);
            Assert.That(DamageCalculator.IsIndirect(units.GetUnitByCode("Tank")), Is.False);
        }

        /// <summary>
        /// Sonja's day to day is the only thing that makes a counterattack differ from an attack.
        /// </summary>
        [Test]
        public void OnlySonjaCountersHarderThanSheAttacks()
        {
            var sonjaAttacking = calculator.Calculate(situation("Tank", "Tank", attackerCO: "Sonja", defenderCO: "Andy"));
            var sonjaCountering = calculator.Calculate(situation("Tank", "Tank", attackerCO: "Sonja", defenderCO: "Sonja", counter: true));
            var andyCountering = calculator.Calculate(situation("Tank", "Tank", counter: true));
            var andyAttacking = calculator.Calculate(situation("Tank", "Tank"));

            //The multiplier belongs to the CO being attacked, not the one swinging.
            Assert.That(sonjaCountering.AverageDamage, Is.GreaterThan(sonjaAttacking.AverageDamage));
            Assert.That(andyCountering.AverageDamage, Is.EqualTo(andyAttacking.AverageDamage));
        }

        /// <summary>
        /// Terrain protects in proportion to how much of the defender is left, which is why a
        /// wounded unit on a mountain is so much easier to finish off than a fresh one.
        /// </summary>
        [Test]
        public void TerrainProtectsLessAsTheDefenderWeakens()
        {
            var full = calculator.Calculate(situation("Tank", "Tank", defenderHitPoints: 10, stars: 4));
            var hurt = calculator.Calculate(situation("Tank", "Tank", defenderHitPoints: 3, stars: 4));
            var bare = calculator.Calculate(situation("Tank", "Tank", defenderHitPoints: 10, stars: 0));

            Assert.That(hurt.AverageDamage, Is.GreaterThan(full.AverageDamage));
            Assert.That(bare.AverageDamage, Is.GreaterThan(hurt.AverageDamage));
        }

        /// <summary>
        /// A CO the charts have never heard of, "No CO" among them, gets no modifier rather than
        /// throwing.
        /// </summary>
        [Test]
        public void AnUnknownCOSimplyHasNoModifiers()
        {
            var result = calculator.Calculate(situation("Tank", "Infantry", attackerCO: "No CO", defenderCO: null));

            Assert.That(result.CanAttack, Is.True);
            Assert.That(result.AttackModifier, Is.EqualTo(0));
            Assert.That(result.DefenceModifier, Is.EqualTo(0));
        }

        [Test]
        public void ExchangeProbabilitiesAlwaysSumToOne()
        {
            var exchange = calculator.ResolveExchange(situation("Tank", "Tank"), 100, 100);

            Assert.That(exchange.HasCounterAttack, Is.True);
            Assert.That(exchange.AttackerOutcomes.Sum(x => x.Probability), Is.EqualTo(1).Within(0.0001));
            Assert.That(exchange.DefenderOutcomes.Sum(x => x.Probability), Is.EqualTo(1).Within(0.0001));
        }

        /// <summary>
        /// Advance Wars only lets units trade blows in direct combat, so an indirect on either
        /// side of the fight means nothing comes back.
        /// </summary>
        [Test]
        public void IndirectsNeverTradeBlows()
        {
            var fromArtillery = calculator.ResolveExchange(situation("Artillery", "Tank"), 100, 100);
            Assert.That(fromArtillery.HasCounterAttack, Is.False);
            Assert.That(fromArtillery.AttackerOutcomes.Single().HitPoints, Is.EqualTo(10));

            var atArtillery = calculator.ResolveExchange(situation("Tank", "Artillery"), 100, 100);
            Assert.That(atArtillery.HasCounterAttack, Is.False);

            var betweenTanks = calculator.ResolveExchange(situation("Tank", "Tank"), 100, 100);
            Assert.That(betweenTanks.HasCounterAttack, Is.True);
        }

        /// <summary>
        /// An infantry cannot scratch a tank's paint, so the tank always walks away whole, while
        /// the infantry is never safe.
        /// </summary>
        [Test]
        public void TheExchangeLeavesBothUnitsSomewhere()
        {
            var exchange = calculator.ResolveExchange(situation("Tank", "Infantry"), 100, 100);

            Assert.That(exchange.DefenderOutcomes.Max(x => x.HitPoints), Is.LessThan(10));
            Assert.That(exchange.ExpectedDamage, Is.GreaterThan(0));

            //The infantry does hit back, just not for much.
            Assert.That(exchange.HasCounterAttack, Is.True);
            Assert.That(exchange.ExpectedCounterDamage, Is.GreaterThan(0));
            Assert.That(exchange.ExpectedCounterDamage, Is.LessThan(exchange.ExpectedDamage));
        }

        /// <summary>
        /// The whole point of the second, exact section of the tooltip: a unit the game shows at
        /// 10 but the replay has at 9.1 takes the same damage and is left a whole point lower.
        /// </summary>
        [Test]
        public void ExactHitPointsChangeWhereAUnitLandsButNotTheDamage()
        {
            var asShown = calculator.ResolveExchange(situation("Tank", "Tank"), 100, 100);
            var asStored = calculator.ResolveExchange(situation("Tank", "Tank"), 100, 91);

            //Both defenders display as 10, so they are shot at exactly as hard.
            Assert.That(asStored.ExpectedDamage, Is.EqualTo(asShown.ExpectedDamage).Within(0.0001));

            //But the wounded one is left lower.
            Assert.That(asStored.DefenderOutcomes.Max(x => x.HitPoints),
                Is.LessThan(asShown.DefenderOutcomes.Max(x => x.HitPoints)));
        }

        [Test]
        public void AUnitThatCannotAttackLeavesBothSidesUntouched()
        {
            var exchange = calculator.ResolveExchange(situation("APC", "Tank"), 100, 100);

            Assert.That(exchange.Attack.CanAttack, Is.False);
            Assert.That(exchange.HasCounterAttack, Is.False);
            Assert.That(exchange.AttackerOutcomes.Single().HitPoints, Is.EqualTo(10));
            Assert.That(exchange.DefenderOutcomes.Single().HitPoints, Is.EqualTo(10));
        }

        /// <summary>
        /// Everything the charts name must be a unit the rest of the program knows about, or the
        /// name mapping taken from the spreadsheet has drifted.
        /// </summary>
        [Test]
        public void EveryUnitNamedByTheChartsExists()
        {
            var known = units.GetAllUnitIds().Select(units.GetUnitByAWBWId).Select(x => x.Name).ToHashSet();

            foreach (var unit in chartUnitNames())
                Assert.That(known.Contains(unit), Is.True, $"The charts name a unit nothing else knows: {unit}");
        }

        private IEnumerable<string> chartUnitNames()
        {
            //Every attacker row is a unit, bar the secondary weapon rows which are suffixed.
            foreach (var attacker in charts.AttackerNames)
                yield return attacker.EndsWith("-Secondary", System.StringComparison.Ordinal)
                    ? attacker[..^"-Secondary".Length]
                    : attacker;

            foreach (var defender in charts.DefenderNames)
            {
                //A pipe seam is a building the charts let you shoot at, not a unit.
                if (defender != "Pipe Seam")
                    yield return defender;
            }
        }

        /// <summary>
        /// Bad luck can be worth more than a weak attack is, and an attack that goes that badly
        /// does nothing rather than healing what it hit.
        /// </summary>
        [Test]
        public void TheWorstLuckNeverHealsTheDefender()
        {
            //Infantry into a Neotank is about as little damage as the charts hold, and Sonja
            //rolls down to -9 on top of it.
            var result = calculator.Calculate(situation("Infantry", "Neotank", attackerCO: "Sonja", stars: 4));

            Assert.That(result.CanAttack, Is.True);
            Assert.That(result.Outcomes.Select(x => x.Damage), Has.All.GreaterThanOrEqualTo(0));
            Assert.That(result.Outcomes.Select(x => x.Damage), Has.Some.EqualTo(0), "The worst rolls here should come to nothing at all.");
        }

        /// <summary>
        /// Jugger's super rolls down to -44, far past what most attacks deal.
        /// </summary>
        [Test]
        public void JuggersSuperNeverHealsTheDefender()
        {
            var result = calculator.Calculate(situation("Infantry", "Neotank", attackerCO: "Jugger", attackerPower: ActiveCOPower.Super, stars: 4));

            Assert.That(result.Outcomes.Select(x => x.Damage), Has.All.GreaterThanOrEqualTo(0));
        }

        /// <summary>
        /// A variant only carries rows where its numbers differ, so one with none at all has to
        /// behave exactly like the CO it stands in for.
        /// </summary>
        [Test]
        public void AVariantWithNoRowsOfItsOwnFightsLikeItsBase()
        {
            var asKanbei = calculator.Calculate(situation("Tank", "Tank", attackerCO: "Kanbei", defenderCO: "Kanbei"));

            var asVariant = situation("Tank", "Tank", attackerCO: "Pretend Kanbei", defenderCO: "Pretend Kanbei");
            asVariant.Attacker.BaseCO = "Kanbei";
            asVariant.Defender.BaseCO = "Kanbei";

            Assert.That(calculator.Calculate(asVariant).Outcomes.Select(x => x.Damage),
                Is.EqualTo(asKanbei.Outcomes.Select(x => x.Damage)));
        }

        /// <summary>
        /// The rules that single a CO out by name mean the variant too. Sonja's counterattack
        /// bonus is the one that bites: comparing display names would quietly drop it.
        /// </summary>
        [Test]
        public void AVariantOfSonjaStillCountersLikeSonja()
        {
            var counter = situation("Tank", "Tank", attackerCO: "Andy", defenderCO: "DS Sonja", counter: true);
            counter.Defender.BaseCO = "Sonja";

            var asSonja = situation("Tank", "Tank", attackerCO: "Andy", defenderCO: "Sonja", counter: true);
            var asAndy = situation("Tank", "Tank", attackerCO: "Andy", defenderCO: "Andy", counter: true);

            Assert.That(calculator.Calculate(counter).Outcomes.Select(x => x.Damage),
                Is.EqualTo(calculator.Calculate(asSonja).Outcomes.Select(x => x.Damage)));

            Assert.That(calculator.Calculate(counter).Outcomes.Select(x => x.Damage),
                Is.Not.EqualTo(calculator.Calculate(asAndy).Outcomes.Select(x => x.Damage)),
                "If this matches Andy, the counterattack multiplier was lost.");
        }

        /// <summary>
        /// Luck falls back too, which is what keeps a variant of Sonja rolling her weighted dice
        /// rather than a flat 0 to 9.
        /// </summary>
        [Test]
        public void AVariantInheritsItsBasesLuck()
        {
            var variant = situation("Tank", "Tank", attackerCO: "Pretend Sonja");
            variant.Attacker.BaseCO = "Sonja";

            var spread = calculator.Calculate(variant).Outcomes.Count;

            Assert.That(spread, Is.EqualTo(calculator.Calculate(situation("Tank", "Tank", attackerCO: "Sonja")).Outcomes.Count));
            Assert.That(spread, Is.Not.EqualTo(calculator.Calculate(situation("Tank", "Tank", attackerCO: "Andy")).Outcomes.Count));
        }

        /// <summary>
        /// The hand-maintained rows are the ones a regeneration would wipe, so the list naming
        /// them has to stay true to what is actually in the file.
        /// </summary>
        [Test]
        public void EveryHandMaintainedRowIsNamedAndPresent()
        {
            Assert.That(charts.HandMaintainedCORows, Is.Not.Empty);

            foreach (var row in charts.HandMaintainedCORows)
                Assert.That(charts.HasCORow(row), Is.True, $"{row} is listed as hand-maintained but has no row.");

            var coStorage = new COStorage();

            using (var stream = new DllResourceStore(typeof(AWBWAppResources).Assembly).GetStream("Json/COs.json"))
                coStorage.LoadStream(stream);

            //A variant's rows can only have been written by hand, so any that exist must be listed.
            foreach (var co in coStorage.GetAllCOs().Where(x => x.IsVariant))
            {
                foreach (var state in new[] { co.Name, co.Name + "-COP", co.Name + "-SCOP" })
                {
                    if (charts.HasCORow(state))
                        Assert.That(charts.HandMaintainedCORows, Does.Contain(state), $"{state} is a variant row but is not listed as hand-maintained.");
                }
            }
        }

        private (int, int) luckBounds(string coState)
        {
            var rolls = charts.GetLuckRolls(coState).Keys.Select(int.Parse).ToList();
            return (rolls.Min(), rolls.Max());
        }

        private AttackSituation situation(string attacker, string defender, int ammo = 9, string attackerCO = "Andy",
                                          string defenderCO = "Andy", ActiveCOPower attackerPower = ActiveCOPower.None,
                                          string attackerTerrain = null, int stars = 0, int defenderHitPoints = 10,
                                          int funds = 0, int cities = 0, bool counter = false) =>
            new AttackSituation
            {
                Attacker = new CombatantState
                {
                    Unit = units.GetUnitByCode(attacker),
                    HitPoints = 10,
                    Ammo = ammo,
                    CO = attackerCO,
                    Power = attackerPower,
                    Funds = funds,
                    Cities = cities,
                    Terrain = attackerTerrain
                },
                Defender = new CombatantState
                {
                    Unit = units.GetUnitByCode(defender),
                    HitPoints = defenderHitPoints,
                    Ammo = 9,
                    CO = defenderCO,
                    TerrainStars = stars
                },
                IsCounterAttack = counter
            };
    }
}
