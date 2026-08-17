using System.Collections.Generic;
using System.Threading;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    /// <summary>
    /// The registry exists for one reason: an optional capability registers itself on the main thread while
    /// the listener may already be answering requests on thread-pool threads (ADR-0015). So the thing worth
    /// asserting is the thing that would otherwise throw.
    /// </summary>
    [TestFixture]
    public sealed class EndpointRegistryTests
    {
        private sealed class Nothing : IEndpoint
        {
            private readonly string path;

            public Nothing(string path)
            {
                this.path = path;
            }

            public string Method
            {
                get { return "GET"; }
            }

            public string Path
            {
                get { return path; }
            }

            public IDictionary<string, object> Describe()
            {
                return new Dictionary<string, object>();
            }

            public Response Handle(Request request)
            {
                return Response.Json(200, new Dictionary<string, object>());
            }
        }

        [Test]
        public void KeepsWhatItIsGivenInOrder()
        {
            var registry = new EndpointRegistry();
            registry.Add(new Nothing("/first"));
            registry.Add(new Nothing("/second"));

            var paths = new List<string>();
            foreach (var endpoint in registry)
            {
                paths.Add(endpoint.Path);
            }

            CollectionAssert.AreEqual(new[] { "/first", "/second" }, paths);
            Assert.AreEqual(2, registry.Count);
        }

        [Test]
        public void SurvivesARegistrationArrivingWhileItIsBeingRead()
        {
            var registry = new EndpointRegistry();
            for (var i = 0; i < 20; i++)
            {
                registry.Add(new Nothing("/seeded" + i));
            }

            var failure = (System.Exception)null;

            // The reader stands in for a request thread walking the collection to route or to describe. A
            // fixed number of passes rather than a flag, so the test cannot hang on a stale read of one.
            var reader = new Thread(delegate()
            {
                try
                {
                    for (var pass = 0; pass < 500; pass++)
                    {
                        foreach (var endpoint in registry)
                        {
                            if (endpoint.Path == null)
                            {
                                throw new System.InvalidOperationException("an endpoint went missing mid-walk");
                            }
                        }
                    }
                }
                catch (System.Exception exception)
                {
                    failure = exception;
                }
            });

            reader.Start();
            for (var i = 0; i < 80; i++)
            {
                registry.Add(new Nothing("/late" + i));
            }
            Assert.IsTrue(reader.Join(30000), "the reader should not be blocked by registrations");

            // Against a plain List this is an InvalidOperationException, intermittently, on an unrelated
            // request — which FaultBarrier would report as a 500 and nobody would be able to reproduce.
            Assert.IsNull(failure, failure == null ? null : failure.ToString());
            Assert.AreEqual(100, registry.Count);
        }

        [Test]
        public void GivesAReaderASnapshotItCanFinishWalking()
        {
            var registry = new EndpointRegistry();
            registry.Add(new Nothing("/one"));

            var walking = registry.GetEnumerator();
            registry.Add(new Nothing("/two"));

            var seen = 0;
            while (walking.MoveNext())
            {
                seen++;
            }

            Assert.AreEqual(1, seen, "a reader sees the collection as it was when it started, not a mixture");
            Assert.AreEqual(2, registry.Count);
        }
    }
}
