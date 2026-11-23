using System;
using System.Collections.Generic;
using System.Globalization;
using AWBWApp.Game.Game.Country;
using AWBWApp.Game.Game.Damage;
using AWBWApp.Game.Game.Logic;
using AWBWApp.Game.Game.Units;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace AWBWApp.Game.UI.Replay
{
    /// <summary>
    /// The tooltip shown while the attack calculator gesture is pointed at a unit: one column per
    /// side, each ending in where that unit is likely to be left once the dust settles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here is editable. Every figure is read out of the replay at the action being
    /// watched, which is the whole reason this can be simpler than the calculator on the website.
    /// </para>
    /// <para>
    /// It sits beside the camera rather than inside it, so it keeps a constant size no matter how
    /// far the map is zoomed.
    /// </para>
    /// </remarks>
    public partial class AttackCalculatorPopup : Container
    {
        private const float cursor_offset = 24;
        private const float column_spacing = 8;

        [Resolved]
        private DamageCalculator calculator { get; set; }

        private readonly CombatantHeader attackerHeader;
        private readonly CombatantHeader defenderHeader;
        private readonly HitPointRangeLine attackerRange;
        private readonly HitPointRangeLine defenderRange;
        private readonly ContextStrip attackerContext;
        private readonly ContextStrip defenderContext;
        private readonly DamageLine attackerDamage;
        private readonly DamageLine defenderDamage;
        private readonly OutcomeList shownAttackerOutcomes;
        private readonly OutcomeList shownDefenderOutcomes;
        private readonly OutcomeList exactAttackerOutcomes;
        private readonly OutcomeList exactDefenderOutcomes;
        private readonly FillFlowContainer shownSection;
        private readonly FillFlowContainer exactSection;

        private bool isShowing;
        private Vector2 lastScreenSpaceMousePosition;
        private string lastSignature;

        public AttackCalculatorPopup()
        {
            Alpha = 0;
            AutoSizeAxes = Axes.Both;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;
            CornerRadius = 8;
            Masking = true;

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(22, 22, 22, 235)
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Horizontal = 8, Vertical = 7 },
                    Spacing = new Vector2(0, 4),
                    Children = new Drawable[]
                    {
                        createPair(
                            createColumnTitle("Attacker", new Color4(255, 160, 140, 255)),
                            createColumnTitle("Defender", new Color4(140, 190, 255, 255))),
                        createPair(
                            attackerHeader = new CombatantHeader { FacesTowards = FaceDirection.Right },
                            defenderHeader = new CombatantHeader { FacesTowards = FaceDirection.Left }),
                        createPair(
                            attackerRange = new HitPointRangeLine(),
                            defenderRange = new HitPointRangeLine()),
                        createPair(
                            attackerContext = new ContextStrip(),
                            defenderContext = new ContextStrip()),
                        createPair(
                            attackerDamage = new DamageLine(),
                            defenderDamage = new DamageLine()),
                        shownSection = new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 4),
                            Children = new Drawable[]
                            {
                                createSectionLabel("Using calculated HP values"),
                                createPair(
                                    shownAttackerOutcomes = new OutcomeList(),
                                    shownDefenderOutcomes = new OutcomeList())
                            }
                        },
                        exactSection = new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 4),
                            Alpha = 0,
                            Children = new Drawable[]
                            {
                                createSectionLabel("Using true HP"),
                                createPair(
                                    exactAttackerOutcomes = new OutcomeList(),
                                    exactDefenderOutcomes = new OutcomeList())
                            }
                        }
                    }
                }
            };
        }

        //Pure decoration, and it sits right next to the cursor. Taking hover would fight the map.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => false;

        /// <param name="attackFrom">
        /// The tile the attack is launched from. For a direct unit that is wherever the cursor
        /// walked it to rather than where it is standing, and the ground there is what defends it
        /// against the counterattack.
        /// </param>
        public void ShowAttack(ReplayController controller, DrawableUnit attacker, DrawableUnit defender, Vector2I attackFrom, Vector2 screenSpaceMousePosition)
        {
            if (controller == null)
                return;

            //Rebuilding the whole tree every frame would be wasteful, and the answer only moves
            //when one of these does. The believed ranges are in there because stepping through a
            //turn can narrow them without the number on the board ever changing.
            var signature = string.Join('/', attacker.UnitID, defender.UnitID,
                attacker.HealthPoints.Value, defender.HealthPoints.Value,
                attacker.Ammo.Value, defender.Ammo.Value,
                attacker.MapPosition, defender.MapPosition, attackFrom,
                controller.CurrentTurnIndex.Value,
                describe(controller.GetHitPointBelief(attacker.UnitID)),
                describe(controller.GetHitPointBelief(defender.UnitID)),
                describeExact(controller, attacker),
                describeExact(controller, defender));

            if (signature != lastSignature)
            {
                lastSignature = signature;

                if (!rebuild(controller, attacker, defender, attackFrom))
                {
                    HideAttack();
                    return;
                }
            }

            lastScreenSpaceMousePosition = screenSpaceMousePosition;
            updatePosition();

            if (isShowing)
                return;

            isShowing = true;
            this.FadeIn(80, Easing.OutQuint);
        }

        public void HideAttack()
        {
            if (!isShowing)
                return;

            isShowing = false;
            this.FadeOut(120, Easing.OutQuint);
        }

        protected override void Update()
        {
            base.Update();

            //The box only gets its real size once auto-size has run, so the first frame after it
            //is shown would otherwise sit in the wrong place.
            if (isShowing)
                updatePosition();
        }

        private bool rebuild(ReplayController controller, DrawableUnit attacker, DrawableUnit defender, Vector2I attackFrom)
        {
            var situation = controller.CreateAttackSituation(attacker, defender, false, attackFrom);

            if (situation == null)
                return false;

            controller.Players.TryGetValue(attacker.OwnerID!.Value, out var attackingPlayer);
            controller.Players.TryGetValue(defender.OwnerID!.Value, out var defendingPlayer);

            //The attacker's row shows the ground it fires from, not the ground it set off from,
            //so that the sprite and the defence stars under it are talking about the same tile.
            attackerHeader.BindTo(controller, attacker, attackingPlayer, attackFrom);
            defenderHeader.BindTo(controller, defender, defendingPlayer, defender.MapPosition);
            attackerContext.BindTo(situation.Attacker, attackingPlayer);
            defenderContext.BindTo(situation.Defender, defendingPlayer);

            //What a person watching could work out, rather than the top of each unit's tenth. A
            //unit showing 8 is somewhere in 71 to 80, and often somewhere much narrower than that.
            var attackerBelief = controller.GetHitPointBelief(attacker.UnitID) ?? HitPointBelief.ForDisplayed(attacker.HealthPoints.Value);
            var defenderBelief = controller.GetHitPointBelief(defender.UnitID) ?? HitPointBelief.ForDisplayed(defender.HealthPoints.Value);

            var hasExactAttacker = tryGetExactHitPoints(controller, attacker, out var exactAttackerHitPoints);
            var hasExactDefender = tryGetExactHitPoints(controller, defender, out var exactDefenderHitPoints);

            //Said per unit rather than over the section, because the two sides are believed to
            //different degrees and a single span covering both would include values neither
            //of them could be.
            attackerRange.BindTo(attackerBelief, hasExactAttacker ? exactAttackerHitPoints : null);
            defenderRange.BindTo(defenderBelief, hasExactDefender ? exactDefenderHitPoints : null);

            var shown = calculator.ResolveExchange(situation, attackerBelief, defenderBelief);

            attackerDamage.SetDamage(shown.Attack.CanAttack, shown.Attack.MinimumDamage, shown.Attack.MaximumDamage);
            defenderDamage.SetDamage(shown.HasCounterAttack, shown.MinimumCounterDamage, shown.MaximumCounterDamage);

            //A unit with nothing that can touch the defender is not really attacking at all, so
            //there is no exchange to show the ending hit points of.
            shownSection.Alpha = shown.Attack.CanAttack ? 1 : 0;

            setOutcomes(shownAttackerOutcomes, shown.AttackerOutcomes, shown.HasCounterAttack);
            shownDefenderOutcomes.SetOutcomes(shown.DefenderOutcomes);

            //The second section only earns its space when it would say something different. The
            //replay knowing a unit is really at 9.4 often changes nothing about where it lands,
            //and a repeated block of identical bars is just noise.
            var showExact = false;

            if (shown.Attack.CanAttack && hasExactAttacker && hasExactDefender)
            {
                var exact = calculator.ResolveExchange(situation, exactAttackerHitPoints, exactDefenderHitPoints);

                if (!sameOutcomes(shown.AttackerOutcomes, exact.AttackerOutcomes) ||
                    !sameOutcomes(shown.DefenderOutcomes, exact.DefenderOutcomes))
                {
                    setOutcomes(exactAttackerOutcomes, exact.AttackerOutcomes, exact.HasCounterAttack);
                    exactDefenderOutcomes.SetOutcomes(exact.DefenderOutcomes);
                    showExact = true;
                }
            }

            exactSection.Alpha = showExact ? 1 : 0;

            return true;
        }

        /// <summary>
        /// An attacker nothing can hit back at always ends exactly where it started, so a single
        /// bar reading 100% would say nothing and take up room saying it.
        /// </summary>
        private static void setOutcomes(OutcomeList list, IReadOnlyList<HitPointOutcome> outcomes, bool canBeHitBack)
        {
            if (canBeHitBack)
                list.SetOutcomes(outcomes);
            else
                list.ClearOutcomes();
        }

        private static string describe(HitPointBelief belief) => belief == null ? "?" : $"{belief.Minimum}-{belief.Maximum}";

        private static string describeExact(ReplayController controller, DrawableUnit unit) =>
            tryGetExactHitPoints(controller, unit, out var hitPoints) ? hitPoints.ToString(CultureInfo.InvariantCulture) : "?";

        private static bool sameOutcomes(IReadOnlyList<HitPointOutcome> first, IReadOnlyList<HitPointOutcome> second)
        {
            if (first.Count != second.Count)
                return false;

            for (var i = 0; i < first.Count; i++)
            {
                if (first[i].HitPoints != second[i].HitPoints)
                    return false;

                if (Math.Abs(first[i].Probability - second[i].Probability) > 0.000001)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The hit points the replay really holds, on the 0 to 100 scale. False where the replay
        /// never recorded them, or where Sonja is hiding them from everyone.
        /// </summary>
        private static bool tryGetExactHitPoints(ReplayController controller, DrawableUnit unit, out int hitPoints)
        {
            hitPoints = 0;

            if (unit.HideHP || !controller.TryGetExactHitPoints(unit.UnitID, out var exact))
                return false;

            hitPoints = (int)Math.Round(exact * 10);
            return true;
        }

        private static Drawable createPair(Drawable attacker, Drawable defender) =>
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(column_spacing, 0),
                Children = new[] { attacker, defender }
            };

        private static Drawable createColumnTitle(string text, Color4 colour) =>
            new Container
            {
                Width = AttackCalculatorColumn.WIDTH,
                Height = 16,
                Child = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = new FontUsage("Roboto", size: 13, weight: "Bold"),
                    Colour = colour,
                    Text = text
                }
            };

        private static Drawable createSectionLabel(string text) =>
            new Container
            {
                Width = AttackCalculatorColumn.WIDTH * 2 + column_spacing,
                Height = 18,
                Children = new Drawable[]
                {
                    new Box
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        RelativeSizeAxes = Axes.X,
                        Height = 1,
                        Colour = new Color4(70, 70, 70, 255)
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        X = 2,
                        Font = AttackCalculatorColumn.SECTION_FONT,
                        Colour = AttackCalculatorColumn.DIM,
                        Text = text
                    }
                }
            };

        private void updatePosition()
        {
            if (Parent == null)
                return;

            var cursor = Parent.ToLocalSpace(lastScreenSpaceMousePosition);

            var x = cursor.X + cursor_offset;
            var y = cursor.Y + cursor_offset;

            //Flip to the other side of the cursor rather than letting the box run off the screen.
            if (x + DrawWidth > Parent.DrawWidth)
                x = cursor.X - cursor_offset - DrawWidth;
            if (y + DrawHeight > Parent.DrawHeight)
                y = Math.Max(0, cursor.Y - cursor_offset - DrawHeight);

            Position = new Vector2(x, y);
        }
    }
}
