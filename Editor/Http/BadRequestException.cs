using System;

namespace Agxmeister.Uplink.Http
{
    /// <summary>
    /// The client asked for something the endpoint cannot make sense of. Thrown rather than shaped into a
    /// response where it is detected, so that every failure still leaves the API through one place — see
    /// <see cref="Agxmeister.Uplink.Api.FaultBarrier"/>, which turns this into a `400`.
    /// </summary>
    public sealed class BadRequestException : Exception
    {
        public BadRequestException(string message) : base(message)
        {
        }
    }
}
