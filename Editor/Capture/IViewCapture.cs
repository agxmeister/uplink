namespace Agxmeister.Uplink.Capture
{
    /// <summary>
    /// Renders what the Editor is showing into an image. Keeps cameras and render textures out of the
    /// endpoint, which then only has to decide how to hand the bytes over.
    /// </summary>
    public interface IViewCapture
    {
        /// <summary>Must be called on the Editor main thread.</summary>
        CaptureResult Take(CaptureRequest request);
    }

    public sealed class CaptureRequest
    {
        public CaptureRequest()
        {
            View = CaptureView.Camera;
            Width = 1280;
            Height = 720;
        }

        /// <summary>Which of <see cref="CaptureView"/> to render.</summary>
        public string View { get; set; }

        /// <summary>Name of the camera to render, or null for the scene's main camera.</summary>
        public string Camera { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        /// <summary>Region of the rendered image to keep, or null for the whole image.</summary>
        public CaptureRect Crop { get; set; }

        /// <summary>
        /// Where to put a camera of Uplink's own, when <see cref="View"/> is
        /// <see cref="CaptureView.Viewpoint"/>. Null for every other view.
        /// </summary>
        public Viewpoint Viewpoint { get; set; }
    }

    /// <summary>
    /// A region of a rendered image, in pixels from its top-left corner — the way image tools count, not the
    /// way textures do. Rendering large and cropping small is how a detail gets inspected.
    /// </summary>
    public sealed class CaptureRect
    {
        public int X { get; set; }

        public int Y { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }
    }

    /// <summary>An image, and an honest account of where it came from.</summary>
    public sealed class CaptureResult
    {
        /// <summary>
        /// The view actually rendered, which is not always the one asked for: the Game view cannot be
        /// grabbed outside play mode, and a project may have no Scene view open.
        /// </summary>
        public string View { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public byte[] Png { get; set; }

        /// <summary>
        /// The pose actually used, for a viewpoint render. Null on every other view — with `frame` the
        /// caller did not choose these numbers and needs to know what it got, and a non-nullable field would
        /// otherwise report a pose in answers that never had one.
        /// </summary>
        public Point? From { get; set; }

        public Point? At { get; set; }

        public float? Fov { get; set; }

        public float? Ortho { get; set; }
    }

    public static class CaptureView
    {
        /// <summary>A camera in the scene, rendered on demand. Works whether or not the game is running.</summary>
        public const string Camera = "camera";

        /// <summary>The Game view as a person would see it, including anything drawn over the camera.</summary>
        public const string Game = "game";

        /// <summary>The Scene view, from wherever the editing camera happens to be.</summary>
        public const string Scene = "scene";

        /// <summary>
        /// A camera of Uplink's own, put where the caller asked. Reaches what no camera in the scene is
        /// pointed at, and — unlike every other view — never falls back to one that is.
        /// </summary>
        public const string Viewpoint = "viewpoint";

        public static readonly string[] All = { Camera, Game, Scene, Viewpoint };
    }
}
