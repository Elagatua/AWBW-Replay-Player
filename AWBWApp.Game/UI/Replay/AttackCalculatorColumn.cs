using System;
using System.Collections.Generic;
using System.Globalization;
using AWBWApp.Game.Game.COs;
using AWBWApp.Game.Game.Country;
using AWBWApp.Game.Game.Damage;
using AWBWApp.Game.Game.Logic;
using AWBWApp.Game.Game.Tile;
using AWBWApp.Game.Game.Units;
using AWBWApp.Game.Helpers;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace AWBWApp.Game.UI.Replay
{
    /// <summary>
    /// The pieces one column of the attack calculator is built from. Each is a fixed width so the
    /// attacker and defender line up down the whole tooltip.
    /// </summary>
    public static class AttackCalculatorColumn
    {
        public const float WIDTH = 176;

        public static readonly FontUsage LABEL_FONT = new FontUsage("Roboto", size: 11);
        public static readonly FontUsage VALUE_FONT = new FontUsage("Roboto", size: 12, weight: "Bold");
        public static readonly FontUsage SECTION_FONT = new FontUsage("Roboto", size: 12, weight: "Bold");
        public static readonly FontUsage OUTCOME_FONT = new FontUsage("Roboto", size: 13);
        public static readonly FontUsage OUTCOME_HP_FONT = new FontUsage("Roboto", size: 15, weight: "Bold");

        public static readonly Color4 DIM = new Color4(150, 150, 150, 255);
        public static readonly Color4 DESTROYED = new Color4(230, 85, 70, 255);

        private static readonly Color4 never = new Color4(230, 85, 70, 255);
        private static readonly Color4 even_odds = new Color4(235, 200, 70, 255);
        private static readonly Color4 certain = new Color4(110, 210, 110, 255);

        /// <summary>
        /// How likely something is, as a colour: red at nothing, yellow at even odds, green at
        /// certain, and a straight line through each half.
        /// </summary>
        /// <remarks>
        /// This says nothing about whether the outcome is a good one. A column full of red is a
        /// spread with no favourite in it, not a column full of bad news - the left of each row
        /// still says which outcome it is. The one row this is not asked about is a wreck, which
        /// is always drawn in <see cref="DESTROYED"/>.
        /// </remarks>
        public static Color4 ForLikelihood(double probability)
        {
            var amount = (float)Math.Clamp(probability, 0, 1);

            return amount < 0.5f
                ? mix(never, even_odds, amount * 2)
                : mix(even_odds, certain, (amount - 0.5f) * 2);
        }

        private static Color4 mix(Color4 from, Color4 to, float amount) =>
            new Color4(from.R + (to.R - from.R) * amount,
                from.G + (to.G - from.G) * amount,
                from.B + (to.B - from.B) * amount,
                1);
    }

    /// <summary>
    /// The top row: who is fighting, what they are driving and what they are standing on.
    /// </summary>
    public partial class CombatantHeader : Container
    {
        private const float portrait_size = 34;
        private const float sprite_size = 32;

        private readonly Sprite coSprite;
        private readonly TextureAnimation unitAnimation;
        private readonly TextureSpriteText healthText;
        private readonly Sprite terrainSprite;

        [Resolved]
        private NearestNeighbourTextureStore textureStore { get; set; }

        [Resolved]
        private IBindable<MapSkin> mapSkin { get; set; }

        [Resolved]
        private IBindable<BuildingSkin> buildingSkin { get; set; }

        /// <summary>
        /// Which way this unit should be turned, so that the two columns face each other however
        /// they happen to be standing on the board.
        /// </summary>
        public FaceDirection FacesTowards { get; init; } = FaceDirection.Right;

        public CombatantHeader()
        {
            Width = AttackCalculatorColumn.WIDTH;
            AutoSizeAxes = Axes.Y;

            Child = new FillFlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(6, 0),
                //Just enough room for the hit points to hang below the unit, and no more.
                Padding = new MarginPadding { Bottom = 2 },
                Children = new Drawable[]
                {
                    new Container
                    {
                        Size = new Vector2(portrait_size),
                        Masking = true,
                        CornerRadius = 3,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Child = coSprite = new Sprite
                        {
                            RelativeSizeAxes = Axes.Both,
                            FillMode = FillMode.Fit
                        }
                    },
                    new Container
                    {
                        Size = new Vector2(sprite_size),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        //A tile's worth of map, drawn at twice the size. Everything inside uses the
                        //same numbers DrawableUnit does, so the hit points keep the proportions and
                        //the slight overhang below the unit that they have on the board.
                        Child = new Container
                        {
                            Size = new Vector2(DrawableUnit.BASE_SIZE.X, DrawableUnit.BASE_SIZE.Y),
                            Scale = new Vector2(2),
                            Children = new Drawable[]
                            {
                                unitAnimation = new TextureAnimation
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre
                                },
                                healthText = new TextureSpriteText("UI/Healthv2")
                                {
                                    Anchor = Anchor.BottomRight,
                                    Origin = Anchor.BottomRight,
                                    Font = new FontUsage(size: 2.25f),
                                    //DrawableUnit uses (1, 1). The extra half sits inside a
                                    //container drawn at 2x, so it lands exactly one screen pixel
                                    //lower and nothing ends up on a half pixel.
                                    Position = new Vector2(1, 1.5f)
                                }
                            }
                        }
                    },
                    //A fixed cell, so a narrow sprite cannot pull the row's width about either.
                    new Container
                    {
                        Size = new Vector2(sprite_size),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Child = terrainSprite = new Sprite
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre
                        }
                    }
                }
            };
        }

        /// <param name="position">
        /// The tile to draw under the unit, which for an attacker is the one it fires from rather
        /// than the one it is standing on.
        /// </param>
        public void BindTo(ReplayController controller, DrawableUnit unit, PlayerInfo player, Vector2I position)
        {
            var coName = player?.ActiveCO.Value.CO?.Name;
            coSprite.Texture = coName != null ? textureStore.Get($"CO/{coName}-Small") : null;

            //The animation has to already be in the tree before its frames are swapped out, or
            //clearing the old ones throws on a drawable that was never loaded.
            textureStore.LoadIntoAnimation($"{unit.Country.UnitPath}/{unit.UnitData.IdleAnimation.Texture}", unitAnimation,
                unit.UnitData.IdleAnimation.Frames, unit.UnitData.IdleAnimation.FrameOffset);

            //Turn the two columns to face each other, whichever way they happen to be standing on
            //the board. The sprite is centred, so flipping it needs no change of origin.
            unitAnimation.Scale = new Vector2(FacesTowards == unit.Country.FaceDirection ? 1 : -1, 1);

            //Sonja's units keep their secret here too; the board already hides them.
            if (unit.HideHP)
                healthText.Text = "?";
            else
                healthText.Text = unit.HealthPoints.Value >= 10 ? "" : unit.HealthPoints.Value.ToString(CultureInfo.InvariantCulture);

            var terrain = getTerrainTexture(controller, position);
            terrainSprite.Texture = terrain;

            if (terrain != null)
            {
                //Mountains and the taller properties are drawn higher than a tile and overhang
                //upwards on the board, where there is room for it. Here they come down to a tile's
                //height instead, so that every row is the same height whatever is standing where.
                var scale = MathF.Min(2, sprite_size / terrain.Height);
                terrainSprite.Size = terrain.Size * scale;
            }
        }

        private osu.Framework.Graphics.Textures.Texture getTerrainTexture(ReplayController controller, Vector2I position)
        {
            if (controller.Map.TryGetDrawableBuilding(position, out var building))
                return textureStore.Get($"Map/{buildingSkin.Value}/{building.BuildingTile.Textures[WeatherType.Clear]}-0");

            var tile = controller.Map.GetDrawableTile(position).TerrainTile;
            return textureStore.Get($"Map/{mapSkin.Value}/{tile.Textures[WeatherType.Clear]}");
        }
    }

    /// <summary>
    /// What is known about this unit's hit points: the span the belief leaves open, and the figure
    /// the replay privately holds where there is one.
    /// </summary>
    /// <remarks>
    /// This is the one row that says how much of the rest of the tooltip is guesswork, so it sits
    /// directly under the unit it is talking about rather than over the section it feeds.
    /// </remarks>
    public partial class HitPointRangeLine : Container
    {
        private const float height = 16;

        private readonly SpriteText label;

        public HitPointRangeLine()
        {
            Width = AttackCalculatorColumn.WIDTH;
            Height = height;

            Child = label = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = AttackCalculatorColumn.OUTCOME_FONT,
                Colour = AttackCalculatorColumn.DIM
            };
        }

        /// <param name="belief">What can be worked out about this unit's hit points.</param>
        /// <param name="trueHitPoints">
        /// The figure the replay really holds, where it has one and is not hiding it.
        /// </param>
        public void BindTo(HitPointBelief belief, int? trueHitPoints)
        {
            //A belief narrowed to a single value has nothing left for the replay's own figure to
            //add, because they are necessarily the same number.
            if (belief.Minimum == belief.Maximum)
            {
                label.Text = $"HP: {belief.Minimum}%";
                return;
            }

            var text = $"HP range: {belief.Minimum}% - {belief.Maximum}%";

            if (trueHitPoints.HasValue)
                text += $", True HP: {trueHitPoints.Value}%";

            label.Text = text;
        }
    }

    /// <summary>
    /// Everything about this side's position that changes the numbers below, and nothing that
    /// does not.
    /// </summary>
    public partial class ContextStrip : Container
    {
        private const float height = 15;

        private readonly FillFlowContainer chips;

        [Resolved]
        private NearestNeighbourTextureStore textureStore { get; set; }

        public ContextStrip()
        {
            Width = AttackCalculatorColumn.WIDTH;
            Height = height;

            Child = chips = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0)
            };
        }

        public void BindTo(CombatantState state, PlayerInfo player)
        {
            chips.Clear();

            switch (player?.ActivePower.Value ?? ActiveCOPower.None)
            {
                //The UI/NormalPower and UI/SuperPower textures the player list uses are wide text
                //banners, which shrink to an unreadable smear at this size. These are the square
                //icons meant for it.
                case ActiveCOPower.Normal:
                    chips.Add(createChip("UI/NormalPowerIcon", null));
                    break;

                case ActiveCOPower.Super:
                    chips.Add(createChip("UI/SuperPowerIcon", null));
                    break;
            }

            if (state.TerrainStars > 0)
                chips.Add(createChip("UI/TerrainStar", state.TerrainStars.ToString(CultureInfo.InvariantCulture)));

            if (state.ComTowers > 0)
                chips.Add(createChip("UI/commtowericon", state.ComTowers.ToString(CultureInfo.InvariantCulture)));

            //Only Kindle's super counts cities, and only Colin's cares what is in the bank. Showing
            //either for anyone else would suggest it changes a number that it does not.
            if (state.IsCO("Kindle"))
                chips.Add(createChip("UI/BuildingsCaptured", state.Cities.ToString(CultureInfo.InvariantCulture)));

            if (state.IsCO("Colin"))
                chips.Add(createChip("UI/Coin", FormatFunds(state.Funds)));
        }

        /// <summary>
        /// Funds rounded to the nearest hundred and written in thousands, so the strip does not
        /// jump about in width as the numbers grow. Display only; the maths uses the real figure.
        /// </summary>
        public static string FormatFunds(int funds)
        {
            if (funds < 1000)
                return funds.ToString(CultureInfo.InvariantCulture);

            return (funds / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
        }

        private Drawable createChip(string texturePath, string text)
        {
            var texture = textureStore.Get(texturePath);

            var chip = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(2, 0),
                Child = new Sprite
                {
                    //Drawn at its own size. These are pixel art of several different sizes, and
                    //forcing them all into one box smears the smaller ones.
                    Size = texture?.Size ?? Vector2.Zero,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Texture = texture
                }
            };

            if (text != null)
            {
                chip.Add(new SpriteText
                {
                    Font = AttackCalculatorColumn.VALUE_FONT,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = text
                });
            }

            return chip;
        }
    }

    /// <summary>
    /// The spread of damage this side can do, which is the headline figure of the whole tooltip.
    /// </summary>
    /// <remarks>
    /// Written as a percentage, which is how AWBW states damage and how the replay stores it: 49%
    /// is 4.9 hit points. The average is deliberately absent - it can be read off the bars below,
    /// and the range is what a player is actually weighing up.
    /// </remarks>
    public partial class DamageLine : Container
    {
        private readonly SpriteText range;

        public DamageLine()
        {
            Width = AttackCalculatorColumn.WIDTH;
            Height = 26;

            Child = range = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = new FontUsage("Roboto", size: 21, weight: "Bold")
            };
        }

        public void SetDamage(bool dealsDamage, int minimum, int maximum)
        {
            if (!dealsDamage)
            {
                range.Text = "-";
                range.Colour = AttackCalculatorColumn.DIM;
                return;
            }

            range.Colour = Color4.White;
            range.Text = minimum == maximum
                ? $"{minimum.ToString(CultureInfo.InvariantCulture)}%"
                : $"{minimum.ToString(CultureInfo.InvariantCulture)}% - {maximum.ToString(CultureInfo.InvariantCulture)}%";
        }
    }

    /// <summary>
    /// Where a unit is likely to be left: one bar per surviving hit point total, worst at the bottom.
    /// </summary>
    public partial class OutcomeList : FillFlowContainer
    {
        public OutcomeList()
        {
            Width = AttackCalculatorColumn.WIDTH;
            AutoSizeAxes = Axes.Y;
            Direction = FillDirection.Vertical;
            Spacing = new Vector2(0, 1);
        }

        public void SetOutcomes(IReadOnlyList<HitPointOutcome> outcomes)
        {
            Clear();

            //Highest first, so reading down the column is watching the unit get worse off.
            for (var i = outcomes.Count - 1; i >= 0; i--)
                Add(new OutcomeBar(outcomes[i]));
        }

        /// <summary>
        /// Leaves the column empty but still holding its width, so the other column does not slide
        /// across to fill the gap.
        /// </summary>
        public void ClearOutcomes() => Clear();
    }

    public partial class OutcomeBar : Container
    {
        private const float bar_left = 46;
        private const float bar_width = 80;
        private const float bar_height = 8;
        private const float hit_points_gap = 4;
        private const float probability_gap = 10;

        public OutcomeBar(HitPointOutcome outcome)
        {
            Width = AttackCalculatorColumn.WIDTH;
            Height = 16;

            var destroyed = outcome.HitPoints <= 0;

            //Every surviving outcome is coloured by how likely it is rather than by what it is.
            //A wreck is the exception: it stays red however long the odds, because the one row in
            //the column that matters at a glance should not have to be read to be noticed.
            var colour = destroyed ? AttackCalculatorColumn.DESTROYED : AttackCalculatorColumn.ForLikelihood(outcome.Probability);

            Children = new Drawable[]
            {
                //Right aligned against the bar, so the gap stays the same whether the row says
                //"10 HP" or "KO".
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreRight,
                    X = bar_left - hit_points_gap,
                    Font = AttackCalculatorColumn.OUTCOME_HP_FONT,
                    Colour = destroyed ? AttackCalculatorColumn.DESTROYED : Color4.White,
                    Text = destroyed ? "KO" : outcome.HitPoints.ToString(CultureInfo.InvariantCulture) + " HP"
                },
                new Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = bar_left,
                    Size = new Vector2(bar_width, bar_height),
                    Colour = new Color4(60, 60, 60, 255)
                },
                new Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = bar_left,
                    //A one-in-a-thousand chance still deserves a sliver, or the bar reads as zero.
                    Size = new Vector2(MathF.Max(1, bar_width * (float)outcome.Probability), bar_height),
                    Colour = colour
                },
                //Left aligned off the end of the bar, so every figure starts at the same place
                //rather than drifting with its own length.
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = bar_left + bar_width + probability_gap,
                    Font = AttackCalculatorColumn.OUTCOME_FONT,
                    Colour = colour,
                    Text = FormatProbability(outcome.Probability)
                }
            };
        }

        /// <summary>
        /// A whole number loses its decimal, so a certainty reads "100%" rather than "100.0%".
        /// </summary>
        /// <remarks>
        /// Anything that could still happen keeps a figure of its own rather than rounding away to
        /// nothing, because "0%" and "cannot happen" are very different things to a player.
        /// </remarks>
        public static string FormatProbability(double probability)
        {
            var percent = probability * 100;

            if (percent > 0 && percent < 0.05)
                return "<0.1%";

            return percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }
    }
}
