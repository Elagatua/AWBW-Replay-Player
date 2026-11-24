using System;
using System.Globalization;

namespace AWBWApp.Game.Game.Logic
{
    public enum WeatherType
    {
        Clear,
        Rain,
        Snow
    }

    public static class WeatherHelper
    {
        public static WeatherType ParseWeatherCode(string code)
        {
            switch (code)
            {
                case "R":
                case "r":
                    return WeatherType.Rain;

                case "S":
                case "s":
                    return WeatherType.Snow;

                case "C":
                case "c":
                    return WeatherType.Clear;

                default:
                    throw new Exception("Unknown weather code: " + code);
            }
        }

        /// <summary>
        /// Recognises the form AWBW uses while a weather a CO power started is still running:
        /// the number of days it has left, rather than a letter naming it.
        /// </summary>
        /// <remarks>
        /// The number says how long, not what, so it cannot be turned into a <see cref="WeatherType"/>
        /// on its own. <c>ReplayPostProcessor</c> resolves it against the power that started it.
        /// A game whose weather nobody has touched reports "C" throughout, so a number always
        /// implies some earlier power.
        /// </remarks>
        public static bool TryParseTimedWeatherCode(string code, out int daysRemaining)
            => int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out daysRemaining);

        /// <summary>
        /// Reads the weather name that a power action carries beside its code. Unlike the code,
        /// this always names the weather.
        /// </summary>
        public static WeatherType ParseWeatherName(string name)
        {
            switch (name?.ToLowerInvariant())
            {
                case "rain":
                    return WeatherType.Rain;

                case "snow":
                    return WeatherType.Snow;

                case "clear":
                    return WeatherType.Clear;

                default:
                    throw new Exception("Unknown weather name: " + name);
            }
        }
    }
}
