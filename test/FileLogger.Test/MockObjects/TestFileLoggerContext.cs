using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File.Test.MockObjects
{
    class TestFileLoggerContext : FileLoggerContext
    {
        public TestFileLoggerContext(CancellationToken completeToken = default)
            : this(new MemoryFileAppender(), completeToken) { }

        public TestFileLoggerContext(PhysicalFileProvider fileProvider, CancellationToken completeToken = default)
            : this(new PhysicalFileAppender(fileProvider), completeToken) { }

        public TestFileLoggerContext(IFileAppender fileAppender, CancellationToken completeToken = default)
            : base(fileAppender, "fallback.log", completeToken)
        {
            _writeRetryDelay = base.WriteRetryDelay;
            _completionTimeout = base.CompletionTimeout;
        }

        DateTimeOffset _timestamp;
        public override DateTimeOffset GetTimestamp() => _timestamp;
        public void SetTimestamp(DateTimeOffset value) => _timestamp = value;

        TimeSpan _writeRetryDelay;
        public override TimeSpan WriteRetryDelay => _writeRetryDelay;
        public void SetWriteRetryDelay(TimeSpan value) => _writeRetryDelay = value;

        TimeSpan _completionTimeout;
        public override TimeSpan CompletionTimeout => _completionTimeout;
        public void SetCompletionTimeout(TimeSpan value) => _completionTimeout = value;
    }
}
