using Agxmeister.Uplink.Http;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class ArgumentsTests
    {
        private sealed class Options
        {
            public string Mode { get; set; }
        }

        private static Arguments For(string name, string value)
        {
            return new Arguments(Requests.With("GET", "/console", name, value));
        }

        [Test]
        public void FallsBackWhenAParameterIsAbsent()
        {
            var arguments = new Arguments(Requests.Of("GET", "/console"));

            Assert.AreEqual(100, arguments.Int("limit", 100, 1, 500));
            Assert.AreEqual("all", arguments.Choice("level", "all", new[] { "all", "error" }));
            Assert.IsTrue(arguments.Bool("stackTraces", true));
        }

        [Test]
        public void RejectsSomethingThatIsNotANumber()
        {
            Assert.Throws<BadRequestException>(() => For("limit", "lots").Int("limit", 100, 1, 500));
        }

        [Test]
        public void RejectsANumberOutsideTheAcceptedRange()
        {
            Assert.Throws<BadRequestException>(() => For("limit", "5000").Int("limit", 100, 1, 500));
            Assert.Throws<BadRequestException>(() => For("limit", "0").Int("limit", 100, 1, 500));
        }

        [Test]
        public void RejectsAValueOutsideTheDeclaredChoices()
        {
            Assert.Throws<BadRequestException>(
                () => For("level", "critical").Choice("level", "all", new[] { "all", "error" }));
        }

        [Test]
        public void MatchesAChoiceLooselyButReturnsTheDeclaredSpelling()
        {
            Assert.AreEqual("error", For("level", "ERROR").Choice("level", "all", new[] { "all", "error" }));
        }

        [Test]
        public void ReadsTheUsualSpellingsOfABoolean()
        {
            Assert.IsTrue(For("stackTraces", "1").Bool("stackTraces", false));
            Assert.IsFalse(For("stackTraces", "no").Bool("stackTraces", true));
            Assert.Throws<BadRequestException>(() => For("stackTraces", "maybe").Bool("stackTraces", true));
        }

        [Test]
        public void ReadsAnAbsentBodyAsDefaultOptions()
        {
            var options = new Arguments(Requests.Of("POST", "/tests")).Body<Options>();

            Assert.IsNotNull(options);
            Assert.IsNull(options.Mode);
        }

        [Test]
        public void ReadsABodyIntoItsOptions()
        {
            var request = Requests.Of("POST", "/tests", "{\"mode\":\"edit\"}");

            Assert.AreEqual("edit", new Arguments(request).Body<Options>().Mode);
        }

        [Test]
        public void RejectsABodyThatIsNotJson()
        {
            var request = Requests.Of("POST", "/tests", "{not json");

            Assert.Throws<BadRequestException>(() => new Arguments(request).Body<Options>());
        }
    }
}
