using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Refresh;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class RefreshLogTests
    {
        private static readonly DateTime Now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static IList<OpenScene> Scenes(int roots)
        {
            return new List<OpenScene>
            {
                new OpenScene { Name = "Main", Path = "Assets/Main.unity", RootCount = roots },
            };
        }

        [Test]
        public void AnIdleCallStartsARunAndAsksTheCallerToDoIt()
        {
            var log = new RefreshLog();

            var outcome = log.Advance(Now, true);

            Assert.IsTrue(outcome.ShouldTrigger);
            Assert.AreEqual(RefreshLog.Refreshing, outcome.Result.State);
            Assert.IsTrue(log.WantsScenes);
        }

        [Test]
        public void ACallDuringARunChangesNothing()
        {
            var log = new RefreshLog();
            log.Advance(Now, true);

            var outcome = log.Advance(Now, true);

            Assert.IsFalse(outcome.ShouldTrigger, "A run already under way must not be started again.");
            Assert.AreEqual(RefreshLog.Refreshing, outcome.Result.State);
        }

        [Test]
        public void TheResultIsHandedOverExactlyOnce()
        {
            var log = new RefreshLog();
            log.Advance(Now, true);
            log.Completed(Now.AddMilliseconds(250), true, Scenes(5));

            var first = log.Advance(Now, true);
            Assert.AreEqual(RefreshLog.Done, first.Result.State);
            Assert.AreEqual(5, first.Result.Scenes[0].RootCount);
            Assert.AreEqual(250, first.Result.DurationMs);
            Assert.IsFalse(first.ShouldTrigger);

            // Handing it over returns to idle, so the call after it unambiguously means "do it again".
            var second = log.Advance(Now, true);
            Assert.AreEqual(RefreshLog.Refreshing, second.Result.State);
            Assert.IsTrue(second.ShouldTrigger);
        }

        [Test]
        public void RemembersWhetherScenesWereWantedAcrossTheRun()
        {
            var log = new RefreshLog();

            log.Advance(Now, false);

            Assert.IsFalse(log.WantsScenes, "The tick reads this after the request is long gone.");
        }

        [Test]
        public void SurvivesADomainReload()
        {
            var log = new RefreshLog();
            log.Advance(Now, true);
            log.Completed(Now.AddMilliseconds(10), true, Scenes(7));

            var restored = new RefreshLog();
            restored.Restore(log.Capture());

            Assert.AreEqual(RefreshLog.Done, restored.Phase);
            Assert.AreEqual(7, restored.Advance(Now, true).Result.Scenes[0].RootCount);
        }

        [Test]
        public void AnEmptyRestoreLeavesTheCycleIdle()
        {
            var log = new RefreshLog();

            log.Restore(null);

            Assert.AreEqual(RefreshLog.Idle, log.Phase);
        }
    }
}
