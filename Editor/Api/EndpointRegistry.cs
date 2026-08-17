using System.Collections;
using System.Collections.Generic;

namespace Agxmeister.Uplink.Api
{
    /// <summary>
    /// The endpoint collection, safe to add to while it is being read.
    ///
    /// <see cref="Router"/> and <see cref="OpenApiEndpoint"/> both enumerate the live collection on a
    /// thread-pool thread, once per request — that is what keeps a route and its published description from
    /// drifting apart. An optional capability registers itself from the main thread after the listener is
    /// already up (ADR-0015), and appending to a `List` that another thread is walking throws. So the two
    /// happen under one lock, here, and every enumeration walks a snapshot.
    ///
    /// Guarding it in this one place is deliberate: `Router` and `OpenApiEndpoint` still take a plain
    /// <see cref="IEnumerable{T}"/> and know nothing about registration, and no call site can forget to lock
    /// because there is no unlocked path to forget.
    /// </summary>
    public sealed class EndpointRegistry : IEnumerable<IEndpoint>
    {
        private readonly object gate = new object();
        private readonly List<IEndpoint> endpoints = new List<IEndpoint>();

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return endpoints.Count;
                }
            }
        }

        public void Add(IEndpoint endpoint)
        {
            lock (gate)
            {
                endpoints.Add(endpoint);
            }
        }

        public IEnumerator<IEndpoint> GetEnumerator()
        {
            // A copy, not the list itself: the caller may take as long as it likes, and a registration may
            // arrive while it does.
            lock (gate)
            {
                return new List<IEndpoint>(endpoints).GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
