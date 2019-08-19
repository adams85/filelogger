using System;
using System.Threading;

namespace Karambolo.Extensions.Logging.File.Test.Mocks
{
    internal class TestFileLoggerContext : FileLoggerContext
    {
        public TestFileLoggerContext(CancellationToken completeToken, TimeSpan? completionTimeout = null, TimeSpan? writeRetryDelay = null)
            : base(completeToken, completionTimeout, writeRetryDelay) { }

        private DateTimeOffset _timestamp;
        public override DateTimeOffset GetTimestamp() => _timestamp;
        public void SetTimestamp(DateTimeOffset value) => _timestamp = value;
    }
}
