using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Refresh
{
    /// <summary>
    /// `POST /refresh`: makes the Editor pick up files written from outside it, which is what anything that
    /// edits a project as text needs before what it wrote can be seen.
    /// </summary>
    public sealed class RefreshEndpoint : IEndpoint
    {
        private readonly IRefresher refresher;

        public RefreshEndpoint(IRefresher refresher)
        {
            if (refresher == null)
            {
                throw new ArgumentNullException("refresher");
            }
            this.refresher = refresher;
        }

        public string Method
        {
            get { return "POST"; }
        }

        public string Path
        {
            get { return "/refresh"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "refresh",
                "Make the Editor re-read files that changed on disk.",
                "Call this after writing project files from outside the Editor. An Editor that is not the " +
                "focused window defers importing until someone clicks on it, so until this is called a file " +
                "that was written may as well not exist.\n\n" +
                "Importing alone does not update a scene the Editor already has open. Unity keeps its " +
                "in-memory copy of an open scene and never re-reads the file, so an edit written into a " +
                "`.unity` stays invisible however many times the project is imported or compiled. `scenes` " +
                "re-opens the open scenes from their files as well, which is what makes such an edit appear; " +
                "leave it on unless only assets were written.\n\n" +
                "Re-opening throws away whatever the Editor was holding unsaved, along with the selection " +
                "and the undo history. A scene with unsaved changes is therefore answered `409` and left " +
                "alone until `discardUnsavedChanges` says otherwise.\n\n" +
                "Importing changed scripts reloads the domain, so — as with `compile` — the call that starts " +
                "a refresh cannot be the call that reports it. The first call answers `202` with " +
                "`state: \"refreshing\"`, and further calls answer the same until one returns `state: " +
                "\"done\"` with the scenes as they now stand. `rootCount` is how to tell that a scene picked " +
                "up what was written. That result is handed over once; the call after it starts a fresh " +
                "refresh.\n\n" +
                "Play mode is refused outright — neither importing nor re-opening is safe while the game is " +
                "running.",
                new Dictionary<string, object>
                {
                    { "200", Schema.JsonContent("The Editor has re-read the project.", ResultSchema()) },
                    { "202", Schema.JsonContent("Under way. Call again to find out how it went.", ResultSchema()) },
                    { "400", Schema.ErrorContent("Play mode is running, where this cannot be done.") },
                    { "409", Schema.ErrorContent("An open scene has unsaved changes a reload would discard.") },
                    { "504", Schema.ErrorContent("The Editor was too busy to answer. Retry.") },
                },
                null,
                Schema.JsonBody("What to re-read.", Schema.Object(new Dictionary<string, object>
                {
                    {
                        "scenes",
                        Schema.Property(
                            "boolean",
                            "Whether to re-open the open scenes from their files, which is the only way an " +
                            "edit written into a scene file becomes visible.",
                            true)
                    },
                    {
                        "discardUnsavedChanges",
                        Schema.Property(
                            "boolean",
                            "Whether re-opening may throw away unsaved edits the Editor is holding.",
                            false)
                    },
                }), false));
        }

        public Response Handle(Request request)
        {
            var wanted = new Arguments(request).Body<Wanted>();
            var scenes = !wanted.Scenes.HasValue || wanted.Scenes.Value;
            var discard = wanted.DiscardUnsavedChanges.HasValue && wanted.DiscardUnsavedChanges.Value;

            if (refresher.IsPlaying)
            {
                return Response.Error(400, "Cannot refresh while play mode is running. Stop it first.");
            }

            if (scenes && !discard)
            {
                var dirty = Dirty();
                if (dirty.Count > 0)
                {
                    return Response.Error(409, string.Format(
                        "Unsaved changes in {0}. Re-opening would discard them: save in the Editor, call " +
                        "again with discardUnsavedChanges: true, or with scenes: false to import only.",
                        string.Join(", ", dirty.ToArray())));
                }
            }

            var result = refresher.Poll(scenes);

            return Response.Json(result.State == RefreshLog.Done ? 200 : 202, result);
        }

        private List<string> Dirty()
        {
            var dirty = new List<string>();
            foreach (var scene in refresher.OpenScenes())
            {
                if (scene.IsDirty)
                {
                    dirty.Add(scene.Path);
                }
            }
            return dirty;
        }

        private static IDictionary<string, object> ResultSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                {
                    "state",
                    Schema.Choice(
                        "Whether the Editor has finished re-reading.",
                        new[] { RefreshLog.Refreshing, RefreshLog.Done },
                        null)
                },
                {
                    "reloaded",
                    Schema.Property("boolean", "Whether the scenes were re-opened, as opposed to only imported.")
                },
                {
                    "scenes",
                    Schema.Array("The open scenes as they now stand.", Schema.Object(
                        new Dictionary<string, object>
                        {
                            { "name", Schema.Property("string", "The scene's name.") },
                            { "path", Schema.Property("string", "The scene's asset path.") },
                            { "isDirty", Schema.Property("boolean", "Whether it holds unsaved edits.") },
                            {
                                "rootCount",
                                Schema.Property(
                                    "integer",
                                    "How many root objects it has, which is how a reload that picked up " +
                                    "new objects shows.")
                            },
                        }))
                },
                { "durationMs", Schema.Property("integer", "How long the finished refresh took.") },
            });
        }

        /// <summary>The request body.</summary>
        private sealed class Wanted
        {
            [JsonProperty("scenes")]
            public bool? Scenes { get; set; }

            [JsonProperty("discardUnsavedChanges")]
            public bool? DiscardUnsavedChanges { get; set; }
        }
    }
}
