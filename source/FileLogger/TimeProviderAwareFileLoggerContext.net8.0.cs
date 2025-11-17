using System;
using System.Threading;

namespace Karambolo.Extensions.Logging.File;

public sealed class TimeProviderAwareFileLoggerContext : FileLoggerContext
{
    private readonly TimeProvider _timeProvider;

    public TimeProviderAwareFileLoggerContext(TimeProvider timeProvider, CancellationToken completeToken)
        : this(timeProvider, completeToken, completionTimeout: null, writeRetryDelay: null) { }

    public TimeProviderAwareFileLoggerContext(TimeProvider timeProvider, CancellationToken completeToken, TimeSpan? completionTimeout = null, TimeSpan? writeRetryDelay = null)
        : base(completeToken, completionTimeout, writeRetryDelay)
    {
        _timeProvider = timeProvider;
    }

    public override DateTimeOffset GetTimestamp()
    {
        return _timeProvider.GetUtcNow();
    }
}
