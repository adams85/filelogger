#if NET8_0_OR_GREATER

using System;
using System.Threading;

namespace Karambolo.Extensions.Logging.File.Test.Mocks;

public class TestTimeProvider : TimeProvider
{
    private DateTime? _utcNow;

    public void Reset()
    {
        _utcNow = null;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow ?? DateTime.UtcNow;

    public void SetUtcNow(DateTime newUtcNow)
    {
        _utcNow = newUtcNow;
    }

    public override TimeZoneInfo LocalTimeZone => throw new NotImplementedException();

    public override long TimestampFrequency => throw new NotImplementedException();

    public override long GetTimestamp() => throw new NotImplementedException();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => throw new NotImplementedException();
}

#endif
