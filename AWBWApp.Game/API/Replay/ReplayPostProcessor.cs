using System;
using AWBWApp.Game.API.Replay.Actions;
using AWBWApp.Game.Game.Logic;

namespace AWBWApp.Game.API.Replay
{
    public static class ReplayPostProcessor
    {
        /// <summary>
        /// This helps fix some errors with replays that do not occur due to parsing.
        /// </summary>
        /// <param name="replay"></param>
        public static void ProcessReplay(ReplayData replay)
        {
            fixTagEndOfTurns(replay);
            resolveTimedWeather(replay);
        }

        /// <summary>
        /// Names the weather on every turn that reported a countdown instead of a type.
        /// </summary>
        /// <remarks>
        /// While a weather a CO power started is running, AWBW reports it as the number of days
        /// it has left rather than as "S" or "R". That says how long but not what, so the type has
        /// to come from the power that started it — which does state it, in the name beside its
        /// own code. Walking the replay in order and carrying the weather forward resolves every
        /// countdown, and the replay states each step, so nothing here is simulated.
        /// </remarks>
        private static void resolveTimedWeather(ReplayData replay)
        {
            WeatherType? currentWeather = null;

            foreach (var turn in replay.TurnData)
            {
                if (turn.StartWeather != null)
                {
                    if (turn.StartWeather.DaysRemaining.HasValue)
                        turn.StartWeather.Type = weatherInEffect(currentWeather, turn);
                    else
                        currentWeather = turn.StartWeather.Type;
                }

                if (turn.Actions == null)
                    continue;

                foreach (var action in turn.Actions)
                {
                    switch (action)
                    {
                        case PowerAction power when power.ChangeToWeather.HasValue:
                            currentWeather = power.ChangeToWeather.Value;
                            break;

                        case EndTurnAction endTurn:
                            if (endTurn.NextWeatherDaysRemaining.HasValue)
                                endTurn.NextWeatherType = weatherInEffect(currentWeather, turn);
                            else
                                currentWeather = endTurn.NextWeatherType;
                            break;
                    }
                }
            }
        }

        private static WeatherType weatherInEffect(WeatherType? currentWeather, TurnData turn)
        {
            if (currentWeather == null)
                throw new Exception($"Day {turn.Day} reports a weather still running, but no power before it set any weather.");

            return currentWeather.Value;
        }

        /// <summary>
        /// Tag matches can sometimes add the end of turn action to the next turn, rather than the turn it was supposed to end.
        /// </summary>
        /// <param name="replay"></param>
        private static void fixTagEndOfTurns(ReplayData replay)
        {
            for (int i = 0; i < replay.TurnData.Count; i++)
            {
                var turn = replay.TurnData[i];
                if (turn?.Actions == null || turn.Actions.Count <= 0)
                    continue;

                var endTurnAction = turn.Actions[0] as EndTurnAction;
                if (endTurnAction == null || !endTurnAction.TagSwitchOccurred)
                    continue;

                turn.Actions.RemoveAt(0);
                turn.Actions.Add(endTurnAction);
            }
        }
    }
}
