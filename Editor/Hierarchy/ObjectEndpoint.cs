using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Hierarchy
{
    /// <summary>
    /// `GET /object`: one GameObject with its components' values, which is how a client checks what an edit
    /// actually did to the scene.
    /// </summary>
    public sealed class ObjectEndpoint : IEndpoint
    {
        private readonly ISceneProbe probe;

        public ObjectEndpoint(ISceneProbe probe)
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
            get { return "/object"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "read_object",
                "Read one GameObject's components and their values.",
                "Returns every component on the object and the fields the Inspector would show for it, " +
                "which is how to confirm that a value is what a change was supposed to make it.\n\n" +
                "`path` is the slash-separated path `read_scene` reports, such as `/Level/Player`. " +
                "Inactive objects can be read as well as active ones.\n\n" +
                "Values Unity serializes but JSON cannot carry — animation curves, nested structures, " +
                "arrays — are reported as their type and size rather than their contents.",
                new Dictionary<string, object>
                {
                    { "200", Schema.JsonContent("The object.", DetailSchema()) },
                    { "400", Schema.ErrorContent("No path was given.") },
                    { "404", Schema.ErrorContent("There is no object at that path.") },
                    { "504", Schema.ErrorContent("The Editor was too busy to answer. Retry.") },
                },
                new List<object>
                {
                    Schema.QueryParameter(
                        "path", "Path of the object to read, as reported by read_scene.",
                        Schema.Property("string", "A path such as /Level/Player."), true),
                },
                null);
        }

        public Response Handle(Request request)
        {
            var path = new Arguments(request).String("path", null);
            if (string.IsNullOrEmpty(path))
            {
                throw new BadRequestException("'path' is required; read_scene reports the path of every object.");
            }

            var found = probe.ReadObject(path);
            if (found == null)
            {
                // A path that names nothing is an ordinary answer to an ordinary question, not a failure of
                // the Editor, so it is shaped here rather than thrown.
                return Response.Error(404, string.Format("No object at '{0}' in the open scenes.", path));
            }

            return Response.Json(200, found);
        }

        private static IDictionary<string, object> DetailSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                { "name", Schema.Property("string", "The object's name.") },
                { "path", Schema.Property("string", "Its slash-separated path.") },
                { "scene", Schema.Property("string", "The scene it belongs to.") },
                { "active", Schema.Property("boolean", "Whether it is active in the hierarchy.") },
                { "tag", Schema.Property("string", "Its tag.") },
                { "layer", Schema.Property("string", "Name of its layer.") },
                {
                    "components", Schema.Array("Its components, in Inspector order.", Schema.Object(
                        new Dictionary<string, object>
                        {
                            { "type", Schema.Property("string", "The component's type name.") },
                            { "enabled", Schema.Property("boolean", "Whether it is switched on, where it can be.") },
                            {
                                "properties",
                                Schema.Property("object", "Its serialized fields, keyed by the name Unity gives them.")
                            },
                        }))
                },
                { "children", Schema.Array("Names of its children.", Schema.Property("string", "A child's name.")) },
            });
        }
    }
}
