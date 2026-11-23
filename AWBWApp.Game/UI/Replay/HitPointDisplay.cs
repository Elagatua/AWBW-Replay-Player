using System.ComponentModel;

namespace AWBWApp.Game.UI.Replay
{
    /// <summary>
    /// How much to say about a unit's hit points. The tile info popup and the attack calculator
    /// each have their own setting of this, because wanting the plain figure while hovering a
    /// unit says nothing about what is wanted from a tooltip whose whole subject is uncertainty.
    /// </summary>
    public enum HitPointDisplay
    {
        /// <summary>
        /// The whole number the game itself shows, and nothing more.
        /// </summary>
        [Description("Nominal")]
        Nominal,

        /// <summary>
        /// The most and least the unit could be holding, worked out from what a person watching
        /// the game could know. See <see cref="Game.Damage.HitPointBelief"/>.
        /// </summary>
        [Description("Possible Range")]
        Possible,

        /// <summary>
        /// The figure the replay privately records, which nobody playing the game would have.
        /// </summary>
        [Description("True")]
        True
    }
}
