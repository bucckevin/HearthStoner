using System;
using System.Collections.Concurrent;
using Xunit;

namespace HearthStoner.Tests
{
    public class DiagTests
    {
        [Fact]
        public void Caught_errors_reach_the_sink_and_a_broken_sink_is_harmless()
        {
            var lines = new ConcurrentQueue<string>();
            try
            {
                Diag.SetSink(lines.Enqueue);
                Diag.Ignored(new InvalidOperationException("disk full"), "Settings");
                Diag.Ignored(null, "Settings");   // nothing to record
                Assert.Contains("[Settings] InvalidOperationException: disk full", lines);

                Diag.SetSink(_ => throw new ObjectDisposedException("log"));
                Diag.Ignored(new Exception("x"), "Test");   // must not throw

                Diag.SetSink(null);
                Diag.Ignored(new Exception("y"), "Test");   // no sink: no-op
            }
            finally { Diag.SetSink(null); }
        }
    }
}
