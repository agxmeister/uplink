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

            /// <summary>Set to make the stub answer the way a viewpoint render does — with a pose.</summary>
            public CaptureResult Pose { get; set; }

            public CaptureResult Take(CaptureRequest request)
            {
                Asked = request;
                return new CaptureResult
                {
                    View = rendered,
                    Width = request.Width,
                    Height = request.Height,
                    Png = PngBytes,
                    From = Pose == null ? null : Pose.From,
                    At = Pose == null ? null : Pose.At,
                    Fov = Pose == null ? null : Pose.Fov,
                    Ortho = Pose == null ? null : Pose.Ortho,
                };
            }
        }

        /// <summary>A `view=viewpoint` request, with whatever else the case under test needs.</summary>
        private static Request Viewpoint(params string[] pairs)
        {
            var query = new Dictionary<string, string> { { "view", "viewpoint" } };
            for (var i = 0; i < pairs.Length; i += 2)
            {
                query[pairs[i]] = pairs[i + 1];
            }
            return Requests.Of("GET", "/screenshot", query);
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
        public void PassesTheCropThrough()
        {
            var capture = new StubCapture();

            new ScreenshotEndpoint(capture).Handle(
                Requests.With("GET", "/screenshot", "crop", "800,400,320,180"));

            Assert.AreEqual(800, capture.Asked.Crop.X);
            Assert.AreEqual(400, capture.Asked.Crop.Y);
            Assert.AreEqual(320, capture.Asked.Crop.Width);
            Assert.AreEqual(180, capture.Asked.Crop.Height);
        }

        [Test]
        public void AsksForNoCropWhenNoneWasGiven()
        {
            var capture = new StubCapture();

            new ScreenshotEndpoint(capture).Handle(Requests.Of("GET", "/screenshot"));

            Assert.IsNull(capture.Asked.Crop);
        }

        [Test]
        public void RefusesACropItCannotRead()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture());

            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/screenshot", "crop", "10,10")));
            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/screenshot", "crop", "10,10,0,50")));
            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/screenshot", "crop", "-1,10,50,50")));
        }

        [Test]
        public void WritesThePngToAPathAndAnswersWithThePathInstead()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture());
            var path = System.IO.Path.Combine(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uplink-tests"), "shot.png");

            try
            {
                var response = endpoint.Handle(Requests.With("GET", "/screenshot", "path", path));
                var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

                Assert.AreEqual(200, response.Status);
                Assert.AreEqual("application/json", response.ContentType);
                Assert.AreEqual(path, body["path"].Value<string>());
                Assert.IsNull(body["image"], "nothing binary or base64 should cross the transport");
                CollectionAssert.AreEqual(PngBytes, System.IO.File.ReadAllBytes(path));
            }
            finally
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
        }

        [Test]
        public void ReportsAPathItCannotWriteToAsTheClientsMistake()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture());

            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/screenshot", "path", "\0not-a-path")));
        }

        [Test]
        public void DescribesEveryParameterItAccepts()
        {
            var described = JObject.FromObject(new ScreenshotEndpoint(new StubCapture()).Describe());
            var names = new List<string>();
            foreach (var parameter in (JArray)described["parameters"])
            {
                names.Add(parameter["name"].Value<string>());
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "view", "camera", "width", "height", "format", "path", "crop",
                    "from", "at", "dir", "frame", "axis", "fov", "ortho", "near", "far",
                },
                names);
        }

        [Test]
        public void HandsTheWholeViewpointToTheCaptureIntact()
        {
            var capture = new StubCapture(CaptureView.Viewpoint);

            new ScreenshotEndpoint(capture).Handle(Viewpoint(
                "from", "-20,1.85,-13.5", "at", "-20,1.85,0", "fov", "50", "near", "0.5", "far", "400"));

            var viewpoint = capture.Asked.Viewpoint;
            Assert.AreEqual(CaptureView.Viewpoint, capture.Asked.View);
            Assert.AreEqual(-20f, viewpoint.From.Value.X);
            Assert.AreEqual(1.85f, viewpoint.From.Value.Y);
            Assert.AreEqual(-13.5f, viewpoint.From.Value.Z);
            Assert.AreEqual(0f, viewpoint.At.Value.Z);
            Assert.IsFalse(viewpoint.Dir.HasValue);
            Assert.AreEqual(50f, viewpoint.Fov.Value);
            Assert.IsFalse(viewpoint.Ortho.HasValue);
            Assert.AreEqual(0.5f, viewpoint.Near.Value);
            Assert.AreEqual(400f, viewpoint.Far.Value);
        }

        [Test]
        public void HandsAFramedViewpointToTheCaptureIntact()
        {
            var capture = new StubCapture(CaptureView.Viewpoint);

            new ScreenshotEndpoint(capture).Handle(Viewpoint(
                "frame", "/MenuScreen/MenuSlider/MenuHall", "axis", "top", "ortho", "6.5"));

            var viewpoint = capture.Asked.Viewpoint;
            Assert.AreEqual("/MenuScreen/MenuSlider/MenuHall", viewpoint.Frame);
            Assert.AreEqual(CaptureAxis.Top, viewpoint.Axis);
            Assert.AreEqual(6.5f, viewpoint.Ortho.Value);
            Assert.IsFalse(viewpoint.Fov.HasValue, "'ortho' and 'fov' cannot both be in force");
            Assert.IsFalse(viewpoint.From.HasValue);
        }

        [Test]
        public void LooksStraightAheadFromAPositionGivenOnItsOwn()
        {
            var capture = new StubCapture(CaptureView.Viewpoint);

            new ScreenshotEndpoint(capture).Handle(Viewpoint("from", "0,1,0"));

            var viewpoint = capture.Asked.Viewpoint;
            Assert.AreEqual(0f, viewpoint.Dir.Value.X);
            Assert.AreEqual(0f, viewpoint.Dir.Value.Y);
            Assert.AreEqual(1f, viewpoint.Dir.Value.Z);
            Assert.AreEqual(60f, viewpoint.Fov.Value, "a viewpoint has a field of view even when unasked");
            Assert.AreEqual(CaptureAxis.Front, viewpoint.Axis);
        }

        [Test]
        public void AsksForNoViewpointWhenSomeOtherViewWasWanted()
        {
            var capture = new StubCapture();

            new ScreenshotEndpoint(capture).Handle(Requests.Of("GET", "/screenshot"));

            Assert.IsNull(capture.Asked.Viewpoint);
        }

        [Test]
        public void RefusesAViewpointItCannotPlace()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture(CaptureView.Viewpoint));

            // Neither 'from' nor 'frame': nothing says where the camera goes.
            Assert.Throws<BadRequestException>(() => endpoint.Handle(Viewpoint()));
            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Viewpoint("from", "0,0,0", "frame", "/Player")));
        }

        [Test]
        public void RefusesTwoWaysOfSayingTheSameThing()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture(CaptureView.Viewpoint));

            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Viewpoint("from", "0,0,0", "at", "1,1,1", "dir", "0,0,1")));
            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Viewpoint("from", "0,0,0", "fov", "50", "ortho", "5")));
        }

        [Test]
        public void RefusesAnAimItWouldHaveToIgnore()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture(CaptureView.Viewpoint));

            // 'frame' works its own direction out, so an 'at' beside it could only be dropped.
            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Viewpoint("frame", "/Player", "at", "1,1,1")));
            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Viewpoint("frame", "/Player", "dir", "0,0,1")));
            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Viewpoint("from", "0,0,0", "axis", "top")));
        }

        [Test]
        public void RefusesATripleItCannotRead()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture(CaptureView.Viewpoint));

            Assert.Throws<BadRequestException>(() => endpoint.Handle(Viewpoint("from", "1,2")));
            Assert.Throws<BadRequestException>(() => endpoint.Handle(Viewpoint("from", "1,2,over-there")));
        }

        [Test]
        public void NamesAViewpointParameterItWouldOtherwiseHaveToDrop()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture());

            // Forgetting 'view=viewpoint' is the likeliest way to get here, and a picture of the main camera
            // would look like agreement.
            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/screenshot", "frame", "/Player")));
            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/screenshot", "axis", "top")));
            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/screenshot", "from", "0,0,0")));
        }

        [Test]
        public void EchoesThePoseItWasActuallyGiven()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture(CaptureView.Viewpoint)
            {
                Pose = new CaptureResult
                {
                    From = new Point(-20f, 1.85f, -13.5f),
                    At = new Point(-20f, 1.85f, 0f),
                    Fov = 50f,
                },
            });

            var body = JObject.Parse(Encoding.UTF8.GetString(
                endpoint.Handle(Viewpoint("frame", "/MenuScreen")).Body));

            CollectionAssert.AreEqual(new[] { -20f, 1.85f, -13.5f }, body["from"].ToObject<float[]>());
            CollectionAssert.AreEqual(new[] { -20f, 1.85f, 0f }, body["at"].ToObject<float[]>());
            Assert.AreEqual(50f, body["fov"].Value<float>());
            Assert.IsNull(body["ortho"], "an orthographic size is not something a perspective shot has");
        }

        [Test]
        public void LeavesTheOtherViewsAnswersExactlyAsTheyWere()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture());

            var body = JObject.Parse(Encoding.UTF8.GetString(
                endpoint.Handle(Requests.Of("GET", "/screenshot")).Body));

            var fields = new List<string>();
            foreach (var field in body)
            {
                fields.Add(field.Key);
            }

            CollectionAssert.AreEquivalent(new[] { "view", "width", "height", "image" }, fields);
        }

        [Test]
        public void DescribesEveryFieldAViewpointAnswerReturns()
        {
            var endpoint = new ScreenshotEndpoint(new StubCapture(CaptureView.Viewpoint)
            {
                Pose = new CaptureResult { From = new Point(1f, 2f, 3f), At = new Point(0f, 0f, 0f), Ortho = 4f },
            });

            var body = JObject.Parse(Encoding.UTF8.GetString(
                endpoint.Handle(Viewpoint("frame", "/MenuScreen")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }
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
