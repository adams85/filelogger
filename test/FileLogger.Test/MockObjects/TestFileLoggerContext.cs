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
            : base(completeToken)
        {
            _writeRetryDelay = base.WriteRetryDelay;
            _completionTimeout = base.CompletionTimeout;
        }

        public TestFileLoggerContext(PhysicalFileProvider fileProvider, string fallbackFileName, CancellationToken completeToken = default)
#pragma warning disable CS0618 // Type or member is obsolete
            : base(fileProvider, fallbackFileName, completeToken)
#pragma warning restore CS0618 // Type or member is obsolete
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
