using System.Text;
using Agxmeister.Uplink.Http;
using Agxmeister.Uplink.PlayMode;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class PlayModeTests
    {
        /// <summary>
        /// An Editor whose play mode changes only when told, and — like the real one — not until a moment
        /// after being asked to enter or leave.
        /// </summary>
        private sealed class FakeEditor : IEditorPlayMode
        {
            public bool IsPlaying { get; set; }

            public bool IsPaused { get; set; }

            public bool? Requested { get; private set; }

            public int Steps { get; private set; }

            public void Enter()
            {
                Requested = true;
            }

            public void Exit()
            {
                Requested = false;
            }

            public void Pause(bool paused)
            {
                IsPaused = paused;
            }

            public void Step()
            {
                Steps++;
            }

            /// <summary>The tick on which a deferred request actually lands.</summary>
            public void Settle()
            {
                if (Requested.HasValue)
                {
                    IsPlaying = Requested.Value;
                    IsPaused = false;
                    Requested = null;
                }
            }
        }

        [Test]
        public void AsksToEnterPlayModeAndSaysItIsOnTheWay()
        {
            var editor = new FakeEditor();

            var status = new PlayModeControl(editor).Poll(PlayModeTarget.Play);

            Assert.AreEqual(PlayModeCycle.Changing, status.State);
            Assert.AreEqual(true, editor.Requested);
        }

        [Test]
        public void ReportsDoneOnceTheEditorHasArrived()
        {
            var editor = new FakeEditor();
            var control = new PlayModeControl(editor);
            control.Poll(PlayModeTarget.Play);
            editor.Settle();

            var status = control.Poll(PlayModeTarget.Play);

            Assert.AreEqual(PlayModeCycle.Done, status.State);
            Assert.IsTrue(status.IsPlaying);
        }

        [Test]
        public void AskingForWhatIsAlreadyTrueChangesNothing()
        {
            var editor = new FakeEditor { IsPlaying = true };

            var status = new PlayModeControl(editor).Poll(PlayModeTarget.Play);

            Assert.AreEqual(PlayModeCycle.Done, status.State);
            Assert.IsNull(editor.Requested, "nothing should have been asked of the Editor");
        }

        [Test]
        public void PausingTakesEffectAtOnce()
        {
            var editor = new FakeEditor { IsPlaying = true };

            var status = new PlayModeControl(editor).Poll(PlayModeTarget.Pause);

            Assert.AreEqual(PlayModeCycle.Done, status.State);
            Assert.IsTrue(status.IsPaused);
        }

        [Test]
        public void PlayResumesAPausedGameRatherThanRestartingIt()
        {
            var editor = new FakeEditor { IsPlaying = true, IsPaused = true };

            var status = new PlayModeControl(editor).Poll(PlayModeTarget.Play);

            Assert.AreEqual(PlayModeCycle.Done, status.State);
            Assert.IsFalse(status.IsPaused);
            Assert.IsNull(editor.Requested, "play mode was already running");
        }

        [Test]
        public void StoppingIsAlsoOnItsWay()
        {
            var editor = new FakeEditor { IsPlaying = true };

            var status = new PlayModeControl(editor).Poll(PlayModeTarget.Stop);

            Assert.AreEqual(PlayModeCycle.Changing, status.State);
            Assert.AreEqual(false, editor.Requested);
        }

        [Test]
        public void SteppingIsOverAsSoonAsItIsTaken()
        {
            var editor = new FakeEditor { IsPlaying = true, IsPaused = true };

            var status = new PlayModeControl(editor).Poll(PlayModeTarget.Step);

            Assert.AreEqual(PlayModeCycle.Done, status.State);
            Assert.AreEqual(1, editor.Steps);
        }

        [Test]
        public void RefusesToPauseOrStepAGameThatIsNotRunning()
        {
            var control = new PlayModeControl(new FakeEditor());

            Assert.Throws<BadRequestException>(() => control.Poll(PlayModeTarget.Pause));
            Assert.Throws<BadRequestException>(() => control.Poll(PlayModeTarget.Step));
        }

        [Test]
        public void RefusesATargetItDoesNotHave()
        {
            var control = new PlayModeControl(new FakeEditor());

            Assert.Throws<BadRequestException>(() => control.Poll("rewind"));
        }

        [Test]
        public void PlaysWhenTheBodyDoesNotSayOtherwise()
        {
            var editor = new FakeEditor();

            new PlayModeEndpoint(new PlayModeControl(editor)).Handle(Requests.Of("POST", "/play"));

            Assert.AreEqual(true, editor.Requested);
        }

        [Test]
        public void AnswersAcceptedWhileTheEditorIsStillChanging()
        {
            var endpoint = new PlayModeEndpoint(new PlayModeControl(new FakeEditor()));

            var response = endpoint.Handle(Requests.Of("POST", "/play", "{\"target\":\"play\"}"));

            Assert.AreEqual(202, response.Status);
        }

        [Test]
        public void DescribesEveryFieldItActuallyReturns()
        {
            var endpoint = new PlayModeEndpoint(new PlayModeControl(new FakeEditor { IsPlaying = true }));

            var body = JObject.Parse(Encoding.UTF8.GetString(
                endpoint.Handle(Requests.Of("POST", "/play", "{\"target\":\"pause\"}")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }
        }

        [Test]
        public void DescribesEveryTargetItAccepts()
        {
            var described = JObject.FromObject(new PlayModeEndpoint(new PlayModeControl(new FakeEditor())).Describe());
            var targets = described["requestBody"]["content"]["application/json"]
                ["schema"]["properties"]["target"]["enum"].ToObject<string[]>();

            CollectionAssert.AreEquivalent(new[] { "play", "stop", "pause", "step" }, targets);
        }
    }
}
