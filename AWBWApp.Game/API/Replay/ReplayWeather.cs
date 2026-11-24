using AWBWApp.Game.Game.Logic;

namespace AWBWApp.Game.API.Replay
{
    public class ReplayWeather
    {
        public WeatherType Type;
        public int? TurnStartID;

        /// <summary>
        /// Set when the turn snapshot reported the weather as the number of days it has left to
        /// run instead of naming it, which AWBW does while a CO power's weather is in effect.
        /// <see cref="Type"/> is then filled in by <see cref="ReplayPostProcessor"/> from the
        /// power that started it, so it is resolved by the time a replay finishes parsing.
        /// </summary>
        public int? DaysRemaining;
    }
}
