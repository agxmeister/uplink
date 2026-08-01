using System;
using Agxmeister.Uplink.Persistence;
using Agxmeister.Uplink.Services;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Agxmeister.Uplink.Testing
{
    /// <summary>
    /// The one place that drives the Unity Test Framework.
    ///
    /// It is a service for two reasons. Results arrive while nobody is asking, so they are written to the
    /// session store as each test finishes; and a PlayMode run reloads the domain in the middle of itself,
    /// which unregisters every callback — so <see cref="Attach"/> registering them again on load is what
    /// keeps the second half of such a run from being silently lost.
    /// </summary>
    public sealed class UnityTestRunner : IUplinkService, ITestRunner, IErrorCallbacks
    {
        private const string StateKey = "tests";

        private readonly TestLog log;
        private readonly ISessionStore store;

        private TestRunnerApi api;

        public UnityTestRunner(TestLog log, ISessionStore store)
        {
            if (log == null)
            {
                throw new ArgumentNullException("log");
            }
            if (store == null)
            {
                throw new ArgumentNullException("store");
            }

            this.log = log;
            this.store = store;
        }

        public void Attach()
        {
            log.Restore(Stored.Read<TestRunState>(store, StateKey));

            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            // Registered on every load, not only when a run is started: a PlayMode run reloads the domain
            // partway through, and without this the tests after that point would report to nobody.
            api.RegisterCallbacks(this);
        }

        public void Detach()
        {
            if (api != null)
            {
                api.UnregisterCallbacks(this);
                UnityEngine.Object.DestroyImmediate(api);
                api = null;
            }

            Persist();
        }

        public TestRun Poll(TestRunOptions options)
        {
            var report = log.Advance(options, DateTime.UtcNow);
            Persist();

            if (report.ShouldStart)
            {
                Start(options);
            }

            return report.Run;
        }

        private void Start(TestRunOptions options)
        {
            try
            {
                api.Execute(new ExecutionSettings(new Filter
                {
                    testMode = options.Mode == TestModes.Play ? TestMode.PlayMode : TestMode.EditMode,
                    testNames = Some(options.Names),
                    categoryNames = Some(options.Categories),
                    assemblyNames = Some(options.Assemblies),
                }));
            }
            catch (Exception exception)
            {
                // Nothing will ever call back, so the cycle has to be ended here or it would report `running`
                // for the rest of the session.
                log.Failed(exception.Message, DateTime.UtcNow);
                Persist();
            }
        }

        /// <summary>An empty array filters everything out, where the intent is to filter nothing.</summary>
        private static string[] Some(string[] values)
        {
            return values == null || values.Length == 0 ? null : values;
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            // Suites report as well as tests, and counting both would double every number.
            if (result.Test.IsSuite)
            {
                return;
            }

            log.Add(new TestOutcome
            {
                Name = result.Test.FullName,
                Status = StatusOf(result.TestStatus),
                Message = Trimmed(result.Message),
                StackTrace = Trimmed(result.StackTrace),
                DurationMs = (long)(result.Duration * 1000d),
            });

            // Written per test rather than at the end: a PlayMode run's domain reload lands somewhere in the
            // middle of this sequence.
            Persist();
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            log.Completed(DateTime.UtcNow);
            Persist();
        }

        public void OnError(string message)
        {
            log.Failed(message, DateTime.UtcNow);
            Persist();
        }

        private static string StatusOf(TestStatus status)
        {
            switch (status)
            {
                case TestStatus.Passed:
                    return TestState.Passed;
                case TestStatus.Failed:
                    return TestState.Failed;
                case TestStatus.Skipped:
                    return TestState.Skipped;
                default:
                    return TestState.Inconclusive;
            }
        }

        private static string Trimmed(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private void Persist()
        {
            Stored.Write(store, StateKey, log.Capture());
        }
    }
}
