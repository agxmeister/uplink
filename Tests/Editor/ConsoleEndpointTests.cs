using System;
using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Console;
using Agxmeister.Uplink.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class ConsoleEndpointTests
    {
        /// <summary>Answers nothing, but remembers what it was asked — which is what the parameters are.</summary>
        private sealed class RecordingReader : IConsoleReader
        {
            public ConsoleQuery Asked { get; private set; }

            public ConsolePage Read(ConsoleQuery query)
            {
                Asked = query;
                return new ConsolePage { Entries = new List<ConsoleEntry>(), Counts = new ConsoleCounts() };
            }

            public long Tail
            {
                get { return 0; }
            }
        }

        private static Request Query(IDictionary<string, string> query)
        {
            return Requests.Of("GET", "/console", query);
        }

        [Test]
        public void ReadsEverythingWhenAskedForNothingInParticular()
        {
            var reader = new RecordingReader();

            new ConsoleEndpoint(reader).Handle(Requests.Of("GET", "/console"));

            Assert.AreEqual(ConsoleLevel.Log, reader.Asked.Level);
            Assert.AreEqual(0, reader.Asked.Since);
            Assert.AreEqual(100, reader.Asked.Limit);
            Assert.IsNull(reader.Asked.Search);
            Assert.IsTrue(reader.Asked.StackTraces);
        }

        [Test]
        public void PassesEveryParameterThrough()
        {
            var reader = new RecordingReader();

            new ConsoleEndpoint(reader).Handle(Query(new Dictionary<string, string>
            {
                { "level", "error" },
                { "since", "42" },
                { "limit", "10" },
                { "search", "Null" },
                { "stackTraces", "false" },
            }));

            Assert.AreEqual(ConsoleLevel.Error, reader.Asked.Level);
            Assert.AreEqual(42, reader.Asked.Since);
            Assert.AreEqual(10, reader.Asked.Limit);
            Assert.AreEqual("Null", reader.Asked.Search);
            Assert.IsFalse(reader.Asked.StackTraces);
        }

        [Test]
        public void RefusesALimitBeyondWhatItWillServe()
        {
            var endpoint = new ConsoleEndpoint(new RecordingReader());

            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/console", "limit", "5000")));
        }

        [Test]
        public void RefusesASeverityItDoesNotHave()
        {
            var endpoint = new ConsoleEndpoint(new RecordingReader());

            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/console", "level", "fatal")));
        }

        [Test]
        public void DescribesEveryFieldItActuallyReturns()
        {
            var buffer = new ConsoleBuffer();
            buffer.Record(ConsoleLevel.Error, "boom", "at Thing.Do()", DateTime.UtcNow);
            var endpoint = new ConsoleEndpoint(buffer);

            var body = JObject.Parse(
                Encoding.UTF8.GetString(endpoint.Handle(Requests.Of("GET", "/console")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }

            var entry = described["entries"]["items"]["properties"];
            foreach (var field in (JObject)body["entries"][0])
            {
                Assert.IsNotNull(entry[field.Key], string.Format("entry '{0}' is returned but not described.", field.Key));
            }
        }

        [Test]
        public void DescribesEveryParameterItAccepts()
        {
            var described = JObject.FromObject(new ConsoleEndpoint(new RecordingReader()).Describe());
            var names = new List<string>();
            foreach (var parameter in (JArray)described["parameters"])
            {
                names.Add(parameter["name"].Value<string>());
            }

            CollectionAssert.AreEquivalent(
                new[] { "level", "since", "limit", "search", "stackTraces" }, names);
        }
    }
}
