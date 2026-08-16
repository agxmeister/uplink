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
                "A full dump runs to dozens of properties per component, so narrow the answer when the " +
                "question is narrow: `fields=m_Mesh,m_LocalPosition` returns only those properties, " +
                "wherever they appear, and `components=MeshFilter,MeshRenderer` returns only those " +
                "components. Names match case-insensitively; when `fields` is given, components with no " +
                "matching property are left out unless `components` names them.\n\n" +
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
                    Schema.QueryParameter(
                        "fields",
                        "Return only these serialized properties, matched by name across every component " +
                        "— the way to ask one question instead of reading a whole dump.",
                        Schema.Property("string", "Comma-separated names, such as m_Mesh,m_LocalPosition."),
                        false),
                    Schema.QueryParameter(
                        "components",
                        "Return only components of these types.",
                        Schema.Property("string", "Comma-separated type names, such as MeshFilter,MeshRenderer."),
                        false),
                },
                null);
        }

        public Response Handle(Request request)
        {
            var arguments = new Arguments(request);
            var path = arguments.String("path", null);
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

            return Response.Json(200, Narrowed(
                found,
                Names(arguments.String("components", null)),
                Names(arguments.String("fields", null))));
        }

        private static string[] Names(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            var names = new List<string>();
            foreach (var name in raw.Split(','))
            {
                var trimmed = name.Trim();
                if (trimmed.Length > 0)
                {
                    names.Add(trimmed);
                }
            }
            return names.Count == 0 ? null : names.ToArray();
        }

        /// <summary>
        /// The object cut down to what was asked about. A filter that matches nothing is an ordinary answer —
        /// "that component is not there" — not a bad request, so nothing here throws.
        /// </summary>
        private static ObjectDetail Narrowed(ObjectDetail detail, string[] components, string[] fields)
        {
            if (components == null && fields == null)
            {
                return detail;
            }

            var kept = new List<ComponentDetail>();
            foreach (var component in detail.Components)
            {
                var named = components != null && Matches(component.Type, components);
                if (components != null && !named)
                {
                    continue;
                }

                if (fields == null)
                {
                    kept.Add(component);
                    continue;
                }

                var properties = new Dictionary<string, object>();
                foreach (var property in component.Properties)
                {
                    if (Matches(property.Key, fields))
                    {
                        properties[property.Key] = property.Value;
                    }
                }

                // A component nobody named and nothing matched in is noise; one asked for by name answers
                // "none of those fields are here" by coming back empty.
                if (properties.Count > 0 || named)
                {
                    kept.Add(new ComponentDetail
                    {
                        Type = component.Type,
                        Enabled = component.Enabled,
                        Properties = properties,
                    });
                }
            }

            detail.Components = kept;
            return detail;
        }

        private static bool Matches(string name, string[] wanted)
        {
            foreach (var candidate in wanted)
            {
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
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
