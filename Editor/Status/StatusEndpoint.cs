using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Status
{
    /// <summary>
    /// `GET /status`: tells the assistant which Editor it is talking to and what it is doing.
    /// </summary>
    public sealed class StatusEndpoint : IEndpoint
    {
        private readonly IEditorStatusProbe probe;

        public StatusEndpoint(IEditorStatusProbe probe)
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
            get { return "/status"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "status",
                "Report the state of the connected Unity Editor.",
                "Returns the Unity version, platform, project, active build target, active scene and play mode " +
                "of the Editor instance serving this API.",
                Schema.Object(new Dictionary<string, object>
                {
                    { "uplinkVersion", Schema.Property("string", "Version of the Uplink package.") },
                    { "unityVersion", Schema.Property("string", "Version of the Unity Editor.") },
                    { "platform", Schema.Property("string", "Platform the Editor runs on.") },
                    { "projectName", Schema.Property("string", "Product name of the open project.") },
                    { "projectPath", Schema.Property("string", "Absolute path of the project's Assets folder.") },
                    { "activeBuildTarget", Schema.Property("string", "Currently selected build target.") },
                    { "activeScene", Schema.Property("string", "Path of the active scene, or its name when unsaved.") },
                    { "isPlaying", Schema.Property("boolean", "Whether the Editor is in play mode.") },
                    { "isPaused", Schema.Property("boolean", "Whether play mode is paused.") },
                    { "isCompiling", Schema.Property("boolean", "Whether scripts are being compiled.") },
                    { "isUpdating", Schema.Property("boolean", "Whether the asset database is being updated.") },
                }),
                "The current Editor state.");
        }

        public Response Handle(Request request)
        {
            return Response.Json(200, probe.Read());
        }
    }
}
