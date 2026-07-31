using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class OpenApiEndpointTests
    {
        private static JObject DocumentOf(List<IEndpoint> endpoints)
        {
            var endpoint = new OpenApiEndpoint(endpoints, "Uplink", "Test.", "0.1.0");
            endpoints.Add(endpoint);
            return JObject.Parse(Encoding.UTF8.GetString(endpoint.Handle(Requests.Of("GET", "/openapi.json")).Body));
        }

        [Test]
        public void DescribesEveryRegisteredEndpointWithoutBeingTold()
        {
            var document = DocumentOf(new List<IEndpoint>
            {
                new StubEndpoint("GET", "/status", request => Response.Json(200, null)),
                new StubEndpoint("POST", "/refresh", request => Response.Json(200, null)),
            });

            Assert.IsNotNull(document["paths"]["/status"]["get"]);
            Assert.IsNotNull(document["paths"]["/refresh"]["post"]);
            Assert.IsNotNull(document["paths"]["/openapi.json"]["get"]);
        }

        [Test]
        public void GroupsSeveralMethodsUnderOnePath()
        {
            var document = DocumentOf(new List<IEndpoint>
            {
                new StubEndpoint("GET", "/tests", request => Response.Json(200, null)),
                new StubEndpoint("POST", "/tests", request => Response.Json(200, null)),
            });

            var operations = (JObject)document["paths"]["/tests"];

            Assert.IsNotNull(operations["get"]);
            Assert.IsNotNull(operations["post"]);
        }

        [Test]
        public void AdvertisesTheAddressItWasReachedOn()
        {
            var document = DocumentOf(new List<IEndpoint>());

            Assert.AreEqual("http://localhost:8787", document["servers"][0]["url"].Value<string>());
        }
    }
}
