using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Capture
{
    /// <summary>
    /// `GET /screenshot`: what the game or the scene currently looks like, as a PNG.
    /// </summary>
    public sealed class ScreenshotEndpoint : IEndpoint
    {
        public const string Png = "png";
        public const string Base64 = "base64";

        private const string PngContentType = "image/png";

        /// <summary>Names the view actually rendered, which is not always the one asked for.</summary>
        private const string ViewHeader = "X-Uplink-View";

        private static readonly string[] Formats = { Png, Base64 };

        private readonly IViewCapture capture;

        public ScreenshotEndpoint(IViewCapture capture)
        {
            if (capture == null)
            {
                throw new ArgumentNullException("capture");
            }
            this.capture = capture;
        }

        public string Method
        {
            get { return "GET"; }
        }

        public string Path
        {
            get { return "/screenshot"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "screenshot",
                "Capture what the Unity Editor is showing, as a PNG.",
                "Use this to check that a change looks right, not merely that it compiles.\n\n" +
                "`view=camera` renders a camera in the scene at whatever size is asked for and works " +
                "whether or not the game is running — the dependable choice. `view=game` grabs the Game " +
                "view as a person would see it, including anything drawn over the camera, but only while " +
                "play mode is running. `view=scene` renders the Scene view, which is useful for looking " +
                "at objects the game camera cannot see.\n\n" +
                "A view that cannot draw falls back to one that can — a scene with no enabled camera is " +
                "captured from the Scene view, and a closed Scene view from a camera — so asking for a " +
                "picture generally gets one. The `view` field and the `X-Uplink-View` header always name " +
                "what was really rendered, so a fallback is never silent. Naming a `camera` is the " +
                "exception: if that camera is missing or disabled, the call fails rather than " +
                "photographing a different one.\n\n" +
                "The image comes back base64-encoded inside JSON by default, because an MCP adapter " +
                "generally reads a response as text and would corrupt raw PNG bytes. Ask for " +
                "`format=png` to get the PNG itself, which is what a browser or `curl -o` wants.",
                new Dictionary<string, object>
                {
                    {
                        "200", Schema.Contents("The image.", new Dictionary<string, object>
                        {
                            { PngContentType, BinarySchema() },
                            { Response.JsonContentType, ImageSchema() },
                        })
                    },
                    { "400", Schema.ErrorContent("A parameter was not understood, or there was nothing to render.") },
                    { "504", Schema.ErrorContent("The Editor was too busy to answer. Retry.") },
                },
                new List<object>
                {
                    Schema.QueryParameter(
                        "view", "Which view to render.",
                        Schema.Choice("The view.", CaptureView.All, CaptureView.Camera), false),
                    Schema.QueryParameter(
                        "camera", "Name of the camera to render; defaults to the scene's main camera.",
                        Schema.Property("string", "A camera GameObject's name."), false),
                    Schema.QueryParameter(
                        "width", "Width of the image in pixels.",
                        Schema.Property("integer", "Between 16 and 4096.", 1280), false),
                    Schema.QueryParameter(
                        "height", "Height of the image in pixels.",
                        Schema.Property("integer", "Between 16 and 4096.", 720), false),
                    Schema.QueryParameter(
                        "format", "How to return the image.",
                        Schema.Choice("The image inside JSON, or raw PNG bytes.", Formats, Base64), false),
                },
                null);
        }

        public Response Handle(Request request)
        {
            var arguments = new Arguments(request);
            // Base64 by default: an adapter that reads every response as text turns a raw PNG into mangled
            // characters, and a broken screenshot is worse than a verbose one.
            var format = arguments.Choice("format", Base64, Formats);

            var taken = capture.Take(new CaptureRequest
            {
                View = arguments.Choice("view", CaptureView.Camera, CaptureView.All),
                Camera = arguments.String("camera", null),
                Width = arguments.Int("width", 1280, 16, 4096),
                Height = arguments.Int("height", 720, 16, 4096),
            });

            if (format == Base64)
            {
                return Response.Json(200, new Image
                {
                    View = taken.View,
                    Width = taken.Width,
                    Height = taken.Height,
                    Data = Convert.ToBase64String(taken.Png),
                });
            }

            return Response.Bytes(200, PngContentType, taken.Png).With(ViewHeader, taken.View);
        }

        private static IDictionary<string, object> BinarySchema()
        {
            return new Dictionary<string, object>
            {
                { "type", "string" },
                { "format", "binary" },
                { "description", "The PNG itself. The X-Uplink-View header names the view rendered." },
            };
        }

        private static IDictionary<string, object> ImageSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                { "view", Schema.Choice("The view actually rendered.", CaptureView.All, null) },
                { "width", Schema.Property("integer", "Width of the image in pixels.") },
                { "height", Schema.Property("integer", "Height of the image in pixels.") },
                { "image", Schema.Property("string", "The PNG, base64-encoded.") },
            });
        }

        /// <summary>The `format=base64` response.</summary>
        private sealed class Image
        {
            [JsonProperty("view")]
            public string View { get; set; }

            [JsonProperty("width")]
            public int Width { get; set; }

            [JsonProperty("height")]
            public int Height { get; set; }

            [JsonProperty("image")]
            public string Data { get; set; }
        }
    }
}
