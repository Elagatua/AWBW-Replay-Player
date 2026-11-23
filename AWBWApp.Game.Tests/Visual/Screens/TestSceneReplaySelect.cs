using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AWBWApp.Game.IO;
using AWBWApp.Game.UI.Select;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;

namespace AWBWApp.Game.Tests.Visual.Screens
{
    [TestFixture]
    public partial class TestSceneReplaySelect : AWBWAppTestScene
    {
        protected ReplaySelectScreen Screen;

        protected ScreenStack ScreenStack;

        private TestReplayManager replayManager;
        private DependencyContainer dependencies;
        private ResourceStore<byte[]> fileStore;

        [BackgroundDependencyLoader]
        private void load(ResourceStore<byte[]> fileStore)
        {
            this.fileStore = fileStore;

            replayManager = new TestReplayManager(LocalStorage);
            dependencies.CacheAs(typeof(ReplayManager), replayManager);
        }

        [SetUpSteps]
        public void SetupSteps()
        {
            AddStep("Construct Replay Screen", () =>
            {
                replayManager.ClearAllReplays();

                if (ScreenStack != null)
                    Remove(ScreenStack, true);

                Add(ScreenStack = new ScreenStack
                {
                    RelativeSizeAxes = Axes.Both
                });

                ScreenStack.Push(Screen = new ReplaySelectScreen
                {
                    RelativeSizeAxes = Axes.Both
                });
            });
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            return dependencies;
        }

        [Test]
        public void TestReplayScreen()
        {
            AddStep("Add Replay", () => Task.Run(() => replayManager.ParseThenStoreReplayStream(478996, fileStore.GetStream("Json/Replays/478996.zip"))));

            //The replay is written on a background thread, so the file can already exist while the
            //writer still holds it open. Reading it then throws rather than returning null, so treat
            //that as "not ready yet" and keep waiting.
            AddUntilStep("Wait Until Map is loaded", () =>
            {
                try
                {
                    return replayManager.GetReplayDataSync(478996) != null;
                }
                catch (IOException)
                {
                    return false;
                }
            });
        }

        private class TestReplayManager : ReplayManager
        {
            public TestReplayManager(Storage storage)
                : base(storage)
            {
            }

            public void ClearAllReplays()
            {
                var replays = GetAllKnownReplays().ToList();
                foreach (var replay in replays)
                    DeleteReplay(replay);
            }
        }
    }
}
