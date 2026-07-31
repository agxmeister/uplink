using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Agxmeister.Uplink.Diagnostics;

namespace Agxmeister.Uplink.Http
{
    /// <summary>
    /// Serves an <see cref="IRequestHandler"/> over loopback HTTP. It owns the socket and the threads and
    /// nothing else: it knows no routes, no payload shapes and no error semantics.
    /// </summary>
    public sealed class HttpListenerServer
    {
        private readonly IRequestHandler handler;
        private readonly IUplinkLog log;

        private HttpListener listener;

        public HttpListenerServer(IRequestHandler handler, IUplinkLog log)
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }
            if (log == null)
            {
                throw new ArgumentNullException("log");
            }

            this.handler = handler;
            this.log = log;
        }

        public bool IsRunning
        {
            get
            {
                var current = listener;
                return current != null && current.IsListening;
            }
        }

        /// <summary>The reason the last <see cref="Start"/> failed, or null if it succeeded.</summary>
        public string LastError { get; private set; }

        public void Start(int port)
        {
            if (IsRunning)
            {
                return;
            }

            HttpListener starting;
            try
            {
                starting = new HttpListener();
                starting.Prefixes.Add(string.Format("http://localhost:{0}/", port));
                starting.Start();
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                listener = null;
                log.Error(string.Format("Cannot listen on port {0}: {1}", port, exception.Message));
                return;
            }

            LastError = null;
            listener = starting;

            var worker = new Thread(() => Accept(starting)) { IsBackground = true, Name = "Uplink" };
            worker.Start();
            log.Info(string.Format("Serving http://localhost:{0}/", port));
        }

        public void Stop()
        {
            var stopping = listener;
            listener = null;

            if (stopping == null)
            {
                return;
            }

            try
            {
                // Closing the listener is what unblocks GetContext and ends the accept loop.
                stopping.Close();
            }
            catch (Exception exception)
            {
                log.Warning(string.Format("Error while stopping: {0}", exception.Message));
            }
        }

        private void Accept(HttpListener owned)
        {
            while (ReferenceEquals(owned, listener))
            {
                HttpListenerContext context;
                try
                {
                    context = owned.GetContext();
                }
                catch (Exception)
                {
                    // The listener was closed, most likely by a domain reload.
                    return;
                }

                // Serve off the accept loop: a request waiting on a busy Editor must not delay the next one.
                ThreadPool.QueueUserWorkItem(state => Serve((HttpListenerContext)state), context);
            }
        }

        private void Serve(HttpListenerContext context)
        {
            Response response;
            try
            {
                response = handler.Handle(Read(context.Request));
            }
            catch (Exception exception)
            {
                // The handler chain is expected to absorb its own failures; this is the backstop.
                log.Error(string.Format("Unhandled failure while serving a request: {0}", exception));
                response = Response.Error(500, exception.Message);
            }

            try
            {
                Write(context.Response, response);
            }
            catch (Exception exception)
            {
                // Almost always a client that hung up mid-response; nothing left to report to it.
                log.Warning(string.Format("Could not write a response: {0}", exception.Message));
                Abort(context);
            }
        }

        private static Request Read(HttpListenerRequest request)
        {
            var query = new Dictionary<string, string>();
            foreach (var key in request.QueryString.AllKeys)
            {
                if (key != null)
                {
                    query[key] = request.QueryString[key];
                }
            }

            var body = string.Empty;
            if (request.HasEntityBody)
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
                {
                    body = reader.ReadToEnd();
                }
            }

            return new Request(request.HttpMethod, request.Url, query, body);
        }

        private static void Write(HttpListenerResponse target, Response response)
        {
            foreach (var header in response.Headers)
            {
                target.AddHeader(header.Key, header.Value);
            }

            target.StatusCode = response.Status;
            target.ContentType = response.ContentType;
            target.ContentLength64 = response.Body.Length;
            target.OutputStream.Write(response.Body, 0, response.Body.Length);
            target.Close();
        }

        private static void Abort(HttpListenerContext context)
        {
            try
            {
                context.Response.Abort();
            }
            catch (Exception)
            {
                // Already gone.
            }
        }
    }
}
