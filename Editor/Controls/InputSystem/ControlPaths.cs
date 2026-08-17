namespace Agxmeister.Uplink.Controls
{
    /// <summary>
    /// Turns what a client wrote into an Input System control path. Nothing is invented here: the paths are
    /// the Input System's own, which is the language the project's `.inputactions` already speaks, and the
    /// short forms are sugar over them rather than a scheme of their own.
    ///
    /// The keyboard needs no table. Input System key controls are named exactly as a client would write them
    /// — `space`, `leftArrow`, `a`, `enter` — so prefixing is enough, and whether the result exists is a
    /// question for <c>InputSystem.FindControl</c> rather than for a list here that would go stale.
    /// </summary>
    public static class ControlPaths
    {
        public const string Keyboard = "<Keyboard>/";
        public const string Mouse = "<Mouse>/";

        /// <summary>The two forms the description promises to accept, for an error message to quote.</summary>
        public const string Accepted =
            "either an Input System path such as '<Keyboard>/space', or a bare control name such as " +
            "'space', 'leftArrow' or 'a'";

        public static string OfKey(string wanted)
        {
            return IsPath(wanted) ? wanted : Keyboard + wanted;
        }

        public static string OfClick(string wanted)
        {
            if (IsPath(wanted))
            {
                return wanted;
            }

            switch (wanted.ToLowerInvariant())
            {
                case "left":
                    return Mouse + "leftButton";
                case "right":
                    return Mouse + "rightButton";
                case "middle":
                    return Mouse + "middleButton";
                default:
                    // Not a short form we know, but it may still be a real control — `forwardButton`, say.
                    // Letting it through means the Input System gets to answer, which it does better.
                    return Mouse + wanted;
            }
        }

        /// <summary>A path names its device up front, which is what tells one from a bare control name.</summary>
        private static bool IsPath(string wanted)
        {
            return wanted.StartsWith("<");
        }
    }
}
