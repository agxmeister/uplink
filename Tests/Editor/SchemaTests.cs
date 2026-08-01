using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    /// <summary>
    /// The builders are only worth anything if what they produce survives serialization into the document an
    /// adapter actually reads, so these assert on the parsed JSON rather than on the dictionaries.
    /// </summary>
    [TestFixture]
    public sealed class SchemaTests
    {
        private static JObject OperationIn(IDictionary<string, object> description)
        {
            var endpoints = new List<IEndpoint>
            {
                new StubEndpoint("GET", "/thing", request => Response.Json(200, null), description),
            };
            var endpoint = new OpenApiEndpoint(endpoints, "Uplink", "Test.", "0.2.0");
            endpoints.Add(endpoint);

            var body = Encoding.UTF8.GetString(endpoint.Handle(Requests.Of("GET", "/openapi.json")).Body);
            return (JObject)JObject.Parse(body)["paths"]["/thing"]["get"];
        }

        private static IDictionary<string, object> Rich()
        {
            return Schema.Operation(
                "thing", "A thing.", "Does a thing.",
                new Dictionary<string, object>
                {
                    { "200", Schema.JsonContent("Done.", Schema.Object(new Dictionary<string, object>())) },
                    { "202", Schema.JsonContent("Working.", Schema.Object(new Dictionary<string, object>())) },
                },
                new List<object>
                {
                    Schema.QueryParameter(
                        "level", "How much to say.",
                        Schema.Choice("Verbosity.", new[] { "all", "error" }, "all"), false),
                    Schema.QueryParameter("limit", "How many.", Schema.Property("integer", "Count.", 100), false),
                },
                Schema.JsonBody("What to do.", Schema.Object(new Dictionary<string, object>
                {
                    { "mode", Schema.Property("string", "Which mode.") },
                }), false));
        }

        [Test]
        public void DescribesEveryStatusTheEndpointCanAnswerWith()
        {
            var operation = OperationIn(Rich());

            Assert.IsNotNull(operation["responses"]["200"]);
            Assert.IsNotNull(operation["responses"]["202"]);
        }

        [Test]
        public void CarriesQueryParametersWithTheirChoicesAndDefaults()
        {
            var parameters = (JArray)OperationIn(Rich())["parameters"];

            Assert.AreEqual("level", parameters[0]["name"].Value<string>());
            Assert.AreEqual("query", parameters[0]["in"].Value<string>());
            Assert.AreEqual("all", parameters[0]["schema"]["default"].Value<string>());
            CollectionAssert.AreEqual(
                new[] { "all", "error" }, parameters[0]["schema"]["enum"].ToObject<string[]>());
            Assert.AreEqual(100, parameters[1]["schema"]["default"].Value<int>());
        }

        [Test]
        public void CarriesTheRequestBodySchema()
        {
            var body = OperationIn(Rich())["requestBody"];

            Assert.IsNotNull(body["content"]["application/json"]["schema"]["properties"]["mode"]);
        }

        [Test]
        public void OmitsInputsAnEndpointDoesNotTake()
        {
            var operation = OperationIn(Schema.Operation(
                "thing", "A thing.", "Does a thing.",
                Schema.Object(new Dictionary<string, object>()), "Done."));

            Assert.IsNull(operation["parameters"]);
            Assert.IsNull(operation["requestBody"]);
            Assert.IsNotNull(operation["responses"]["200"]);
        }

        [Test]
        public void DescribesANonJsonResponseAsBinary()
        {
            var operation = OperationIn(Schema.Operation(
                "shot", "A picture.", "Takes a picture.",
                new Dictionary<string, object>
                {
                    { "200", Schema.BinaryContent("The image.", "image/png") },
                },
                null, null));

            var content = operation["responses"]["200"]["content"]["image/png"];

            Assert.AreEqual("binary", content["schema"]["format"].Value<string>());
        }
    }
}
