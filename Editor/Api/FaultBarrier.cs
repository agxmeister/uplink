using System;
using Agxmeister.Uplink.Diagnostics;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Api
{
    /// <summary>
    /// Converts anything thrown below it into a response, so a failing endpoint can never take the listener
    /// thread down with it and every client gets a status code it can act on.
    /// </summary>
    public sealed class FaultBarrier : IRequestHandler
    {
        private readonly IRequestHandler inner;
        private readonly IUplinkLog log;

        public FaultBarrier(IRequestHandler inner, IUplinkLog log)
        {
            if (inner == null)
            {
                throw new ArgumentNullException("inner");
            }
            if (log == null)
            {
                throw new ArgumentNullException("log");
            }

            this.inner = inner;
            this.log = log;
        }

        public Response Handle(Request request)
        {
            try
            {
                return inner.Handle(request);
            }
            catch (BadRequestException exception)
            {
                // The client asked wrongly; nothing here is broken, so this is not worth an Editor error.
                log.Warning(string.Format("{0} {1} rejected: {2}", request.Method, request.Path, exception.Message));
                return Response.Error(400, exception.Message);
            }
            catch (TimeoutException exception)
            {
                // The Editor is busy rather than broken: the client should retry, not treat this as a bug.
                log.Warning(string.Format("{0} {1} timed out: {2}", request.Method, request.Path, exception.Message));
                return Response.Error(504, exception.Message);
            }
            catch (Exception exception)
            {
                log.Error(string.Format("{0} {1} failed: {2}", request.Method, request.Path, exception));
                // The type name goes with the message: "Object reference not set" alone says nothing about
                // what failed, and this line is all a client will ever see of the stack trace logged above.
                return Response.Error(500, string.Format("{0}: {1}", exception.GetType().Name, exception.Message));
            }
        }
    }
}
