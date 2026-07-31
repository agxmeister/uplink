using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class RouterTests
    {
        private static Router RouterWith(params IEndpoint[] endpoints)
        {
            return new Router(new List<IEndpoint>(endpoints));
        }

        [Test]
        public void DispatchesToTheEndpointMatchingMethodAndPath()
        {
            var router = RouterWith(
                new StubEndpoint("GET", "/status", request => Response.Text(200, "text/plain", "get")),
                new StubEndpoint("POST", "/status", request => Response.Text(201, "text/plain", "post")));

            Assert.AreEqual(201, router.Handle(Requests.Of("POST", "/status")).Status);
        }

        [Test]
        public void IgnoresATrailingSlash()
        {
            var router = RouterWith(new StubEndpoint("GET", "/status", request => Response.Json(200, null)));

            Assert.AreEqual(200, router.Handle(Requests.Of("GET", "/status/")).Status);
        }

        [Test]
        public void AnswersUnknownPathsWithNotFound()
        {
            var router = RouterWith(new StubEndpoint("GET", "/status", request => Response.Json(200, null)));

            Assert.AreEqual(404, router.Handle(Requests.Of("GET", "/nope")).Status);
        }

        [Test]
        public void AnswersAKnownPathWithTheWrongMethodWithMethodNotAllowed()
        {
            var router = RouterWith(new StubEndpoint("GET", "/status", request => Response.Json(200, null)));

            var response = router.Handle(Requests.Of("POST", "/status"));

            Assert.AreEqual(405, response.Status);
            Assert.AreEqual("GET", response.Headers["Allow"]);
        }
    }
}
