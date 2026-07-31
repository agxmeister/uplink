namespace Agxmeister.Uplink.Http
{
    /// <summary>
    /// Turns a request into a response. The HTTP server knows nothing beyond this, which is what lets the
    /// router, the fault barrier and any future middleware be composed without touching the server.
    /// </summary>
    public interface IRequestHandler
    {
        Response Handle(Request request);
    }
}
