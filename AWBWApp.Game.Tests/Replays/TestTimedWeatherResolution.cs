using System.Collections.Generic;
using AWBWApp.Game.API.Replay;
using AWBWApp.Game.API.Replay.Actions;
using AWBWApp.Game.Game.Logic;
using NUnit.Framework;

namespace AWBWApp.Game.Tests.Replays
{
    /// <summary>
    /// Covers the weather a CO power starts, which AWBW reports as a countdown of days left
    /// rather than as a named weather, leaving the type to be recovered from the power itself.
    /// </summary>
    [TestFixture]
    public class TestTimedWeatherResolution
    {
        /// <summary>
        /// The shape taken from a real DS Olaf game: Winter Fury lands, and the three turns it
        /// covers report "2", "1", "1" in place of a weather, before clearing.
        /// </summary>
        [Test]
        public void TestACountdownTakesItsTypeFromThePowerThatStartedIt()
        {
            var replay = new ReplayData
            {
                TurnData =
                {
                    turn(1, WeatherType.Clear, new PowerAction { ChangeToWeather = WeatherType.Snow }, endTurn(daysRemaining: 2)),
                    turn(1, countdown: 2, endTurn(daysRemaining: 1)),
                    turn(2, countdown: 1, endTurn(daysRemaining: 1)),
                    turn(2, countdown: 1, endTurn(WeatherType.Clear)),
                    turn(3, WeatherType.Clear)
                }
            };

            ReplayPostProcessor.ProcessReplay(replay);

            Assert.That(replay.TurnData[0].StartWeather.Type, Is.EqualTo(WeatherType.Clear));
            Assert.That(replay.TurnData[1].StartWeather.Type, Is.EqualTo(WeatherType.Snow));
            Assert.That(replay.TurnData[2].StartWeather.Type, Is.EqualTo(WeatherType.Snow));
            Assert.That(replay.TurnData[3].StartWeather.Type, Is.EqualTo(WeatherType.Snow));
            Assert.That(replay.TurnData[4].StartWeather.Type, Is.EqualTo(WeatherType.Clear));

            //The end of each covered turn must agree with the start of the next one.
            Assert.That(((EndTurnAction)replay.TurnData[0].Actions[1]).NextWeatherType, Is.EqualTo(WeatherType.Snow));
            Assert.That(((EndTurnAction)replay.TurnData[1].Actions[0]).NextWeatherType, Is.EqualTo(WeatherType.Snow));
            Assert.That(((EndTurnAction)replay.TurnData[2].Actions[0]).NextWeatherType, Is.EqualTo(WeatherType.Snow));
            Assert.That(((EndTurnAction)replay.TurnData[3].Actions[0]).NextWeatherType, Is.EqualTo(WeatherType.Clear));
        }

        /// <summary>
        /// Rain is only ever reported by name, so nothing may quietly assume a countdown is snow.
        /// </summary>
        [Test]
        public void TestACountdownFollowingRainResolvesToRain()
        {
            var replay = new ReplayData
            {
                TurnData =
                {
                    turn(1, WeatherType.Clear, new PowerAction { ChangeToWeather = WeatherType.Rain }),
                    turn(1, countdown: 1)
                }
            };

            ReplayPostProcessor.ProcessReplay(replay);

            Assert.That(replay.TurnData[1].StartWeather.Type, Is.EqualTo(WeatherType.Rain));
        }

        /// <summary>
        /// A countdown with nothing before it is data this program does not understand, and the
        /// parser is meant to say so rather than guess.
        /// </summary>
        [Test]
        public void TestACountdownWithNoPowerBeforeItThrows()
        {
            var replay = new ReplayData { TurnData = { turn(1, countdown: 2) } };

            Assert.That(() => ReplayPostProcessor.ProcessReplay(replay), Throws.Exception.With.Message.Contains("no power before it"));
        }

        private static TurnData turn(int day, WeatherType weather, params IReplayAction[] actions) =>
            new TurnData
            {
                Day = day,
                StartWeather = new ReplayWeather { Type = weather },
                Actions = new List<IReplayAction>(actions)
            };

        private static TurnData turn(int day, int countdown, params IReplayAction[] actions) =>
            new TurnData
            {
                Day = day,
                StartWeather = new ReplayWeather { DaysRemaining = countdown },
                Actions = new List<IReplayAction>(actions)
            };

        private static EndTurnAction endTurn(WeatherType weather) => new EndTurnAction { NextWeatherType = weather };

        private static EndTurnAction endTurn(int daysRemaining) => new EndTurnAction { NextWeatherDaysRemaining = daysRemaining };
    }
}
