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

        private const float DefaultFov = 60f;

        /// <summary>
        /// The range every world-space size — an orthographic half-height, a clip plane — is read within.
        /// Zero and below are not sizes, and beyond this a float's precision is worse than the scene is wide.
        /// </summary>
        private const float MinimumSize = 0.0001f;

        private const float MaximumSize = 1000000f;

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
                "**`view=viewpoint` puts a camera of Uplink's own wherever you say**, which is how to " +
                "photograph something no camera in the scene is pointed at. Give it either `from` (a world " +
                "position as 'x,y,z') with `at` or `dir` to aim it, or `frame` (an object path exactly as " +
                "`read_scene` reports it, such as '/MenuScreen/MenuSlider/MenuHall') and let Uplink measure " +
                "that subtree's renderers and stand back far enough to fit them. With `frame`, `axis` picks " +
                "the side to look from — front (the default), back, left, right, top, bottom. `fov` is the " +
                "vertical field of view in degrees, default 60; `ortho` swaps to an orthographic camera of " +
                "that half-height instead. `near` and `far` are clip planes, and are worked out from the fit " +
                "if you leave them alone. The response echoes the `from`, `at` and `fov`/`ortho` actually " +
                "used, so a shot that framed badly can be nudged by editing numbers rather than guessed at " +
                "again. Unlike every other view this one never falls back: a viewpoint is an explicit " +
                "request, so a `frame` that names nothing, or a subtree with nothing to draw, fails rather " +
                "than photographing somewhere else. Its rendering settings — clear flags, background colour " +
                "and culling mask — are copied from the scene's main camera when there is one, so the " +
                "picture resembles what the game would draw; with no main camera the background is solid " +
                "black and every layer is on. The camera is temporary, hidden, and destroyed before the " +
                "response is written: nothing is added to the scene and nothing is left dirty.\n\n" +
                "A view that cannot draw falls back to one that can — a scene with no enabled camera is " +
                "captured from the Scene view, and a closed Scene view from a camera — so asking for a " +
                "picture generally gets one. The `view` field and the `X-Uplink-View` header always name " +
                "what was really rendered, so a fallback is never silent. Naming a `camera` is the other " +
                "exception, beside `view=viewpoint`: if that camera is missing or disabled, the call fails " +
                "rather than photographing a different one.\n\n" +
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
                    Schema.QueryParameter(
                        "from",
                        "Where to put the camera, in world space. Only with 'view=viewpoint', and only " +
                        "instead of 'frame'.",
                        Schema.Property("string", "Three comma-separated numbers, such as -20,1.85,-13.5."),
                        false),
                    Schema.QueryParameter(
                        "at",
                        "The world-space point to aim at from 'from'. Mutually exclusive with 'dir'.",
                        Schema.Property("string", "Three comma-separated numbers, such as -20,1.85,0."),
                        false),
                    Schema.QueryParameter(
                        "dir",
                        "The direction to look along from 'from'. Mutually exclusive with 'at'.",
                        Schema.Property("string", "Three comma-separated numbers.", "0,0,1"), false),
                    Schema.QueryParameter(
                        "frame",
                        "Fit this object's subtree in view, placing the camera automatically. A " +
                        "slash-separated path exactly as read_scene reports it. Only with " +
                        "'view=viewpoint', and only instead of 'from'.",
                        Schema.Property("string", "An object path, such as /MenuScreen/MenuSlider/MenuHall."),
                        false),
                    Schema.QueryParameter(
                        "axis", "Which side of a framed object to look at it from.",
                        Schema.Choice("The side.", CaptureAxis.All, CaptureAxis.Front), false),
                    Schema.QueryParameter(
                        "fov", "Vertical field of view in degrees. Mutually exclusive with 'ortho'.",
                        Schema.Property("number", "Between 1 and 179.", 60), false),
                    Schema.QueryParameter(
                        "ortho",
                        "Render orthographically with this half-height in world units, instead of a " +
                        "perspective 'fov'.",
                        Schema.Property("number", "Greater than zero."), false),
                    Schema.QueryParameter(
                        "near", "Near clip plane. Derived from the fit when left alone.",
                        Schema.Property("number", "Greater than zero."), false),
                    Schema.QueryParameter(
                        "far", "Far clip plane. Derived from the fit when left alone.",
                        Schema.Property("number", "Greater than 'near'."), false),
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

            var view = arguments.Choice("view", CaptureView.Camera, CaptureView.All);

            var taken = capture.Take(new CaptureRequest
            {
                View = view,
                Camera = arguments.String("camera", null),
                Width = arguments.Int("width", 1280, 16, 4096),
                Height = arguments.Int("height", 720, 16, 4096),
                Crop = Crop(arguments),
                Viewpoint = ViewpointOf(arguments, view),
            });

            if (path != null)
            {
                return Response.Json(200, Answer(taken, path, null));
            }

            if (format == Base64)
            {
                return Response.Json(200, Answer(taken, null, Convert.ToBase64String(taken.Png)));
            }

            return Response.Bytes(200, PngContentType, taken.Png).With(ViewHeader, taken.View);
        }

        /// <summary>
        /// The JSON answer, with whichever of the image and the path is being handed over. The pose rides
        /// along only when there was one — a camera or scene render has none, and a `fov: 0` in those answers
        /// would be a lie rather than an omission.
        /// </summary>
        private static Image Answer(CaptureResult taken, string path, string data)
        {
            if (path != null)
            {
                Save(path, taken.Png);
            }

            return new Image
            {
                View = taken.View,
                Width = taken.Width,
                Height = taken.Height,
                Data = data,
                Path = path,
                From = taken.From.HasValue ? taken.From.Value.ToArray() : null,
                At = taken.At.HasValue ? taken.At.Value.ToArray() : null,
                Fov = taken.Fov,
                Ortho = taken.Ortho,
            };
        }

        /// <summary>
        /// The viewpoint the caller described, or null when this is an ordinary view. Every combination rule
        /// is settled here rather than in the capture, so all of them are testable without an Editor.
        /// </summary>
        private static Viewpoint ViewpointOf(Arguments arguments, string view)
        {
            var from = arguments.Triple("from");
            var at = arguments.Triple("at");
            var dir = arguments.Triple("dir");
            var frame = arguments.String("frame", null);
            var axis = arguments.String("axis", null);
            var fov = Number(arguments, "fov", 1f, 179f);
            var ortho = Number(arguments, "ortho", MinimumSize, MaximumSize);
            var near = Number(arguments, "near", MinimumSize, MaximumSize);
            var far = Number(arguments, "far", MinimumSize, MaximumSize);

            if (view != CaptureView.Viewpoint)
            {
                // Named rather than dropped: forgetting `view=viewpoint` is the likeliest way to get here,
                // and a picture of the main camera would look like agreement.
                var stray = Stray(from, "from") ?? Stray(at, "at") ?? Stray(dir, "dir")
                    ?? Stray(frame, "frame") ?? Stray(axis, "axis") ?? Stray(fov, "fov")
                    ?? Stray(ortho, "ortho") ?? Stray(near, "near") ?? Stray(far, "far");

                if (stray != null)
                {
                    throw new BadRequestException(string.Format(
                        "'{0}' only means something with 'view=viewpoint'; this call asked for 'view={1}'.",
                        stray, view));
                }

                return null;
            }

            if (from == null && frame == null)
            {
                throw new BadRequestException(
                    "'view=viewpoint' needs either 'from' — a world position as 'x,y,z' — or 'frame', an " +
                    "object path whose renderers the camera should be placed to fit.");
            }
            if (from != null && frame != null)
            {
                throw new BadRequestException(
                    "'from' and 'frame' both say where the camera goes; give one or the other.");
            }
            if (at != null && dir != null)
            {
                throw new BadRequestException(
                    "'at' and 'dir' both say where the camera looks; give one or the other.");
            }
            if (fov.HasValue && ortho.HasValue)
            {
                throw new BadRequestException(
                    "'fov' is perspective and 'ortho' is orthographic; give one or the other.");
            }
            // A framed shot is aimed with 'axis' and a positioned one with 'at' or 'dir'. The other pairing
            // would have to be dropped, and dropping an input silently is what this endpoint refuses to do.
            if (frame != null && (at != null || dir != null))
            {
                throw new BadRequestException(string.Format(
                    "'{0}' says where to look, but 'frame' works that out for itself; aim a framed shot " +
                    "with 'axis'.", at != null ? "at" : "dir"));
            }
            if (from != null && axis != null)
            {
                throw new BadRequestException(
                    "'axis' picks a side of a framed object; aim a shot from 'from' with 'at' or 'dir'.");
            }

            return new Viewpoint
            {
                From = PointOf(from),
                At = PointOf(at),
                // Straight ahead, so `from` alone is a complete request rather than half of one.
                Dir = from != null && at == null && dir == null ? new Point(0f, 0f, 1f) : PointOf(dir),
                Frame = frame,
                Axis = arguments.Choice("axis", CaptureAxis.Front, CaptureAxis.All),
                Fov = ortho.HasValue ? (float?)null : fov ?? DefaultFov,
                Ortho = ortho,
                Near = near,
                Far = far,
            };
        }

        /// <summary>The parameter's name when it was given at all, so a chain of these finds the first one.</summary>
        private static string Stray(object value, string name)
        {
            return value == null ? null : name;
        }

        private static Point? PointOf(float[] triple)
        {
            return triple == null ? (Point?)null : new Point(triple[0], triple[1], triple[2]);
        }

        /// <summary>A number the caller may simply not have mentioned, which is different from zero.</summary>
        private static float? Number(Arguments arguments, string name, float minimum, float maximum)
        {
            return arguments.String(name, null) == null
                ? (float?)null
                : arguments.Float(name, 0f, minimum, maximum);
        }

        /// <summary>`x,y,width,height` as a rectangle, or null when no crop was asked for.</summary>
        private static CaptureRect Crop(Arguments arguments)
        {
            var quad = arguments.Quad("crop", "x,y,width,height", new[] { 0, 0, 1, 1 });
            return quad == null
                ? null
                : new CaptureRect { X = quad[0], Y = quad[1], Width = quad[2], Height = quad[3] };
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
                {
                    "from",
                    Schema.Array(
                        "Where the camera stood, as [x, y, z]. Only for 'view=viewpoint', where with " +
                        "'frame' it is Uplink's choice rather than yours.",
                        Schema.Property("number", "A world-space coordinate."))
                },
                {
                    "at",
                    Schema.Array(
                        "What the camera looked at, as [x, y, z]. Only for 'view=viewpoint'.",
                        Schema.Property("number", "A world-space coordinate."))
                },
                {
                    "fov",
                    Schema.Property("number", "The vertical field of view used, for a perspective viewpoint.")
                },
                {
                    "ortho",
                    Schema.Property("number", "The orthographic half-height used, for an orthographic viewpoint.")
                },
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

            // The pose, omitted when there was none, so a camera, game or scene answer is byte-identical to
            // what it was before viewpoints existed.
            [JsonProperty("from", NullValueHandling = NullValueHandling.Ignore)]
            public float[] From { get; set; }

            [JsonProperty("at", NullValueHandling = NullValueHandling.Ignore)]
            public float[] At { get; set; }

            [JsonProperty("fov", NullValueHandling = NullValueHandling.Ignore)]
            public float? Fov { get; set; }

            [JsonProperty("ortho", NullValueHandling = NullValueHandling.Ignore)]
            public float? Ortho { get; set; }
        }
    }
}
