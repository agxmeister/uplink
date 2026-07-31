using System;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class FaultBarrierTests
    {
        private sealed class Throwing : IRequestHandler
        {
            private readonly Exception exception;

            public Throwing(Exception exception)
            {
                this.exception = exception;
            }

            public Response Handle(Request request)
            {
                throw exception;
            }
        }

        [Test]
        public void ReportsABusyEditorAsGatewayTimeout()
        {
            var barrier = new FaultBarrier(new Throwing(new TimeoutException("busy")), new SilentLog());

            Assert.AreEqual(504, barrier.Handle(Requests.Of("GET", "/status")).Status);
        }

        [Test]
        public void ReportsAnyOtherFailureAsServerError()
        {
            var barrier = new FaultBarrier(new Throwing(new InvalidOperationException("boom")), new SilentLog());

            Assert.AreEqual(500, barrier.Handle(Requests.Of("GET", "/status")).Status);
        }

        [Test]
        public void PassesASuccessfulResponseThrough()
        {
            var router = new Router(new[]
            {
                new StubEndpoint("GET", "/status", request => Response.Json(200, null)),
            });
            var barrier = new FaultBarrier(router, new SilentLog());

            Assert.AreEqual(200, barrier.Handle(Requests.Of("GET", "/status")).Status);
        }
    }
}
