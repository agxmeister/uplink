using System;
using System.Text;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;
using Newtonsoft.Json.Linq;
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
        public void EveryFailureCarriesItsStatusInTheBody()
        {
            // An adapter between here and the model may swallow the status line, so the body must be enough
            // to tell a transient worth retrying from a real fault.
            var barrier = new FaultBarrier(new Throwing(new TimeoutException("busy")), new SilentLog());
            var body = JObject.Parse(Encoding.UTF8.GetString(barrier.Handle(Requests.Of("GET", "/status")).Body));

            Assert.AreEqual("busy", body["error"].Value<string>());
            Assert.AreEqual(504, body["status"].Value<int>());
            Assert.IsTrue(body["retry"].Value<bool>());
        }

        [Test]
        public void ARealFaultNamesItsExceptionAndDoesNotInviteARetry()
        {
            var barrier = new FaultBarrier(new Throwing(new InvalidOperationException("boom")), new SilentLog());
            var body = JObject.Parse(Encoding.UTF8.GetString(barrier.Handle(Requests.Of("GET", "/status")).Body));

            Assert.AreEqual("InvalidOperationException: boom", body["error"].Value<string>());
            Assert.AreEqual(500, body["status"].Value<int>());
            Assert.IsNull(body["retry"]);
        }

        [Test]
        public void ReportsAMalformedRequestAsBadRequest()
        {
            var barrier = new FaultBarrier(new Throwing(new BadRequestException("'limit' must be a whole number.")), new SilentLog());

            Assert.AreEqual(400, barrier.Handle(Requests.Of("GET", "/console")).Status);
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
