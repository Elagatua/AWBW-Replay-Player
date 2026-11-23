using AWBWApp.Game.Game.Units;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace AWBWApp.Game.UI.Replay
{
    /// <summary>
    /// The tooltip shown while the attack calculator gesture is pointed at a unit.
    /// </summary>
    /// <remarks>
    /// Deliberately empty of numbers for now: this is the placeholder the damage calculation will
    /// fill in. It sits beside the camera rather than inside it, so that it keeps a constant size
    /// no matter how far the map is zoomed.
    /// </remarks>
    public partial class AttackCalculatorPopup : Container
    {
        private const float cursor_offset = 24;

        private readonly SpriteText matchupText;

        private bool isShowing;
        private Vector2 lastScreenSpaceMousePosition;

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
                    Colour = new Color4(25, 25, 25, 220)
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Horizontal = 10, Vertical = 8 },
                    Spacing = new Vector2(0, 3),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Font = new FontUsage("Roboto", size: 15, weight: "Bold"),
                            Colour = new Color4(255, 160, 140, 255),
                            Text = "Attack Preview"
                        },
                        matchupText = new SpriteText
                        {
                            Font = new FontUsage("Roboto", size: 14)
                        },
                        new SpriteText
                        {
                            Font = new FontUsage("Roboto", size: 12),
                            Colour = new Color4(160, 160, 160, 255),
                            Text = "Damage, counter and KO odds to come."
                        }
                    }
                }
            };
        }

        //Pure decoration, and it sits right next to the cursor. Taking hover would fight the map.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => false;

        public void ShowAttack(DrawableUnit attacker, DrawableUnit defender, Vector2 screenSpaceMousePosition)
        {
            matchupText.Text = $"{attacker.UnitData.Name} -> {defender.UnitData.Name}";
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

            //The box only gets its real size once auto-size has run, so the first frame after it is
            //shown would otherwise sit in the wrong place.
            if (isShowing)
                updatePosition();
        }

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
                y = cursor.Y - cursor_offset - DrawHeight;

            Position = new Vector2(x, y);
        }
    }
}
