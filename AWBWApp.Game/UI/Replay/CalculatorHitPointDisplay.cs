using System.ComponentModel;

namespace AWBWApp.Game.UI.Replay
{
    /// <summary>
    /// How much the attack calculator says about hit points.
    /// </summary>
    /// <remarks>
    /// Close to <see cref="HitPointDisplay"/>, and deliberately not the same thing. The calculator
    /// can show two answers at once, one above the other, which is a choice the tile info popup
    /// does not have - and an option that did nothing there would be worse than a second enum.
    /// </remarks>
    public enum CalculatorHitPointDisplay
    {
        /// <summary>
        /// The whole numbers the board shows, with every unit taken to be sitting exactly on its
        /// tenth. What this tooltip did before it learnt to track anything.
        /// </summary>
        [Description("Nominal")]
        Nominal,

        /// <summary>
        /// The range a person watching the game could have narrowed each unit to, and nothing the
        /// replay privately knows.
        /// </summary>
        [Description("Possible Range")]
        Possible,

        /// <summary>
        /// The figures the replay records, which nobody playing the game would have had.
        /// </summary>
        [Description("True")]
        True,

        /// <summary>
        /// Both of the last two, one section above the other, so that what could be worked out can
        /// be read against what was really the case.
        /// </summary>
        [Description("Both")]
        Both
    }
}
