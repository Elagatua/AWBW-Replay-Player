using System;
using System.Globalization;
using AWBWApp.Game.Game.Building;
using AWBWApp.Game.Game.Logic;
using AWBWApp.Game.Game.Tile;
using AWBWApp.Game.Game.Units;
using AWBWApp.Game.Helpers;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace AWBWApp.Game.UI.Replay
{
    public partial class DetailedInformationPopup : Container
    {
        private bool forceRightSide;

        public bool ForceRightSide
        {
            get => forceRightSide;
            set
            {
                forceRightSide = value;
                popupPositionChanged();
            }
        }

        private TerrainPopup terrainPopup;
        private BuildingPopup buildingPopup;
        private UnitPopup unitPopup;

        /// <summary>
        /// The replay being shown, used to recover exact hit points. Null outside a replay, such as in
        /// the editor, where the popup falls back to the whole number everything else displays.
        /// </summary>
        public ReplayController ReplayController
        {
            get => unitPopup.ReplayController;
            set => unitPopup.ReplayController = value;
        }

        private Bindable<bool> playerListLeftSide;
        private Bindable<float> playerListScale;
        private Bindable<Anchor> infoPopupAnchor;

        public DetailedInformationPopup()
        {
            AutoSizeAxes = Axes.X;
            CornerRadius = 8;
            Height = 130;
            AutoSizeDuration = 250;
            AutoSizeEasing = Easing.OutQuint;
            Masking = true;

            Anchor = Anchor.BottomLeft;
            Origin = Anchor.BottomLeft;
            Position = new Vector2(10, -10);

            Children = new Drawable[]
            {
                new Box()
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(25, 25, 25, 180),
                },
                new FillFlowContainer()
                {
                    RelativeSizeAxes = Axes.Y,
                    AutoSizeAxes = Axes.X,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(2, 0),
                    Children = new Drawable[]
                    {
                        terrainPopup = new TerrainPopup
                        {
                            Alpha = 0
                        },
                        buildingPopup = new BuildingPopup
                        {
                            Alpha = 0
                        },
                        unitPopup = new UnitPopup
                        {
                            Alpha = 0
                        },
                    }
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load(AWBWConfigManager configManager)
        {
            playerListLeftSide = configManager.GetBindable<bool>(AWBWSetting.PlayerListLeftSide);
            playerListLeftSide.BindValueChanged(_ => popupPositionChanged());

            playerListScale = configManager.GetBindable<float>(AWBWSetting.PlayerListScale);
            playerListScale.BindValueChanged(_ => popupPositionChanged());

            infoPopupAnchor = configManager.GetBindable<Anchor>(AWBWSetting.TileInfoPopupAnchor);
            infoPopupAnchor.BindValueChanged(_ => popupPositionChanged(), true);
        }

        private void popupPositionChanged()
        {
            if (ForceRightSide)
            {
                Anchor = Anchor.BottomRight;
                Origin = Anchor.BottomRight;
                Position = new Vector2(-10, -10);
                return;
            }

            Anchor = infoPopupAnchor.Value;
            Origin = infoPopupAnchor.Value;

            var leftSide = playerListLeftSide.Value;

            var listOffset = ReplayController.PLAYER_LIST_WIDTH * playerListScale.Value + 10;

            Vector2 offset = infoPopupAnchor.Value switch
            {
                Anchor.TopLeft => new Vector2(leftSide ? listOffset : 10, 10),
                Anchor.TopCentre => new Vector2(0, 10),
                Anchor.TopRight => new Vector2(leftSide ? -10 : -listOffset, 10),
                Anchor.CentreLeft => new Vector2(leftSide ? listOffset : 10, 0),
                Anchor.Centre => new Vector2(0, 0),
                Anchor.CentreRight => new Vector2(leftSide ? -10 : -listOffset, 0),
                Anchor.BottomLeft => new Vector2(leftSide ? listOffset : 10, -10),
                Anchor.BottomCentre => new Vector2(0, -10),
                Anchor.BottomRight => new Vector2(leftSide ? -10 : -listOffset, -10),
                _ => throw new ArgumentOutOfRangeException()
            };
            Position = offset;
        }

        public void ShowDetails(DrawableTile tile, DrawableBuilding building, DrawableUnit unit)
        {
            if (building != null)
            {
                buildingPopup.BindTo(building);
                terrainPopup.Reset();
            }
            else if (tile != null)
            {
                terrainPopup.BindTo(tile.TerrainTile);
                buildingPopup.Reset();
            }
            else
            {
                terrainPopup.Reset();
                buildingPopup.Reset();
            }

            if (unit != null)
                unitPopup.BindTo(unit);
            else
                unitPopup.Reset();
        }

        public void ShowDetails(TerrainTile tile, BuildingTile building)
        {
            if (building != null)
            {
                buildingPopup.BindTo(building);
                terrainPopup.Reset();
            }
            else if (tile != null)
            {
                terrainPopup.BindTo(tile);
                buildingPopup.Reset();
            }
            else
            {
                terrainPopup.Reset();
                buildingPopup.Reset();
            }

            unitPopup.Reset();
        }

        private partial class TerrainPopup : Container
        {
            private Sprite terrainSprite;
            private StatContainer terrainStarCounter;

            private TerrainTile boundToData;

            [Resolved]
            private NearestNeighbourTextureStore textureStore { get; set; }

            [Resolved]
            private IBindable<MapSkin> currentSkin { get; set; }

            public TerrainPopup()
            {
                Anchor = Anchor.CentreLeft;
                Origin = Anchor.CentreLeft;
                AutoSizeAxes = Axes.Y;
                Width = 50;
                Masking = true;
                CornerRadius = 8;
                Children = new Drawable[]
                {
                    new Box()
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(20, 20, 20, 150)
                    },
                    new FillFlowContainer()
                    {
                        AutoSizeAxes = Axes.Y,
                        RelativeSizeAxes = Axes.X,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Top = 10, Bottom = 5 },
                        Children = new Drawable[]
                        {
                            new Container()
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                AutoSizeAxes = Axes.Both,
                                Child = terrainSprite = new Sprite()
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Size = new Vector2(32)
                                },
                            },
                            terrainStarCounter = new StatContainer("UI/TerrainStar"),
                        }
                    }
                };
            }

            public void BindTo(TerrainTile tile)
            {
                if (tile == boundToData)
                    return;

                boundToData = tile;

                terrainSprite.Texture = textureStore.Get($"Map/{currentSkin.Value}/{boundToData.Textures[WeatherType.Clear]}");
                terrainSprite.Size = terrainSprite.Texture.Size * 2;
                Show();
                terrainStarCounter.SetTo(boundToData.BaseDefence);
            }

            public void Reset()
            {
                Hide();
                terrainStarCounter.SetTo(0);
                boundToData = null;
            }
        }

        private partial class BuildingPopup : Container
        {
            private Container spriteContainer;
            private StatContainer terrainStarCounter;
            private StatContainer buildingHPCounter;

            private DrawableBuilding boundToBuilding;
            private BuildingTile boundToData;

            [Resolved]
            private BuildingStorage buildingStorage { get; set; }

            [Resolved]
            private NearestNeighbourTextureStore textureStore { get; set; }

            [Resolved]
            private IBindable<MapSkin> currentSkin { get; set; }

            [Resolved]
            private IBindable<BuildingSkin> currentBuildingSkin { get; set; }

            public BuildingPopup()
            {
                Anchor = Anchor.CentreLeft;
                Origin = Anchor.CentreLeft;
                AutoSizeAxes = Axes.Y;
                Width = 50;
                Masking = true;
                CornerRadius = 8;
                Children = new Drawable[]
                {
                    new Box()
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(20, 20, 20, 150)
                    },
                    new FillFlowContainer()
                    {
                        AutoSizeAxes = Axes.Y,
                        RelativeSizeAxes = Axes.X,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Top = 10, Bottom = 5 },
                        Children = new Drawable[]
                        {
                            spriteContainer = new Container()
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                AutoSizeAxes = Axes.Both
                            },
                            terrainStarCounter = new StatContainer("UI/TerrainStar"),
                            buildingHPCounter = new StatContainer("UI/BuildingsCaptured")
                        }
                    }
                };
            }

            public void BindTo(DrawableBuilding building)
            {
                if (building == boundToBuilding)
                    return;

                boundToBuilding = building;
                boundToData = building.BuildingTile;
                setup();
            }

            public void BindTo(BuildingTile buildingData)
            {
                if (buildingData == boundToData)
                    return;

                boundToBuilding = null;
                boundToData = buildingData;
                setup();
            }

            private void setup()
            {
                if (boundToData.CountryID != -1)
                {
                    var country = boundToBuilding?.GetCurrentCountry();
                    if (country != null)
                        boundToData = buildingStorage.GetBuildingByTypeAndCountry(boundToData.BuildingType, country.AWBWID);
                }

                var unitSprite = new TextureAnimation()
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                };
                spriteContainer.Child = unitSprite;

                var firstTexture = textureStore.Get($"Map/{currentBuildingSkin.Value}/{boundToData.Textures[WeatherType.Clear]}-0");
                unitSprite.Size = firstTexture.Size * 2;

                if (boundToData.Frames != null)
                {
                    unitSprite.AddFrame(firstTexture, boundToData.Frames[0]);
                    for (int i = 1; i < boundToData.Frames.Length; i++)
                        unitSprite.AddFrame(textureStore.Get($"Map/{currentBuildingSkin.Value}/{boundToData.Textures[WeatherType.Clear]}-{i}"), boundToData.Frames[i]);
                }
                else
                    unitSprite.AddFrame(firstTexture);

                Show();
                terrainStarCounter.SetTo(boundToData.BaseDefence);

                if (boundToBuilding != null)
                    buildingHPCounter.BindTo(boundToBuilding?.CaptureHealth);
                else
                    buildingHPCounter.SetTo(20);
            }

            public void Reset()
            {
                Hide();
                terrainStarCounter.SetTo(0);
                buildingHPCounter.SetTo(0);
                boundToBuilding = null;
            }
        }

        private partial class UnitPopup : Container
        {
            private const float minimum_width = 50;

            private Container spriteContainer;
            private StatContainer unitHPCounter;
            private StatContainer unitHighestHPCounter;
            private StatContainer unitLowestHPCounter;
            private StatContainer unitAmmoCounter;
            private StatContainer unitFuelCounter;

            private DrawableUnit boundToUnit;

            public ReplayController ReplayController { get; set; }

            private Bindable<HitPointDisplay> hitPointDisplay;

            [Resolved]
            private NearestNeighbourTextureStore textureStore { get; set; }

            [BackgroundDependencyLoader]
            private void load(AWBWConfigManager configManager)
            {
                hitPointDisplay = configManager.GetBindable<HitPointDisplay>(AWBWSetting.TileInfoHitPoints);
            }

            public UnitPopup()
            {
                Anchor = Anchor.CentreLeft;
                Origin = Anchor.CentreLeft;
                AutoSizeAxes = Axes.Y;
                Width = minimum_width;
                Masking = true;
                CornerRadius = 8;
                Children = new Drawable[]
                {
                    new Box()
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(20, 20, 20, 150)
                    },
                    new FillFlowContainer()
                    {
                        AutoSizeAxes = Axes.Y,
                        RelativeSizeAxes = Axes.X,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Top = 10, Bottom = 5 },
                        Children = new Drawable[]
                        {
                            spriteContainer = new Container()
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                AutoSizeAxes = Axes.Both
                            },
                            unitHPCounter = new StatContainer("UI/HP"),
                            //Only one of these is ever up at a time: either the single figure above,
                            //or the two ends of the range. An alpha of zero takes a row out of the
                            //flow entirely, so the popup closes up around whichever is hidden.
                            unitHighestHPCounter = new StatContainer("UI/HP", StatSign.Most) { Alpha = 0 },
                            unitLowestHPCounter = new StatContainer("UI/HP", StatSign.Least) { Alpha = 0 },
                            unitAmmoCounter = new StatContainer("UI/Ammo"),
                            unitFuelCounter = new StatContainer("UI/Fuel"),
                        }
                    }
                };
            }

            public void BindTo(DrawableUnit unit)
            {
                if (unit == boundToUnit)
                    return;

                boundToUnit = unit;

                var unitSprite = new TextureAnimation()
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                };
                spriteContainer.Child = unitSprite;

                textureStore.LoadIntoAnimation($"{unit.Country.UnitPath}/{unit.UnitData.IdleAnimation.Texture}", unitSprite, unit.UnitData.IdleAnimation.Frames, unit.UnitData.IdleAnimation.FrameOffset);
                unitSprite.Size *= 2;

                Show();
                updateHitPoints(unit);
                unitAmmoCounter.BindTo(unit.Ammo);
                unitFuelCounter.BindTo(unit.Fuel);
            }

            protected override void Update()
            {
                base.Update();

                //Hit points are re-read every frame rather than bound, because what is known about
                //them depends on how far through the turn the replay is, not just on the unit.
                if (boundToUnit != null)
                    updateHitPoints(boundToUnit);

                //Hit points carry a decimal place, so this popup is no longer a fixed width. The parent
                //auto-sizes, so growing here widens the whole popup smoothly.
                var hitPointWidth = Math.Max(widthOf(unitHPCounter), Math.Max(widthOf(unitHighestHPCounter), widthOf(unitLowestHPCounter)));

                Width = Math.Max(minimum_width, Math.Max(hitPointWidth, Math.Max(unitAmmoCounter.RequiredWidth, unitFuelCounter.RequiredWidth)));
            }

            private static float widthOf(StatContainer counter) => counter.Alpha > 0 ? counter.RequiredWidth : 0;

            /// <summary>
            /// What this popup is willing to say about a unit's hit points, which is a setting: the
            /// whole number the game shows, the range a person watching could narrow it to, or the
            /// figure the replay privately records.
            /// </summary>
            private void updateHitPoints(DrawableUnit unit)
            {
                //Sonja's day to day hides unit health. The board already respects this; so should this.
                if (unit.HideHP)
                {
                    showOneFigure("?");
                    return;
                }

                var nominal = unit.HealthPoints.Value.ToString(CultureInfo.InvariantCulture);

                //Anything past the number the game itself shows only exists inside a replay.
                if (ReplayController == null || hitPointDisplay?.Value == HitPointDisplay.Nominal)
                {
                    showOneFigure(nominal);
                    return;
                }

                if (hitPointDisplay?.Value == HitPointDisplay.True)
                {
                    if (ReplayController.TryGetExactHitPoints(unit.UnitID, out var exact))
                        showOneFigure(exact.ToString("0.0", CultureInfo.InvariantCulture));
                    else
                        //All that is known is the ceiling the rest of the game displays, so a unit
                        //shown as "10.?" is really somewhere in 9.1 - 10.0.
                        showOneFigure($"{nominal}.?");

                    return;
                }

                var belief = ReplayController.GetHitPointBelief(unit.UnitID);

                //Nothing has been worked out about this unit, so there is nothing to add to the board.
                if (belief == null)
                {
                    showOneFigure(nominal);
                    return;
                }

                //Nothing has chipped away at this one, so both ends of the range are the same number
                //and two rows would be two rows saying the same thing.
                if (belief.Minimum == belief.Maximum)
                {
                    showOneFigure(formatHitPoints(belief.Maximum));
                    return;
                }

                showRange(formatHitPoints(belief.Maximum), formatHitPoints(belief.Minimum));
            }

            /// <summary>
            /// A whole tenth is written the way the board writes it. Anything else carries its
            /// decimal, because that is the whole reason it is being shown.
            /// </summary>
            private static string formatHitPoints(int hitPoints) =>
                hitPoints % 10 == 0
                    ? (hitPoints / 10).ToString(CultureInfo.InvariantCulture)
                    : (hitPoints / 10.0).ToString("0.0", CultureInfo.InvariantCulture);

            private void showOneFigure(string text)
            {
                unitHPCounter.SetText(text);
                unitHPCounter.Alpha = 1;
                unitHighestHPCounter.Alpha = 0;
                unitLowestHPCounter.Alpha = 0;
            }

            private void showRange(string most, string least)
            {
                unitHighestHPCounter.SetText(most);
                unitLowestHPCounter.SetText(least);
                unitHPCounter.Alpha = 0;
                unitHighestHPCounter.Alpha = 1;
                unitLowestHPCounter.Alpha = 1;
            }

            public void Reset()
            {
                Hide();
                showOneFigure(string.Empty);
                unitAmmoCounter.SetTo(0);
                unitFuelCounter.SetTo(0);
                boundToUnit = null;
            }
        }

        /// <summary>
        /// Which end of a range a row is showing, marked on its icon.
        /// </summary>
        private enum StatSign
        {
            None,
            Most,
            Least
        }

        private partial class StatContainer : Container
        {
            private const float edge_padding = 4;
            private const float icon_size = 22;
            private const float icon_to_text_spacing = 2;
            private const float badge_size = 11;
            private const float badge_bar_length = 7;
            private const float badge_bar_thickness = 2;

            private Sprite sprite;
            private string texturePath;

            private RollingCounter<int> rollingCounter;
            private SpriteText textCounter;

            private IBindable<int> bindableReference;

            public StatContainer(string texturePath, StatSign sign = StatSign.None)
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;

                this.texturePath = texturePath;

                Children = new Drawable[]
                {
                    new Container()
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(icon_size),
                        Position = new Vector2(edge_padding, 0),
                        Children = new Drawable[]
                        {
                            sprite = new Sprite()
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                            },
                            createBadge(sign)
                        }
                    },
                    rollingCounter = new RollingCounter<int>()
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Position = new Vector2(-edge_padding, 0),
                        Font = new FontUsage("Roboto", weight: "Bold")
                    },
                    //Used instead of the rolling counter for stats that are not plain whole numbers,
                    //such as hit points, which carry a decimal place and can be partly unknown.
                    textCounter = new SpriteText()
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Position = new Vector2(-edge_padding, 0),
                        Font = new FontUsage("Roboto", weight: "Bold"),
                        Alpha = 0
                    }
                };
            }

            /// <summary>
            /// How wide this row needs to be for its icon and its value not to overlap. The icon is
            /// pinned left and the value right, so a value that grows - "10.0" rather than "10" - runs
            /// into the icon once the row is too narrow for both.
            /// </summary>
            public float RequiredWidth
            {
                get
                {
                    var valueWidth = textCounter.Alpha > 0 ? textCounter.DrawWidth : rollingCounter.DrawWidth;
                    return edge_padding + icon_size + icon_to_text_spacing + valueWidth + edge_padding;
                }
            }

            /// <summary>
            /// The plus or minus sitting on the corner of the icon, telling the two ends of a range
            /// apart. Drawn rather than stored as its own texture, so that it keeps its edges at
            /// whatever size the popup is scaled to.
            /// </summary>
            private static Drawable createBadge(StatSign sign)
            {
                if (sign == StatSign.None)
                    return Empty();

                return new Container
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Position = new Vector2(1, 1),
                    Size = new Vector2(badge_size),
                    Masking = true,
                    CornerRadius = badge_size / 2,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(20, 20, 20, 230)
                        },
                        new Box
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(badge_bar_length, badge_bar_thickness)
                        },
                        new Box
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(badge_bar_thickness, badge_bar_length),
                            Alpha = sign == StatSign.Most ? 1 : 0
                        }
                    }
                };
            }

            [BackgroundDependencyLoader]
            private void load(NearestNeighbourTextureStore textureStore)
            {
                sprite.Texture = textureStore.Get(texturePath);
                sprite.Size = sprite.Texture.Size * 2;
            }

            public void BindTo(Bindable<int> stat)
            {
                bindableReference = stat.GetBoundCopy();
                bindableReference.BindValueChanged(x => rollingCounter.Current.Value = x.NewValue, true);

                rollingCounter.Alpha = 1;
                textCounter.Alpha = 0;
            }

            public void SetTo(int stat)
            {
                bindableReference = null;
                rollingCounter.Current.Value = stat;

                rollingCounter.Alpha = 1;
                textCounter.Alpha = 0;
            }

            public void SetText(string stat)
            {
                bindableReference = null;

                if (textCounter.Text != stat)
                    textCounter.Text = stat;

                rollingCounter.Alpha = 0;
                textCounter.Alpha = 1;
            }
        }
    }
}
