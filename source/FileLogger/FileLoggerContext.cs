using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File;

public class FileLoggerContext
{
    public class WithTimestampProvider : FileLoggerContext
    {
        public WithTimestampProvider(Func<DateTimeOffset> timestampProvider, CancellationToken completeToken, TimeSpan? completionTimeout = null, TimeSpan? writeRetryDelay = null)
            : base(timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider)), completeToken, completionTimeout, writeRetryDelay) { }

        public sealed override DateTimeOffset GetTimestamp() => TimestampProvider();
    }

    public static readonly FileLoggerContext Default = new(completeToken: default);

    public FileLoggerContext(CancellationToken completeToken)
        : this(completeToken, completionTimeout: null, writeRetryDelay: null) { }

    public FileLoggerContext(CancellationToken completeToken, TimeSpan? completionTimeout = null, TimeSpan? writeRetryDelay = null)
        : this(timestampProvider: null!, completeToken, completionTimeout, writeRetryDelay)
    {
        // special-case the default implementation to avoid double virtual calls
        TimestampProvider = GetType() == typeof(FileLoggerContext) ? (() => DateTimeOffset.UtcNow) : GetTimestamp;
    }

    private FileLoggerContext(Func<DateTimeOffset> timestampProvider, CancellationToken completeToken, TimeSpan? completionTimeout = null, TimeSpan? writeRetryDelay = null)
    {
        CompleteToken = completeToken;
        CompletionTimeout = completionTimeout ?? TimeSpan.FromMilliseconds(1500);
        WriteRetryDelay = writeRetryDelay ?? TimeSpan.FromMilliseconds(500);
        TimestampProvider = timestampProvider;
    }

    public Func<DateTimeOffset> TimestampProvider { get; }

    public CancellationToken CompleteToken { get; }

    public TimeSpan CompletionTimeout { get; }

    public TimeSpan WriteRetryDelay { get; }

    public event Action<IFileLoggerDiagnosticEvent>? DiagnosticEvent;

    internal Action<IFileLoggerDiagnosticEvent>? GetDiagnosticEventReporter() => DiagnosticEvent;

    public virtual DateTimeOffset GetTimestamp() => DateTimeOffset.UtcNow;

    internal IEnumerable<FileLoggerProvider> GetProviders(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<IEnumerable<ILoggerProvider>>()
            .OfType<FileLoggerProvider>()
            .Where(provider => ReferenceEquals(provider.Context, this));
    }

    public Task GetCompletion(IServiceProvider serviceProvider)
    {
        return Task.WhenAll(GetProviders(serviceProvider).Select(provider => provider.Completion));
    }
}
