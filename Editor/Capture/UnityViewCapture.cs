using Agxmeister.Uplink.Hierarchy;
using Agxmeister.Uplink.Http;
using UnityEditor;
using UnityEngine;

namespace Agxmeister.Uplink.Capture
{
    /// <summary>The one place that renders the Editor's views into pixels.</summary>
    public sealed class UnityViewCapture : IViewCapture
    {
        /// <summary>How much room to leave around a framed object, so it does not touch the image's edges.</summary>
        private const float Margin = 1.15f;

        /// <summary>The clip range for a `from` viewpoint, which says nothing about how big the scene is.</summary>
        private const float DefaultNear = 0.03f;

        private const float DefaultFar = 5000f;

        public CaptureResult Take(CaptureRequest request)
        {
            // First, and with no fallback in either direction: a viewpoint is as explicit a request as a
            // named camera, so a bad one fails rather than photographing somewhere else.
            if (request.View == CaptureView.Viewpoint)
            {
                return FromViewpoint(request);
            }

            if (request.View == CaptureView.Game && Application.isPlaying)
            {
                // Only worth trying while the game is running: outside play mode the Game view is not
                // drawing, and the grab comes back empty or stale.
                var grabbed = FromGameView(request.Crop);
                if (grabbed != null)
                {
                    return grabbed;
                }
            }

            // A named camera is an explicit request. If it is not there, say so rather than quietly
            // photographing something else and letting the answer look like agreement.
            if (!string.IsNullOrEmpty(request.Camera))
            {
                var named = NamedCamera(request.Camera);
                if (named == null)
                {
                    throw new BadRequestException(string.Format(
                        "No camera named '{0}' among the enabled cameras in the open scenes.", request.Camera));
                }
                return Rendered(CaptureView.Camera, named, request);
            }

            // Otherwise render whichever view can actually draw, preferring the one asked for. Either may be
            // missing — a scene need not contain a camera, and a Scene view need not be open — so each falls
            // back to the other and the result says which it turned out to be.
            var sceneView = SceneViewCamera();
            var main = MainCamera();

            if (request.View == CaptureView.Scene && sceneView != null)
            {
                return Rendered(CaptureView.Scene, sceneView, request);
            }
            if (main != null)
            {
                return Rendered(CaptureView.Camera, main, request);
            }
            if (sceneView != null)
            {
                return Rendered(CaptureView.Scene, sceneView, request);
            }

            throw new BadRequestException(
                "Nothing to render: the open scenes have no enabled camera, and no Scene view is open. " +
                "Add a camera to the scene, enable the one that is there, or open the Scene window.");
        }

        /// <summary>
        /// Renders through a camera of Uplink's own, built for this one shot and destroyed before the answer
        /// is written. `HideAndDontSave` is what keeps this inside ADR-0007: the object is not in the scene
        /// the user sees, is not saved with it, and does not dirty it.
        /// </summary>
        private static CaptureResult FromViewpoint(CaptureRequest request)
        {
            var viewpoint = request.Viewpoint;
            var aspect = (float)request.Width / request.Height;

            Vector3 position;
            Quaternion rotation;
            Vector3 focus;
            float near;
            float far;

            if (viewpoint.Frame != null)
            {
                float distance, size;
                Fit(viewpoint, aspect, out position, out rotation, out focus, out distance, out size);

                // Derived from the fit, because a fixed pair cannot contain a framed object two hundred
                // units wide and still resolve depth on one a centimetre across.
                near = Mathf.Max(0.01f, 0.01f * distance);
                far = distance + Mathf.Max(size * 4f, distance);
            }
            else
            {
                position = Where(viewpoint.From.Value);
                focus = viewpoint.At.HasValue
                    ? Where(viewpoint.At.Value)
                    : position + Direction(viewpoint);

                var forward = focus - position;
                if (forward.sqrMagnitude < 1e-8f)
                {
                    throw new BadRequestException(
                        "'at' is the same point as 'from', so there is no direction to look in.");
                }
                rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

                // Nothing here says how big the scene is — `at` may be a metre away or the far wall — so the
                // range is deliberately generous, and `near`/`far` are there for when it is not enough.
                near = DefaultNear;
                far = DefaultFar;
            }

            var subject = new GameObject("Uplink Viewpoint");
            try
            {
                subject.hideFlags = HideFlags.HideAndDontSave;
                subject.transform.SetPositionAndRotation(position, rotation);

                var camera = subject.AddComponent<Camera>();
                // Never joins the render loop: this camera draws once, when we ask it to.
                camera.enabled = false;
                camera.aspect = aspect;
                Configure(camera);

                camera.orthographic = viewpoint.Ortho.HasValue;
                if (camera.orthographic)
                {
                    camera.orthographicSize = viewpoint.Ortho.Value;
                }
                else
                {
                    camera.fieldOfView = viewpoint.Fov.Value;
                }

                camera.nearClipPlane = viewpoint.Near ?? near;
                camera.farClipPlane = viewpoint.Far ?? far;

                if (camera.farClipPlane <= camera.nearClipPlane)
                {
                    throw new BadRequestException(string.Format(
                        "'far' ({0}) must be beyond 'near' ({1}).", camera.farClipPlane, camera.nearClipPlane));
                }

                return new CaptureResult
                {
                    View = CaptureView.Viewpoint,
                    Width = request.Crop == null ? request.Width : request.Crop.Width,
                    Height = request.Crop == null ? request.Height : request.Crop.Height,
                    Png = Render(camera, request.Width, request.Height, request.Crop),
                    From = Pose(position),
                    At = Pose(focus),
                    Fov = camera.orthographic ? (float?)null : camera.fieldOfView,
                    Ortho = camera.orthographic ? camera.orthographicSize : (float?)null,
                };
            }
            finally
            {
                Object.DestroyImmediate(subject);
            }
        }

