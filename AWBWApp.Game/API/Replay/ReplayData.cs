using System;
using System.Collections.Generic;
using System.ComponentModel;
using osu.Framework.Graphics.Primitives;

namespace AWBWApp.Game.API.Replay
{
    public class ReplayData
    {
        public ReplayInfo ReplayInfo = new ReplayInfo();
        public List<TurnData> TurnData = new List<TurnData>();

        /// <summary>
        /// Pushes each player's chosen CO variants onto every turn, so that asking a turn which CO
        /// was played gives the same answer wherever it is asked.
        /// </summary>
        /// <remarks>
        /// Call this after the choices change, and run the setup pass again: a CO's day to day
        /// feeds unit value and property income, which are worked out up front.
        /// </remarks>
        public void ApplyCOVariantChoices()
        {
            foreach (var turn in TurnData)
            {
                if (turn.Players == null)
                    continue;

                foreach (var playerTurn in turn.Players)
                {
                    if (!ReplayInfo.Players.TryGetValue(playerTurn.Key, out var player))
                        continue;

                    playerTurn.Value.COVariantName = chosenCO(player, playerTurn.Value.ActiveCOID);
                    playerTurn.Value.TagCOVariantName = playerTurn.Value.TagCOID.HasValue ? chosenCO(player, playerTurn.Value.TagCOID.Value) : null;
                }
            }
        }

        private static string chosenCO(ReplayUser player, int coID)
            => player.COVariants != null && player.COVariants.TryGetValue(coID, out var name) ? name : null;
    }

    public class ReplayInfo
    {
        public long ID;
        public string Name;
        public string UserDefinedName;

        public string Password;
        public long CreatorId;

        public long MapId;
        public Dictionary<long, ReplayUser> Players;

        public int FundsPerBuilding;
        public int StartingFunds;
        public bool Fog;
        public bool PowersAllowed;
        public string WeatherType;

        //Unnessecary data on the Game
        //Todo: Remove when double checked
        public bool OfficialGame;
        public MatchType Type;
        public string LeagueMatch;
        public bool TeamMatch;
        public int ReplayVersion;

        public int? CaptureWinBuildingNumber;

        //Unknown if this data is per turn or not
        public DateTime StartDate;
        public DateTime EndDate;

        public string GetDisplayName()
        {
            return UserDefinedName ?? Name;
        }

        /// <summary>
        /// Copies across everything the user chose by hand, which reading the replay again cannot
        /// recover because the file never said it.
        /// </summary>
        public void CarryOverUserChoicesFrom(ReplayInfo previous)
        {
            if (previous == null)
                return;

            UserDefinedName ??= previous.UserDefinedName;

            if (previous.Players == null || Players == null)
                return;

            foreach (var player in Players)
            {
                if (previous.Players.TryGetValue(player.Key, out var previousPlayer))
                    player.Value.COVariants ??= previousPlayer.COVariants;
            }
        }
    }

    public enum MatchType
    {
        League,
        [Description("Live Queue")]
        LiveQueue,
        [Description("Live League")]
        LiveLeague,
        Normal,
        Tag,
        Unknown
    }

    public class TurnData
    {
        public int Day;
        public long ActivePlayerID = -1;
        public string ActiveTeam;

        public bool Active; //Todo: "active" What is this?

        public ReplayWeather StartWeather;

        public Dictionary<long, ReplayUserTurn> Players;
        public List<IReplayAction> Actions;

        //Should these be ID based?
        public Dictionary<Vector2I, ReplayBuilding> Buildings;
        public Dictionary<long, ReplayUnit> ReplayUnit;

        //Todo: Do we need to handle this as a special case on turn start? Replay 554175
        public bool DrawWasAccepted;
    }
}
