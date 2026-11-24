using AWBWApp.Game.Game.COs;

namespace AWBWApp.Game.API.Replay
{
    /// <summary>
    /// Reads a player's CO out of a replay turn, rather than off the id alone.
    /// </summary>
    /// <remarks>
    /// A tournament variant shares the id of the CO it stands in for, so the id by itself is not
    /// the whole answer. What a turn also carries is whether the player had a super power to
    /// charge, which is enough to recognise the variants that lost one.
    /// </remarks>
    public static class ReplayCOExtensions
    {
        /// <summary>
        /// The CO this player was playing on this turn.
        /// </summary>
        public static COData GetCOPlayedBy(this COStorage storage, ReplayUserTurn turn)
            => storage.GetCOPlayed(turn.ActiveCOID, turn.RequiredPowerForSuper.HasValue);

        /// <summary>
        /// The CO this player had waiting in a tag match, or null where there is none.
        /// </summary>
        public static COData GetTagCOPlayedBy(this COStorage storage, ReplayUserTurn turn)
            => turn.TagCOID.HasValue ? storage.GetCOPlayed(turn.TagCOID.Value, turn.TagRequiredPowerForSuper.HasValue) : null;
    }
}