        /// <summary>
        /// Where to stand to see all of a subtree's renderers. The distance fits both axes — a wide object in
        /// a tall image is limited by width, not height — with room left over.
        /// </summary>
        private static void Fit(
            Viewpoint viewpoint, float aspect,
            out Vector3 position, out Quaternion rotation, out Vector3 focus, out float distance, out float size)
        {
            var subject = ObjectPath.Find(viewpoint.Frame);
            if (subject == null)
            {
                throw new BadRequestException(string.Format(
                    "No object at '{0}' in the open scenes. Paths are the ones read_scene reports, from a " +
                    "scene root down, such as '/MenuScreen/MenuSlider/MenuHall'.", viewpoint.Frame));
            }

            Bounds bounds;
            if (!Measured(subject, out bounds))
            {
                throw new BadRequestException(string.Format(
                    "'{0}' has no enabled renderers, so there is nothing to frame. The object or one of its " +
                    "ancestors is probably inactive — in edit mode a subtree authored inactive renders " +
                    "nothing. Enter play mode, or frame something that is on screen.", viewpoint.Frame));
            }

            Vector3 forward, up;
            Along(viewpoint.Axis, out forward, out up);
            rotation = Quaternion.LookRotation(forward, up);

            var right = rotation * Vector3.right;
            var halfWidth = Projected(bounds.extents, right);
            var halfHeight = Projected(bounds.extents, rotation * Vector3.up);
            var halfDepth = Projected(bounds.extents, forward);

            if (viewpoint.Fov.HasValue)
            {
                var tangent = Mathf.Tan(viewpoint.Fov.Value * 0.5f * Mathf.Deg2Rad);
                distance = Mathf.Max(halfHeight / tangent, halfWidth / (tangent * aspect)) * Margin + halfDepth;
                size = Mathf.Max(halfHeight, halfWidth / aspect);
            }
            else
            {
                size = Mathf.Max(halfHeight, halfWidth / aspect) * Margin;
                distance = halfDepth + Mathf.Max(bounds.size.magnitude, 1f);
            }

            focus = bounds.center;
            position = focus - forward * distance;
        }

        /// <summary>
        /// The union of what the subtree actually draws. Renderers that are switched off, or hang under an
        /// inactive object, are left out — they are exactly what the caller cannot see either.
        /// </summary>
        private static bool Measured(GameObject subject, out Bounds bounds)
        {
            bounds = new Bounds();
            var any = false;

            foreach (var renderer in subject.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (any)
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                else
                {
                    bounds = renderer.bounds;
                    any = true;
                }
            }

            return any;
        }

        /// <summary>Half a box's width along an arbitrary direction.</summary>
        private static float Projected(Vector3 extents, Vector3 direction)
        {
            return Mathf.Abs(extents.x * direction.x)
                + Mathf.Abs(extents.y * direction.y)
                + Mathf.Abs(extents.z * direction.z);
        }

        /// <summary>Which way the camera faces for each named side, and which way is up while it does.</summary>
        private static void Along(string axis, out Vector3 forward, out Vector3 up)
        {
            up = Vector3.up;
            switch (axis)
            {
                case CaptureAxis.Back:
                    forward = Vector3.back;
                    return;
                case CaptureAxis.Left:
                    forward = Vector3.right;
                    return;
                case CaptureAxis.Right:
                    forward = Vector3.left;
                    return;
                case CaptureAxis.Top:
                    forward = Vector3.down;
                    up = Vector3.forward;
                    return;
                case CaptureAxis.Bottom:
                    forward = Vector3.up;
                    up = Vector3.back;
                    return;
                default:
                    forward = Vector3.forward;
                    return;
            }
        }

