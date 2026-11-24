using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using AWBWApp.Game.API.Replay;
using AWBWApp.Game.Game.Logic;
using NUnit.Framework;

namespace AWBWApp.Game.Tests.Replays
{
    /// <summary>
    /// Parses every replay in the repository's `replays` directory.
    /// </summary>
    /// <remarks>
    /// That directory is working data rather than checked-in test data, so this test ignores
    /// itself when the directory is absent or empty. Dropping a replay in there that the parser
    /// cannot handle is the cheapest way to turn it into a regression case.
    /// </remarks>
    [TestFixture]
    [Category("NoCI")]
    public class TestReplayCorpusParsing
    {
        [Test]
        public void TestParsingEveryReplayInTheWorkingDirectory()
        {
            var directory = findReplayDirectory();

            if (directory == null)
                Assert.Ignore("Could not find a `replays` directory in any parent of the test assembly.");

            var replays = Directory.GetFiles(directory, "*.zip");

            if (replays.Length == 0)
                Assert.Ignore($"`{directory}` contains no replays.");

            var failures = new List<string>();

            foreach (var replay in replays)
            {
                try
                {
                    using var stream = File.OpenRead(replay);
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                    var data = new AWBWJsonReplayParser().ParseReplayZip(archive);

                    if (data.TurnData.Count <= 0)
                    {
                        failures.Add($"{Path.GetFileName(replay)}: parsed to zero turns.");
                        continue;
                    }

                    foreach (var turn in data.TurnData)
                    {
                        //A negative requirement is AWBW's marker for a power the CO does not have.
                        //It must never survive into the model, where it would be read as a
                        //reachable meter and lay the power bar out backwards.
                        foreach (var player in turn.Players)
                        {
                            if (player.Value.RequiredPowerForNormal < 0 || player.Value.RequiredPowerForSuper < 0)
                                failures.Add($"{Path.GetFileName(replay)}: day {turn.Day} player {player.Key} kept a negative power requirement.");
                        }

                        //Only a CO power starts a weather that runs on a countdown, and no power
                        //sets the weather back to clear, so a countdown resolving to Clear means
                        //it was never resolved at all.
                        if (turn.StartWeather?.DaysRemaining != null && turn.StartWeather.Type == WeatherType.Clear)
                            failures.Add($"{Path.GetFileName(replay)}: day {turn.Day} left a {turn.StartWeather.DaysRemaining} day weather unresolved.");
                    }
                }
                catch (Exception e)
                {
                    failures.Add($"{Path.GetFileName(replay)}: {e.GetType().Name}: {e.Message}");
                }
            }

            Assert.That(failures, Is.Empty, $"{failures.Count} of {replays.Length} replays failed to parse:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
        }

        /// <summary>
        /// Walks up from the test assembly looking for the repository root, identified by the
        /// solution filter that <c>CLAUDE.md</c> tells you to build.
        /// </summary>
        private static string findReplayDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AWBWApp.Desktop.slnf")))
                {
                    var replays = Path.Combine(directory.FullName, "replays");

                    return Directory.Exists(replays) ? replays : null;
                }

                directory = directory.Parent;
            }

            return null;
        }
    }
}
