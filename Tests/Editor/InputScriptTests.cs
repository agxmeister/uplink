using System.Collections.Generic;
using Agxmeister.Uplink.Controls;
using Agxmeister.Uplink.Persistence;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    /// <summary>
    /// The input cycle and its clock, driven entirely by hand. Nothing here needs an Editor, a player loop or
    /// `EditorApplication.update` — which is the point: the scheduling is the only interesting logic, so it
    /// lives where it can be stepped through a moment at a time.
    /// </summary>
    [TestFixture]
    public sealed class InputScriptTests
    {
        private const string Space = "<Keyboard>/space";
        private const string Left = "<Keyboard>/leftArrow";

        private static InputStep Key(string path, double? hold)
        {
            return new InputStep { Key = path, Hold = hold };
        }

        /// <summary>The arkanoid script from the requirement: tap, hold left, pause, tap.</summary>
        private static ScriptPlan Launch()
        {
            var steps = new List<InputStep>
            {
                Key(Space, 0.05),
                Key(Left, 1.2),
                new InputStep { Wait = 0.5 },
                Key(Space, 0.05),
            };

            return InputScript.Compile(steps, new List<string> { Space, Left, null, Space });
        }

        [Test]
        public void CompilesStepsIntoASequentialTimeline()
        {
            var plan = Launch();

            // press space, release space, press left, release left, press space, release space
            Assert.AreEqual(6, plan.Actions.Count);
            Assert.AreEqual(0.0, plan.Actions[0].At, 1e-9);
            Assert.IsTrue(plan.Actions[0].Pressed);
            Assert.AreEqual(0.05, plan.Actions[1].At, 1e-9);
            Assert.IsFalse(plan.Actions[1].Pressed);

            // The second step starts where the first ended: steps run one after another, not together.
            Assert.AreEqual(0.05, plan.Actions[2].At, 1e-9);
            Assert.AreEqual(Left, plan.Actions[2].Path);
            Assert.AreEqual(1.25, plan.Actions[3].At, 1e-9);

            // ...and the wait pushes the last tap out by half a second.
            Assert.AreEqual(1.75, plan.Actions[4].At, 1e-9);
            Assert.AreEqual(1.8, plan.DurationSeconds, 1e-9);
            Assert.AreEqual(4, plan.Steps);
        }

        [Test]
        public void HandsOutOnlyWhatHasFallenDue()
        {
            var script = new InputScript();
            script.Advance(100.0, Launch());

            var atStart = script.Tick(100.0);
            Assert.AreEqual(1, atStart.Count, "only the first press is due at t=0");
            Assert.AreEqual(Space, atStart[0].Path);
            Assert.IsTrue(atStart[0].Pressed);

            var soonAfter = script.Tick(100.6);
            Assert.AreEqual(2, soonAfter.Count, "space released, left pressed — and neither of them twice");
            Assert.IsFalse(soonAfter[0].Pressed);
            Assert.AreEqual(Left, soonAfter[1].Path);
            Assert.IsTrue(soonAfter[1].Pressed);

            var midHold = script.Tick(101.0);
            Assert.AreEqual(0, midHold.Count, "left is still held: its release is not due until 1.25");

            var atEnd = script.Tick(102.0);
            Assert.AreEqual(3, atEnd.Count, "left released, then the last tap");
            Assert.IsTrue(script.IsFinished(102.0));
        }

        [Test]
        public void CountsAWaitAsDeliveredEvenThoughItHasNoActions()
        {
            var script = new InputScript();
            script.Advance(0.0, Launch());

            // 1.4s in: the tap and the hold are done, the wait (ending at 1.75) is not.
            Assert.AreEqual(2, script.Observe(1.4).StepsDelivered);
            Assert.AreEqual(3, script.Observe(1.76).StepsDelivered);
            Assert.AreEqual(4, script.Observe(2.0).StepsDelivered);
        }

        [Test]
        public void PlacesAPointerWithoutSpendingAnyTime()
        {
            var plan = InputScript.Compile(
                new List<InputStep>
                {
                    new InputStep { Move = new[] { 960f, 540f } },
                    new InputStep { Click = "<Mouse>/leftButton", Hold = 0.1 },
                },
                new List<string> { null, "<Mouse>/leftButton" });

            Assert.AreEqual(ControlActionKind.Pointer, plan.Actions[0].Kind);
            Assert.AreEqual(960f, plan.Actions[0].X);
            Assert.AreEqual(540f, plan.Actions[0].Y);

            // The click lands at the same instant, so it clicks where the move just put the pointer.
            Assert.AreEqual(0.0, plan.Actions[1].At, 1e-9);
            Assert.AreEqual(0.1, plan.DurationSeconds, 1e-9);
        }

        [Test]
        public void UsesADefaultHoldLongEnoughToBeSeen()
        {
            var plan = InputScript.Compile(
                new List<InputStep> { new InputStep { Key = Space } }, new List<string> { Space });

            Assert.AreEqual(InputScript.DefaultHold, plan.Actions[1].At, 1e-9);
        }

        [Test]
        public void RunsTheCycleAndHandsTheOutcomeOverExactlyOnce()
        {
            var script = new InputScript();

            var started = script.Advance(0.0, Launch());
            Assert.AreEqual(InputScript.Running, started.Result.State);
            Assert.IsTrue(started.ShouldStart);

            var duringA = script.Advance(1.0, null);
            Assert.AreEqual(InputScript.Running, duringA.Result.State);
            Assert.IsFalse(duringA.ShouldStart, "polling must not start anything");

            script.Tick(2.0);
            script.Completed(2.0);

            var handedOver = script.Advance(2.1, null);
            Assert.AreEqual(InputScript.Done, handedOver.Result.State);

            // Delivered once: the call after it finds nothing waiting, which is what makes "again" unambiguous.
            var afterwards = script.Advance(2.2, null);
            Assert.IsTrue(afterwards.NothingToPlay);
        }

        [Test]
        public void RefusesToReplaceAScriptThatIsAlreadyPlaying()
        {
            var script = new InputScript();
            script.Advance(0.0, Launch());

            var second = script.Advance(0.5, Launch());

            Assert.AreEqual(InputScript.Running, second.Result.State);
            Assert.IsFalse(second.ShouldStart);
            StringAssert.Contains("not queued", second.Result.Note);
        }

        [Test]
        public void SaysNothingWasQueuedOnlyWhenStepsWereActuallySent()
        {
            var script = new InputScript();
            script.Advance(0.0, Launch());

            Assert.IsNull(script.Advance(0.5, null).Result.Note, "an ordinary poll is not a warning");
        }

        [Test]
        public void ReportsTheRestingStateToAnObserverButNeverToAnActor()
        {
            var script = new InputScript();

            var observed = script.Observe(0.0);
            Assert.AreEqual(InputScript.Idle, observed.State);
            Assert.IsTrue(observed.Stale.Value);

            script.Advance(0.0, Launch());
            Assert.AreEqual(InputScript.Running, script.Observe(0.1).State);
            Assert.IsTrue(script.Observe(0.1).Stale.Value);
        }

        [Test]
        public void ObservingChangesNothing()
        {
            var script = new InputScript();
            script.Advance(0.0, Launch());
            script.Completed(2.0);

            script.Observe(2.1);
            script.Observe(2.2);

            // The result is still there to be taken: looking is not taking delivery.
            Assert.AreEqual(InputScript.Done, script.Advance(2.3, null).Result.State);
        }

        [Test]
        public void ClosesARunThatPlayModeEndedUnderneath()
        {
            var script = new InputScript();
            script.Advance(0.0, Launch());
            script.Tick(0.6);

            script.PlayModeEnded(0.6);

            var result = script.Advance(0.7, null).Result;
            Assert.AreEqual(InputScript.Done, result.State);
            Assert.IsTrue(result.PlayModeEnded, "a script that ran into a stopped Editor is not a success");
            Assert.Less(result.StepsDelivered, result.Steps);
        }

        [Test]
        public void CarriesOnWhereItLeftOffAcrossADomainReload()
        {
            var store = new InMemoryStore();
            var before = new InputScript();
            before.Advance(0.0, Launch());
            before.Tick(0.6);

            Stored.Write(store, "input", before.Capture());

            // A reload wipes every static, so the far side is a fresh instance with only the store to go on.
            var after = new InputScript();
            after.Restore(Stored.Read<ScriptState>(store, "input"));

            Assert.AreEqual(InputScript.Running, after.Phase);
            Assert.AreEqual(2, after.Observe(1.4).StepsDelivered);

            // The cursor came across too: the three actions already handed out are not handed out again.
            var due = after.Tick(2.0);
            Assert.AreEqual(3, due.Count);
            Assert.IsTrue(after.IsFinished(2.0));
        }

        [Test]
        public void TreatsAnAbsentOrBrokenStoreAsAFreshCycle()
        {
            var script = new InputScript();

            script.Restore(null);

            Assert.AreEqual(InputScript.Idle, script.Phase);
            Assert.IsTrue(script.Advance(0.0, null).NothingToPlay);
        }
    }
}
