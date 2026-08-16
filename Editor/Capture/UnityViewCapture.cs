using Agxmeister.Uplink.Http;
using UnityEditor;
using UnityEngine;

namespace Agxmeister.Uplink.Capture
{
    /// <summary>The one place that renders the Editor's views into pixels.</summary>
    public sealed class UnityViewCapture : IViewCapture
    {
        public CaptureResult Take(CaptureRequest request)
        {
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
