using System;
using System.Threading;
using Agxmeister.Uplink.Threading;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class MainThreadDispatcherTests
    {
        [Test]
        public void ReturnsTheResultOfWorkRunByThePump()
        {
            var dispatcher = new MainThreadDispatcher();
            var result = 0;

            var caller = new Thread(() => result = dispatcher.Run(() => 42, TimeSpan.FromSeconds(5)));
            caller.Start();
            PumpUntil(dispatcher, caller);

            Assert.AreEqual(42, result);
        }

        [Test]
        public void RunsWorkOnThePumpingThread()
        {
            var dispatcher = new MainThreadDispatcher();
            var pumpingThread = Thread.CurrentThread.ManagedThreadId;
            var ranOn = 0;

            var caller = new Thread(
                () => ranOn = dispatcher.Run(() => Thread.CurrentThread.ManagedThreadId, TimeSpan.FromSeconds(5)));
            caller.Start();
            PumpUntil(dispatcher, caller);

            Assert.AreEqual(pumpingThread, ranOn);
        }

        [Test]
        public void PropagatesAFailureToTheCaller()
        {
            var dispatcher = new MainThreadDispatcher();
            Exception caught = null;

            var caller = new Thread(() =>
            {
                try
                {
                    dispatcher.Run<object>(() => { throw new InvalidOperationException("boom"); },
                        TimeSpan.FromSeconds(5));
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
            });
            caller.Start();
            PumpUntil(dispatcher, caller);

            Assert.IsInstanceOf<InvalidOperationException>(caught);
            Assert.AreEqual("boom", caught.Message);
        }

        [Test]
        public void TimesOutWhenNobodyPumps()
        {
            var dispatcher = new MainThreadDispatcher();

            Assert.Throws<TimeoutException>(() => dispatcher.Run(() => 1, TimeSpan.FromMilliseconds(50)));
        }

        [Test]
        public void DoesNotRunWorkWhoseCallerHasGivenUp()
        {
            var dispatcher = new MainThreadDispatcher();
            var ran = false;

            Assert.Throws<TimeoutException>(
                () => dispatcher.Run(() => ran = true, TimeSpan.FromMilliseconds(50)));
            dispatcher.Pump();

            Assert.IsFalse(ran, "Abandoned work must not run against the Editor later.");
        }

        private static void PumpUntil(MainThreadDispatcher dispatcher, Thread caller)
        {
            while (caller.IsAlive)
            {
                dispatcher.Pump();
                Thread.Sleep(1);
            }
            caller.Join();
        }
    }
}
