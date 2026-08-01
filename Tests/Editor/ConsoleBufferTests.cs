using System;
using Agxmeister.Uplink.Console;
using Agxmeister.Uplink.Persistence;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class ConsoleBufferTests
    {
        private static readonly DateTime When = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        private static ConsoleBuffer Filled(params string[] levels)
        {
            var buffer = new ConsoleBuffer();
            for (var i = 0; i < levels.Length; i++)
            {
                buffer.Record(levels[i], string.Format("message {0}", i), "at Thing.Do()", When);
            }
            return buffer;
        }

        [Test]
        public void NumbersMessagesSoAClientCanAskForWhatIsNew()
        {
            var buffer = Filled(ConsoleLevel.Log, ConsoleLevel.Log);

            var first = buffer.Read(new ConsoleQuery());
            buffer.Record(ConsoleLevel.Error, "boom", null, When);
            var second = buffer.Read(new ConsoleQuery { Since = first.NextSince });

            Assert.AreEqual(2, first.Entries.Count);
            Assert.AreEqual(1, second.Entries.Count);
            Assert.AreEqual("boom", second.Entries[0].Message);
        }

        [Test]
        public void ReturnsNothingTwiceWhenTheCursorIsFollowed()
        {
            var buffer = Filled(ConsoleLevel.Log);

            var page = buffer.Read(new ConsoleQuery());

            Assert.AreEqual(0, buffer.Read(new ConsoleQuery { Since = page.NextSince }).Entries.Count);
        }

        [Test]
        public void TreatsLevelAsAMinimumSeverity()
        {
            var buffer = Filled(ConsoleLevel.Log, ConsoleLevel.Warning, ConsoleLevel.Error);

            Assert.AreEqual(3, buffer.Read(new ConsoleQuery { Level = ConsoleLevel.Log }).Entries.Count);
            Assert.AreEqual(2, buffer.Read(new ConsoleQuery { Level = ConsoleLevel.Warning }).Entries.Count);
            Assert.AreEqual(1, buffer.Read(new ConsoleQuery { Level = ConsoleLevel.Error }).Entries.Count);
        }

        [Test]
        public void CountsEveryLevelEvenWhenAskedForOne()
        {
            var buffer = Filled(ConsoleLevel.Log, ConsoleLevel.Warning, ConsoleLevel.Error, ConsoleLevel.Error);

            var page = buffer.Read(new ConsoleQuery { Level = ConsoleLevel.Error });

            Assert.AreEqual(2, page.Entries.Count);
            Assert.AreEqual(2, page.Counts.Errors);
            Assert.AreEqual(1, page.Counts.Warnings);
            Assert.AreEqual(1, page.Counts.Logs);
        }

        [Test]
        public void PagesForwardWithoutSkippingWhatItCouldNotFit()
        {
            var buffer = Filled(ConsoleLevel.Log, ConsoleLevel.Log, ConsoleLevel.Log);

            var first = buffer.Read(new ConsoleQuery { Limit = 2 });
            var second = buffer.Read(new ConsoleQuery { Limit = 2, Since = first.NextSince });

            Assert.IsTrue(first.Truncated);
            Assert.AreEqual("message 0", first.Entries[0].Message);
            Assert.AreEqual("message 2", second.Entries[0].Message);
            Assert.IsFalse(second.Truncated);
        }

        [Test]
        public void FiltersByText()
        {
            var buffer = new ConsoleBuffer();
            buffer.Record(ConsoleLevel.Error, "NullReferenceException in Player", null, When);
            buffer.Record(ConsoleLevel.Error, "Missing prefab", null, When);

            var page = buffer.Read(new ConsoleQuery { Search = "nullreference" });

            Assert.AreEqual(1, page.Entries.Count);
        }

        [Test]
        public void KeepsStackTracesForErrorsOnly()
        {
            var buffer = Filled(ConsoleLevel.Log, ConsoleLevel.Error);

            var page = buffer.Read(new ConsoleQuery());

            Assert.IsNull(page.Entries[0].StackTrace);
            Assert.AreEqual("at Thing.Do()", page.Entries[1].StackTrace);
        }

        [Test]
        public void OmitsStackTracesWhenTheyAreNotWanted()
        {
            var buffer = Filled(ConsoleLevel.Error);

            Assert.IsNull(buffer.Read(new ConsoleQuery { StackTraces = false }).Entries[0].StackTrace);
        }

        [Test]
        public void HidingAStackTraceFromOneReadDoesNotLoseIt()
        {
            var buffer = Filled(ConsoleLevel.Error);

            buffer.Read(new ConsoleQuery { StackTraces = false });

            Assert.AreEqual("at Thing.Do()", buffer.Read(new ConsoleQuery()).Entries[0].StackTrace);
        }

        [Test]
        public void DropsTheOldestMessagesWhenItIsFull()
        {
            var buffer = new ConsoleBuffer(2);
            buffer.Record(ConsoleLevel.Log, "one", null, When);
            buffer.Record(ConsoleLevel.Log, "two", null, When);
            buffer.Record(ConsoleLevel.Log, "three", null, When);

            var page = buffer.Read(new ConsoleQuery());

            Assert.AreEqual(2, page.Entries.Count);
            Assert.AreEqual("two", page.Entries[0].Message);
            Assert.AreEqual(2, page.Entries[1].Seq, "positions must keep counting past what was dropped");
        }

        [Test]
        public void SurvivesADomainReloadWithItsNumberingIntact()
        {
            var store = new InMemoryStore();
            var before = Filled(ConsoleLevel.Log, ConsoleLevel.Error);
            before.HistoryAvailable = true;
            Stored.Write(store, "console", before.Capture());

            var after = new ConsoleBuffer();
            after.Restore(Stored.Read<ConsoleState>(store, "console"));
            after.Record(ConsoleLevel.Log, "after the reload", null, When);

            var page = after.Read(new ConsoleQuery());

            Assert.AreEqual(3, page.Entries.Count);
            Assert.AreEqual(2, page.Entries[2].Seq);
            Assert.IsTrue(page.HistoryAvailable);
        }

        [Test]
        public void SeedsHistoryAheadOfWhatItHearsItself()
        {
            var buffer = new ConsoleBuffer();
            buffer.Seed(new[]
            {
                new ConsoleEntry { Level = ConsoleLevel.Error, Message = "before Uplink loaded" },
            });
            buffer.Record(ConsoleLevel.Log, "after", null, When);

            var page = buffer.Read(new ConsoleQuery());

            Assert.AreEqual("before Uplink loaded", page.Entries[0].Message);
            Assert.AreEqual(0, page.Entries[0].Seq);
            Assert.AreEqual(1, page.Entries[1].Seq);
        }
    }
}
