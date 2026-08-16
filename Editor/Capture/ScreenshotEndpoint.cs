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
                "Pass `path` to write the PNG to a file on the machine running the Editor and get " +
                "`{path, view, width, height}` back instead of image data — the right choice for a client " +
                "that reads images from files, since nothing binary or base64 crosses the transport. Pass " +
                "`crop=x,y,width,height` (pixels from the top-left of the rendered image) to keep only a " +
                "region: render large and crop small to inspect a detail without any image tooling.\n\n" +
                "Without `path`, the image comes back base64-encoded inside JSON by default, because an " +
                "MCP adapter generally reads a response as text and would corrupt raw PNG bytes. Ask for " +
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
                    Schema.QueryParameter(
                        "path",
                        "Write the PNG to this absolute file path on the machine running the Editor and " +
                        "return the path instead of the image data. Overrides 'format'.",
                        Schema.Property("string", "An absolute file path, such as /tmp/shot.png."), false),
                    Schema.QueryParameter(
                        "crop",
                        "Keep only this region of the rendered image, as 'x,y,width,height' in pixels " +
                        "from its top-left corner, applied after rendering.",
                        Schema.Property("string", "Four comma-separated integers, such as 800,400,320,180."),
                        false),
                },
                null);
        }

        public Response Handle(Request request)
        {
            var arguments = new Arguments(request);
            // Base64 by default: an adapter that reads every response as text turns a raw PNG into mangled
            // characters, and a broken screenshot is worse than a verbose one.
            var format = arguments.Choice("format", Base64, Formats);
            var path = arguments.String("path", null);

            var taken = capture.Take(new CaptureRequest
            {
                View = arguments.Choice("view", CaptureView.Camera, CaptureView.All),
                Camera = arguments.String("camera", null),
                Width = arguments.Int("width", 1280, 16, 4096),
                Height = arguments.Int("height", 720, 16, 4096),
                Crop = Crop(arguments.String("crop", null)),
            });

            if (path != null)
            {
                Save(path, taken.Png);
                return Response.Json(200, new Image
                {
                    View = taken.View,
                    Width = taken.Width,
                    Height = taken.Height,
                    Path = path,
                });
            }

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

        /// <summary>`x,y,width,height` as a rectangle, or null when no crop was asked for.</summary>
        private static CaptureRect Crop(string raw)
        {
            if (raw == null)
            {
                return null;
            }

            var parts = raw.Split(',');
            int x = 0, y = 0, width = 0, height = 0;
            var wellFormed = parts.Length == 4
                && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y)
                && int.TryParse(parts[2], out width) && int.TryParse(parts[3], out height);

            if (!wellFormed || x < 0 || y < 0 || width < 1 || height < 1)
            {
                throw new BadRequestException(string.Format(
                    "'crop' must be four integers 'x,y,width,height' from the image's top-left corner, " +
                    "with a positive size, not '{0}'.", raw));
            }

            return new CaptureRect { X = x, Y = y, Width = width, Height = height };
        }

        /// <summary>
        /// Writes the PNG where the client asked. Anything that goes wrong here is the path's fault, not the
        /// Editor's, so it reads as a 400 naming the path rather than a 500.
        /// </summary>
        private static void Save(string path, byte[] png)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }
                System.IO.File.WriteAllBytes(path, png);
            }
            catch (Exception exception)
            {
                throw new BadRequestException(string.Format(
                    "Cannot write the image to '{0}': {1}", path, exception.Message));
            }
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
                { "width", Schema.Property("integer", "Width of the image in pixels, after any crop.") },
                { "height", Schema.Property("integer", "Height of the image in pixels, after any crop.") },
                { "image", Schema.Property("string", "The PNG, base64-encoded. Absent when 'path' was given.") },
                { "path", Schema.Property("string", "Where the PNG was written, when 'path' was given.") },
            });
        }

        /// <summary>The JSON response: the image itself as base64, or the path it was written to instead.</summary>
        private sealed class Image
        {
            [JsonProperty("view")]
            public string View { get; set; }

            [JsonProperty("width")]
            public int Width { get; set; }

            [JsonProperty("height")]
            public int Height { get; set; }

            [JsonProperty("image", NullValueHandling = NullValueHandling.Ignore)]
            public string Data { get; set; }

            [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
            public string Path { get; set; }
        }
    }
}
