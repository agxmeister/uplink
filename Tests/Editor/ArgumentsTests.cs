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
        public void ReadsAFractionalNumberInTheInvariantCulture()
        {
            Assert.AreEqual(1.85f, For("fov", "1.85").Float("fov", 60f, 1f, 179f));
            Assert.AreEqual(60f, new Arguments(Requests.Of("GET", "/screenshot")).Float("fov", 60f, 1f, 179f));
            Assert.Throws<BadRequestException>(() => For("fov", "1,85").Float("fov", 60f, 1f, 179f));
            Assert.Throws<BadRequestException>(() => For("fov", "200").Float("fov", 60f, 1f, 179f));
        }

        [Test]
        public void ReadsAPositionAsThreeNumbers()
        {
            var triple = For("from", "-20,1.85,-13.5").Triple("from");

            Assert.AreEqual(-20f, triple[0]);
            Assert.AreEqual(1.85f, triple[1]);
            Assert.AreEqual(-13.5f, triple[2]);
            Assert.IsNull(new Arguments(Requests.Of("GET", "/screenshot")).Triple("from"));
        }

        [Test]
        public void RejectsATripleOfTheWrongLengthOrShape()
        {
            Assert.Throws<BadRequestException>(() => For("from", "1,2").Triple("from"));
            Assert.Throws<BadRequestException>(() => For("from", "1,2,3,4").Triple("from"));
            Assert.Throws<BadRequestException>(() => For("from", "1,2,here").Triple("from"));
        }

        [Test]
        public void ReadsARectangleAsFourWholeNumbers()
        {
            var quad = For("crop", "800,400,320,180").Quad("crop", "x,y,width,height", new[] { 0, 0, 1, 1 });

            CollectionAssert.AreEqual(new[] { 800, 400, 320, 180 }, quad);
            Assert.IsNull(
                new Arguments(Requests.Of("GET", "/screenshot")).Quad("crop", "x,y,width,height", new[] { 0, 0, 1, 1 }));
        }

        [Test]
        public void RejectsARectangleThatBreaksOneOfItsMinimums()
        {
            // The minimums are the range rules, component by component, so they say which one was wrong.
            var exception = Assert.Throws<BadRequestException>(
                () => For("crop", "10,10,0,50").Quad("crop", "x,y,width,height", new[] { 0, 0, 1, 1 }));

            StringAssert.Contains("'width'", exception.Message);
            Assert.Throws<BadRequestException>(
                () => For("crop", "-1,10,50,50").Quad("crop", "x,y,width,height", new[] { 0, 0, 1, 1 }));
            Assert.Throws<BadRequestException>(
                () => For("crop", "10,10").Quad("crop", "x,y,width,height", new[] { 0, 0, 1, 1 }));
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