        /// <summary>The two sides of the capture seam, which names no Unity type, meeting.</summary>
        private static Vector3 Where(Point point)
        {
            return new Vector3(point.X, point.Y, point.Z);
        }

        private static Point Pose(Vector3 vector)
        {
            return new Point(vector.x, vector.y, vector.z);
        }

        private static Vector3 Direction(Viewpoint viewpoint)
        {
            var dir = Where(viewpoint.Dir.Value);
            if (dir.sqrMagnitude < 1e-8f)
            {
                throw new BadRequestException("'dir' has no length, so there is no direction to look in.");
            }
            return dir.normalized;
        }

        /// <summary>
        /// Makes the shot resemble what the game would draw, by borrowing the main camera's clear flags,
        /// background and culling mask. With no main camera there is nothing to resemble, so the honest
        /// default is solid black with every layer on.
        /// </summary>
        private static void Configure(Camera camera)
        {
            var main = Camera.main;
            if (main != null)
            {
                camera.clearFlags = main.clearFlags;
                camera.backgroundColor = main.backgroundColor;
                camera.cullingMask = main.cullingMask;
                return;
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = ~0;
        }

        private static CaptureResult Rendered(string view, Camera camera, CaptureRequest request)
        {
            return new CaptureResult
            {
                View = view,
                Width = request.Crop == null ? request.Width : request.Crop.Width,
                Height = request.Crop == null ? request.Height : request.Crop.Height,
                Png = Render(camera, request.Width, request.Height, request.Crop),
            };
        }

        private static Camera SceneViewCamera()
        {
            var view = SceneView.lastActiveSceneView;
            return view == null ? null : view.camera;
        }

        /// <summary>
        /// The scene's main camera, or any enabled one if nothing carries the tag. Both this and
        /// <see cref="Camera.allCameras"/> see only enabled cameras on active objects, which is why a
        /// disabled camera reads here as no camera at all.
        /// </summary>
        private static Camera MainCamera()
        {
            var main = Camera.main;
            if (main != null)
            {
                return main;
            }

            var cameras = Camera.allCameras;
            return cameras.Length == 0 ? null : cameras[0];
        }

        private static Camera NamedCamera(string name)
        {
            foreach (var camera in Camera.allCameras)
            {
                if (camera.name == name)
                {
                    return camera;
                }
            }
            return null;
        }

        /// <summary>
        /// Renders through a texture of our own rather than reading the screen, so the size is what was asked
        /// for and no window has to be open, focused or even drawing.
        /// </summary>
        private static byte[] Render(Camera camera, int width, int height, CaptureRect crop)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            Texture2D image = null;

            try
            {
                camera.targetTexture = target;
                camera.Render();

                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply();

                return Encoded(image, crop);
            }
            finally
            {
                // The camera is the scene's, not ours: leaving it pointed at a texture we are about to
                // destroy would black out the Editor's own view of it.
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;

                if (image != null)
                {
                    Object.DestroyImmediate(image);
                }
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static CaptureResult FromGameView(CaptureRect crop)
        {
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            if (image == null)
            {
                return null;
            }

            try
            {
                return new CaptureResult
                {
                    View = CaptureView.Game,
                    Width = crop == null ? image.width : crop.Width,
                    Height = crop == null ? image.height : crop.Height,
                    Png = Encoded(image, crop),
                };
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        /// <summary>
        /// The image as PNG, cut down to <paramref name="crop"/> when one was given. The crop counts pixels
        /// from the top-left corner the way image tools do; texture rows start at the bottom, hence the
        /// flipped Y.
        /// </summary>
        private static byte[] Encoded(Texture2D image, CaptureRect crop)
        {
            if (crop == null)
            {
                return image.EncodeToPNG();
            }

            if (crop.X + crop.Width > image.width || crop.Y + crop.Height > image.height)
            {
                throw new BadRequestException(string.Format(
                    "The crop at {0},{1} sized {2}x{3} does not fit inside the {4}x{5} image.",
                    crop.X, crop.Y, crop.Width, crop.Height, image.width, image.height));
            }

            var cut = new Texture2D(crop.Width, crop.Height, TextureFormat.RGB24, false);
            try
            {
                cut.SetPixels(image.GetPixels(crop.X, image.height - crop.Y - crop.Height, crop.Width, crop.Height));
                cut.Apply();
                return cut.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(cut);
            }
        }
    }
}
