namespace Agxmeister.Uplink.Capture
{
    /// <summary>
    /// A point in world space, carried across the capture seam. Deliberately not `UnityEngine.Vector3`:
    /// <see cref="IViewCapture"/> names no Unity type, which is what lets an endpoint test drive it with a
    /// stub and no Editor at all.
    /// </summary>
    public struct Point
    {
        public Point(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X;

        public float Y;

        public float Z;

        /// <summary>The three components in the order a client wrote them, ready to echo back as JSON.</summary>
        public float[] ToArray()
        {
            return new[] { X, Y, Z };
        }
    }

    /// <summary>
    /// Where to look from and at, when the picture wanted is not one any camera in the scene is taking.
    /// Either <see cref="From"/> or <see cref="Frame"/> says where the camera goes; the endpoint refuses
    /// anything else before this reaches the capture.
    /// </summary>
    public sealed class Viewpoint
    {
        /// <summary>Where the camera stands. Null when <see cref="Frame"/> is to work it out.</summary>
        public Point? From { get; set; }

        /// <summary>The point to look at. Mutually exclusive with <see cref="Dir"/>.</summary>
        public Point? At { get; set; }

        /// <summary>The direction to look along. Mutually exclusive with <see cref="At"/>.</summary>
        public Point? Dir { get; set; }

        /// <summary>An object path, as `read_scene` reports it, whose renderers are to be fitted.</summary>
        public string Frame { get; set; }

        /// <summary>Which side of a framed object to look from; one of <see cref="CaptureAxis"/>.</summary>
        public string Axis { get; set; }

        /// <summary>Vertical field of view in degrees. Null exactly when <see cref="Ortho"/> is given.</summary>
        public float? Fov { get; set; }

        /// <summary>Orthographic half-height. Null exactly when <see cref="Fov"/> is given.</summary>
        public float? Ortho { get; set; }

        public float? Near { get; set; }

        public float? Far { get; set; }
    }

    /// <summary>Which way to look at a framed object from. `front` is the side facing −Z, as Unity draws it.</summary>
    public static class CaptureAxis
    {
        public const string Front = "front";

        public const string Back = "back";

        public const string Left = "left";

        public const string Right = "right";

        public const string Top = "top";

        public const string Bottom = "bottom";

        public static readonly string[] All = { Front, Back, Left, Right, Top, Bottom };
    }
}
