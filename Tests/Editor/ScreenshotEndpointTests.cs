using System;
using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Capture;
using Agxmeister.Uplink.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class ScreenshotEndpointTests
    {
        /// <summary>The four bytes every PNG starts with, which is enough to tell one from JSON.</summary>
        private static readonly byte[] PngBytes = { 0x89, (byte)'P', (byte)'N', (byte)'G' };

        private sealed class StubCapture : IViewCapture
        {
            private readonly string rendered;

            public StubCapture() : this(CaptureView.Camera)
            {
            }

            public StubCapture(string rendered)
            {
                this.rendered = rendered;
            }

            public CaptureRequest Asked { get; private set; }

            public CaptureResult Take(CaptureRequest request)
            {
                Asked = request;
                return new CaptureResult
                {
                    View = rendered,
                    Width = request.Width,
                    Height = request.Height,
                    Png = PngBytes,
                };
            }
        }

        [Test]
        public void RendersTheMainCameraAtAKnownSizeWhenAskedForNothingInParticular()
        {
            var capture = new StubCapture();

            new ScreenshotEndpoint(capture).Handle(Requests.Of("GET", "/screenshot"));

            Assert.AreEqual(CaptureView.Camera, capture.Asked.View);
            Assert.IsNull(capture.Asked.Camera);
            Assert.AreEqual(1280, capture.Asked.Width);
            Assert.AreEqual(720, capture.Asked.Height);
        }

        [Test]
        public void PassesEveryParameterThrough()
        {
            var capture = new StubCapture();

            new ScreenshotEndpoint(capture).Handle(Requests.Of("GET", "/screenshot", new Dictionary<string, string>
            {
                { "view", "scene" },
                { "camera", "Overhead" },
                { "width", "800" },
                { "height", "600" },
            }));

            Assert.AreEqual(CaptureView.Scene, capture.Asked.View);
            Assert.AreEqual("Overhead", capture.Asked.Camera);
            Assert.AreEqual(800, capture.Asked.Width);
            Assert.AreEqual(600, capture.Asked.Height);
        }

        [Test]
        public void EncodesTheImageIntoJsonByDefaultSoAnAdapterCannotCorruptIt()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture());

            var response = endpoint.Handle(Requests.Of("GET", "/screenshot"));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual("application/json", response.ContentType);
            Assert.AreEqual(CaptureView.Camera, body["view"].Value<string>());
            CollectionAssert.AreEqual(PngBytes, Convert.FromBase64String(body["image"].Value<string>()));
        }

        [Test]
        public void ServesThePngItselfWhenAskedFor()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture());

            var response = endpoint.Handle(Requests.With("GET", "/screenshot", "format", "png"));

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual("image/png", response.ContentType);
            CollectionAssert.AreEqual(PngBytes, response.Body);
        }

        [Test]
        public void SaysWhichViewItReallyRendered()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture(CaptureView.Camera));

            // The Game view cannot be grabbed outside play mode, so asking for it yields a camera render.
            var response = endpoint.Handle(Requests.Of("GET", "/screenshot", new Dictionary<string, string>
            {
                { "view", "game" },
                { "format", "png" },
            }));

            Assert.AreEqual(CaptureView.Camera, response.Headers["X-Uplink-View"]);
        }

        [Test]
        public void RefusesASizeItWillNotRender()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture());

            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/screenshot", "width", "99999")));
        }

        [Test]
        public void RefusesAViewItDoesNotHave()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture());

            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/screenshot", "view", "inspector")));
        }

        [Test]
        public void DescribesBothFormsItCanAnswerIn()
        {
            var described = JObject.FromObject(new ScreenshotEndpoint(new StubCapture()).Describe());
            var content = described["responses"]["200"]["content"];

            Assert.AreEqual("binary", content["image/png"]["schema"]["format"].Value<string>());
            Assert.IsNotNull(content["application/json"]["schema"]["properties"]["image"]);
        }

        [Test]
        public void DescribesEveryFieldItsJsonFormReturns()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture());

            var body = JObject.Parse(Encoding.UTF8.GetString(
                endpoint.Handle(Requests.Of("GET", "/screenshot")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }
        }
    }
}
