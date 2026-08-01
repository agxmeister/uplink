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
                var grabbed = FromGameView();
                if (grabbed != null)
                {
                    return grabbed;
                }
            }

            // A Game view asked for outside play mode, or a Scene view with no Scene window open, falls back
            // to rendering a camera rather than to nothing — and says so.
            var camera = request.View == CaptureView.Scene ? SceneCamera() : null;
            var view = camera != null ? CaptureView.Scene : CaptureView.Camera;

            if (camera == null)
            {
                camera = SceneObjectCamera(request.Camera);
            }
            if (camera == null)
            {
                throw new BadRequestException(request.Camera == null
                    ? "This scene has no camera to render, and no Scene view is open."
                    : string.Format("No camera named '{0}' in the open scenes.", request.Camera));
            }

            return new CaptureResult
            {
                View = view,
                Width = request.Width,
                Height = request.Height,
                Png = Render(camera, request.Width, request.Height),
            };
        }

        private static Camera SceneCamera()
        {
            var view = SceneView.lastActiveSceneView;
            return view == null ? null : view.camera;
        }

        private static Camera SceneObjectCamera(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                var main = Camera.main;
                return main != null ? main : FirstCamera();
            }

            foreach (var camera in Camera.allCameras)
            {
                if (camera.name == name)
                {
                    return camera;
                }
            }
            return null;
        }

        private static Camera FirstCamera()
        {
            var cameras = Camera.allCameras;
            return cameras.Length == 0 ? null : cameras[0];
        }

        /// <summary>
        /// Renders through a texture of our own rather than reading the screen, so the size is what was asked
        /// for and no window has to be open, focused or even drawing.
        /// </summary>
        private static byte[] Render(Camera camera, int width, int height)
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

                return image.EncodeToPNG();
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

        private static CaptureResult FromGameView()
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
                    Width = image.width,
                    Height = image.height,
                    Png = image.EncodeToPNG(),
                };
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }
    }
}
