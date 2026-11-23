using System;
using AWBWApp.Game.Game.Tile;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace AWBWApp.Game.UI.Replay
{
    /// <summary>
    /// Draws the attack calculator gesture over the map: a ring around the unit the attack is
    /// being measured from, and a line running out to whichever tile the cursor is over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The line bends where a direct unit would stop walking. See <see cref="AttackApproach"/> for
    /// how that tile is chosen; the bend is there so that it is obvious which ground the numbers
    /// in the tooltip are talking about.
    /// </para>
    /// <para>
    /// This lives inside <see cref="Game.Logic.GameMap"/> so that it scales with the camera along
    /// with everything else on the board. The tooltip does not; see <see cref="AttackCalculatorPopup"/>.
    /// </para>
    /// </remarks>
    public partial class AttackCalculatorIndicator : CompositeDrawable
    {
        private const float ring_radius = 11;
        private const float ring_thickness = 2;
        private const float line_thickness = 2;
        private const float reticle_thickness = 2;
        private const float elbow_radius = 3.5f;

        //The walk up to the target is drawn back so that the shot itself stays the louder of the
        //two. They are the same gesture, not two separate things.
        private const float approach_alpha = 0.45f;

        private static readonly Color4 no_target_colour = new Color4(235, 235, 235, 255);
        private static readonly Color4 target_colour = new Color4(255, 90, 70, 255);

        private readonly CircularContainer ring;
        private readonly Box approachLine;
        private readonly Box attackLine;
        private readonly Circle elbow;
        private readonly Container reticle;

        private bool isShowing;

        public AttackCalculatorIndicator()
        {
            Alpha = 0;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;

            InternalChildren = new Drawable[]
            {
                //Drawn first so that they pass under the ring rather than over the attacking unit.
                approachLine = createSegment(),
                attackLine = createSegment(),
                elbow = new Circle
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Size = new Vector2(elbow_radius * 2),
                    Alpha = 0
                },
                ring = new CircularContainer
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Size = new Vector2(ring_radius * 2),
                    Masking = true,
                    BorderThickness = ring_thickness,
                    BorderColour = Color4.White,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true
                    }
                },
                reticle = new Container
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Size = new Vector2(DrawableTile.BASE_SIZE.X, DrawableTile.BASE_SIZE.Y),
                    Masking = true,
                    BorderThickness = reticle_thickness,
                    BorderColour = Color4.White,
                    Alpha = 0,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true
                    }
                }
            };
        }

        //Pure decoration. Letting it take hover would pull it out from under the map's own cursor tracking.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => false;

        /// <param name="source">The tile the attacking unit is standing on.</param>
        /// <param name="attackFrom">
        /// The tile the attack would be launched from. The same as <paramref name="source"/> for an
        /// indirect, or whenever there is nothing to attack.
        /// </param>
        /// <param name="target">The tile the cursor is currently over.</param>
        /// <param name="targetHasUnit">Whether <paramref name="target"/> holds a unit that could be attacked.</param>
        public void ShowAttack(Vector2I source, Vector2I attackFrom, Vector2I target, bool targetHasUnit)
        {
            var from = GetTileCentre(source);
            var via = GetTileCentre(attackFrom);
            var to = GetTileCentre(target);

            ring.Position = from;
            reticle.Position = to;

            setSegment(approachLine, from, via, approach_alpha);
            setSegment(attackLine, via, to, 1);

            //With nowhere to walk to there is no bend, and a dot sitting inside the ring would
            //read as part of the ring rather than as a tile.
            elbow.Position = via;
            elbow.Alpha = attackFrom != source && attackFrom != target ? 1 : 0;

            this.FadeColour(targetHasUnit ? target_colour : no_target_colour, 80, Easing.OutQuint);

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

        private static Box createSegment() =>
            new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.CentreLeft,
                Height = line_thickness,
                Width = 0,
                Alpha = 0
            };

        private static void setSegment(Box segment, Vector2 from, Vector2 to, float alpha)
        {
            var delta = to - from;

            segment.Position = from;
            segment.Width = delta.Length;
            segment.Rotation = MathHelper.RadiansToDegrees(MathF.Atan2(delta.Y, delta.X));

            //A stub shorter than the ring would only poke out through the far side of it.
            segment.Alpha = delta.Length > ring_radius ? alpha : 0;
        }

        /// <summary>
        /// The centre of a tile in map space. The tile grid is pushed down by one tile so that
        /// tall tiles have somewhere to overhang, which is where the extra <see cref="DrawableTile.BASE_SIZE"/> comes from.
        /// </summary>
        public static Vector2 GetTileCentre(Vector2I tilePosition) =>
            new Vector2(tilePosition.X * DrawableTile.BASE_SIZE.X + DrawableTile.HALF_BASE_SIZE.X,
                tilePosition.Y * DrawableTile.BASE_SIZE.Y + DrawableTile.BASE_SIZE.Y + DrawableTile.HALF_BASE_SIZE.Y);
    }
}
