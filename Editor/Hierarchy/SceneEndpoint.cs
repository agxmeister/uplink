using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Hierarchy
{
    /// <summary>
    /// `GET /scene`: what is in the open scenes, so a client can see that a change landed on the objects it
    /// meant rather than infer it from a screenshot.
    /// </summary>
    public sealed class SceneEndpoint : IEndpoint
    {
        private readonly ISceneProbe probe;

        public SceneEndpoint(ISceneProbe probe)
        {
            if (probe == null)
            {
                throw new ArgumentNullException("probe");
            }
            this.probe = probe;
        }

        public string Method
        {
            get { return "GET"; }
        }

        public string Path
        {
            get { return "/scene"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "read_scene",
                "List the objects in the Editor's open scenes.",
                "Returns the hierarchy of every loaded scene, with each object's path, whether it is " +
                "active, and the components on it.\n\n" +
                "The walk goes `depth` generations deep and stops after a couple of thousand objects, so a " +
                "large scene comes back partial with `truncated: true`. Narrow it with `path` — the same " +
                "slash-separated path each object reports — to walk one subtree instead. `read_object` " +
                "gives the component values for a single object.\n\n" +
                "Inactive objects are included; `childCount` says how many children an object has even " +
                "where the walk did not list them.",
                new Dictionary<string, object>
                {
                    { "200", Schema.JsonContent("The open scenes.", TreeSchema()) },
                    { "400", Schema.ErrorContent("A parameter was not understood.") },
                    { "504", Schema.ErrorContent("The Editor was too busy to answer. Retry.") },
                },
                new List<object>
                {
                    Schema.QueryParameter(
                        "path", "Walk this object's subtree instead of the whole hierarchy.",
                        Schema.Property("string", "A path such as /Level/Enemies."), false),
                    Schema.QueryParameter(
                        "depth", "How many generations below the starting point to include.",
                        Schema.Property("integer", "Between 0 and 20.", 3), false),
                    Schema.QueryParameter(
                        "components", "Whether to list each object's component types.",
                        Schema.Property("boolean", "Include component names.", true), false),
                },
                null);
        }

        public Response Handle(Request request)
        {
            var arguments = new Arguments(request);

            return Response.Json(200, probe.ReadTree(new SceneQuery
            {
                Path = arguments.String("path", null),
                Depth = arguments.Int("depth", 3, 0, 20),
                Components = arguments.Bool("components", true),
            }));
        }

        private static IDictionary<string, object> TreeSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                {
                    "scenes", Schema.Array("Every open scene.", Schema.Object(new Dictionary<string, object>
                    {
                        { "name", Schema.Property("string", "The scene's name.") },
                        { "path", Schema.Property("string", "Asset path, empty if never saved.") },
                        { "isLoaded", Schema.Property("boolean", "Whether its objects are in memory.") },
                        { "isActive", Schema.Property("boolean", "Whether new objects go into this scene.") },
                        { "roots", Schema.Array("Objects at the top of this scene.", NodeSchema()) },
                    }))
                },
                { "truncated", Schema.Property("boolean", "The walk stopped short; narrow it with 'path'.") },
            });
        }

        /// <summary>
        /// Described one level deep and then by reference to itself, because the shape recurses and OpenAPI
        /// has no way to say "the same again" without a named schema.
        /// </summary>
        private static IDictionary<string, object> NodeSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                { "name", Schema.Property("string", "The object's name.") },
                { "path", Schema.Property("string", "Slash-separated path, as 'read_object' takes.") },
                { "active", Schema.Property("boolean", "Whether it is active in the hierarchy.") },
                { "tag", Schema.Property("string", "Its tag.") },
                { "layer", Schema.Property("string", "Name of its layer.") },
                { "components", Schema.Array("Type names of its components.", Schema.Property("string", "A type name.")) },
                { "childCount", Schema.Property("integer", "How many children it has.") },
                {
                    "children",
                    Schema.Array("Its children, absent where the walk stopped.", Schema.Object(
                        new Dictionary<string, object>()))
                },
            });
        }
    }
}
