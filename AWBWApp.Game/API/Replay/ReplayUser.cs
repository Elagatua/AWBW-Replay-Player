using System.Collections.Generic;
using AWBWApp.Game.Game.COs;

namespace AWBWApp.Game.API.Replay
{
    public class ReplayUser
    {
        public long ID;
        public long UserId;
        public string Username; //This needs to be filled in by asking AWBW

        public string TeamName;
        public int CountryID;

        public HashSet<int> COsUsedByPlayer = new HashSet<int>();

        /// <summary>
        /// Which of <see cref="COsUsedByPlayer"/> the replay gave no super power to charge.
        /// </summary>
        /// <remarks>
        /// This is how a CO that lost a power gives itself away, and the only thing a replay says
        /// that tells a tournament variant apart from the CO it stands in for. Recorded here, on
        /// data that is kept in the replay index, so the replay list can name the right CO without
        /// reading the whole replay back.
        /// <para>
        /// Null rather than empty means the replay was indexed before this was recorded, not that
        /// every CO had a super power. <see cref="IO.ReplayManager"/> reads it back in to decide
        /// which entries need parsing again.
        /// </para>
        /// </remarks>
        public HashSet<int> COsWithoutSuperPower;

        /// <summary>
        /// Which CO this player was really using, for the ones the replay cannot tell apart,
        /// keyed by the CO id the replay reports.
        /// </summary>
        /// <remarks>
        /// A tournament variant that keeps its base's powers, such as the Mini Amarriner Cup's DS
        /// reworks, is identical to it in everything a replay records. Only the person watching
        /// knows, so they say, once per replay, and it is kept here with the rest of the replay's
        /// index entry. Null or missing means nobody has said, and the CO is worked out from the
        /// replay as far as it can be.
        /// </remarks>
        public Dictionary<int, string> COVariants;

        public int ReplayIndex;
        public int RoundOrder;
        public int? EliminatedOn;

        public string GetUIFriendlyUsername()
        {
            return Username ?? $"[Unknown Username:{UserId}";
        }
    }

    public class ReplayUserTurn
    {
        public long ID; //Just to make sure things stay the same
        public int Funds;

        public int ActiveCOID;
        public int Power;
        public double PowerPercentage;
        public int? RequiredPowerForNormal; //This changes every time we use a power.
        public int? RequiredPowerForSuper; //This changes every time we use a power.

        public int? TagCOID;
        public int? TagPower;
        public int? TagRequiredPowerForNormal; //This changes every time we use a power.
        public int? TagRequiredPowerForSuper; //This changes every time we use a power.

        /// <summary>
        /// The CO the user said was really being played, or null to work it out from the replay.
        /// Filled in from <see cref="ReplayUser.COVariants"/>, so that asking a turn which CO was
        /// played gives the same answer everywhere.
        /// </summary>
        public string COVariantName;

        public string TagCOVariantName;

        public ActiveCOPower COPowerOn;
    
        public int? Clock; //how much time player has left at start of the turn in seconds
        public bool Eliminated;

        public void Copy(ReplayUserTurn other)
        {
            ID = other.ID;
            Funds = other.Funds;

            ActiveCOID = other.ActiveCOID;
            COVariantName = other.COVariantName;
            TagCOVariantName = other.TagCOVariantName;
            Power = other.Power;
            RequiredPowerForNormal = other.RequiredPowerForNormal;
            RequiredPowerForSuper = other.RequiredPowerForSuper;

            TagCOID = other.TagCOID;
            TagPower = other.TagPower;
            TagRequiredPowerForNormal = other.TagRequiredPowerForNormal;
            TagRequiredPowerForSuper = other.TagRequiredPowerForSuper;

            Clock = other.Clock;
            Eliminated = other.Eliminated;
        }

        public ReplayUserTurn Clone()
        {
            var clone = new ReplayUserTurn();
            clone.Copy(this);
            return clone;
        }
    }
}
