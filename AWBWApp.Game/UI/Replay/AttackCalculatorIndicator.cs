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
    /// This lives inside <see cref="Game.Logic.GameMap"/> so that it scales with the camera along
    /// with everything else on the board. The tooltip does not; see <see cref="AttackCalculatorPopup"/>.
    /// </remarks>
    public partial class AttackCalculatorIndicator : CompositeDrawable
    {
        private const float ring_radius = 11;
        private const float ring_thickness = 2;
        private const float line_thickness = 2;
        private const float reticle_thickness = 2;

        private static readonly Color4 no_target_colour = new Color4(235, 235, 235, 255);
        private static readonly Color4 target_colour = new Color4(255, 90, 70, 255);

        private readonly CircularContainer ring;
        private readonly Box line;
        private readonly Container reticle;

        private bool isShowing;

        public AttackCalculatorIndicator()
        {
            Alpha = 0;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;

            InternalChildren = new Drawable[]
            {
                //Drawn first so that it passes under the ring rather than over the attacking unit.
                line = new Box
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.CentreLeft,
                    Height = line_thickness,
                    Width = 0
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
        /// <param name="target">The tile the cursor is currently over.</param>
        /// <param name="targetHasUnit">Whether <paramref name="target"/> holds a unit that could be attacked.</param>
        public void ShowAttack(Vector2I source, Vector2I target, bool targetHasUnit)
        {
            var from = GetTileCentre(source);
            var to = GetTileCentre(target);
            var delta = to - from;

            ring.Position = from;
            reticle.Position = to;

            line.Position = from;
            line.Width = delta.Length;
            line.Rotation = MathHelper.RadiansToDegrees(MathF.Atan2(delta.Y, delta.X));

            //With the cursor still on the attacker the line has nowhere to go, and a stub of it
            //would poke out through the far side of the ring.
            line.Alpha = delta.Length > ring_radius ? 1 : 0;
            reticle.Alpha = targetHasUnit ? 1 : 0;

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

        /// <summary>
        /// The centre of a tile in map space. The tile grid is pushed down by one tile so that
        /// tall tiles have somewhere to overhang, which is where the extra <see cref="DrawableTile.BASE_SIZE"/> comes from.
        /// </summary>
        public static Vector2 GetTileCentre(Vector2I tilePosition) =>
            new Vector2(tilePosition.X * DrawableTile.BASE_SIZE.X + DrawableTile.HALF_BASE_SIZE.X,
                tilePosition.Y * DrawableTile.BASE_SIZE.Y + DrawableTile.BASE_SIZE.Y + DrawableTile.HALF_BASE_SIZE.Y);
    }
}
