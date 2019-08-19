using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File
{
    public class FileLoggerContext
    {
        public static readonly FileLoggerContext Default = new FileLoggerContext(default);

        public FileLoggerContext(CancellationToken completeToken)
            : this(completeToken, null, null) { }

        public FileLoggerContext(CancellationToken completeToken, TimeSpan? completionTimeout = null, TimeSpan? writeRetryDelay = null)
        {
            CompleteToken = completeToken;
            CompletionTimeout = completionTimeout ?? TimeSpan.FromMilliseconds(1500);
            WriteRetryDelay = writeRetryDelay ?? TimeSpan.FromMilliseconds(500);
        }

        public CancellationToken CompleteToken { get; }

        public TimeSpan CompletionTimeout { get; }

        public TimeSpan WriteRetryDelay { get; }

        public virtual DateTimeOffset GetTimestamp() => DateTimeOffset.UtcNow;

        internal IEnumerable<FileLoggerProvider> GetProviders(IServiceProvider serviceProvider)
        {
            return serviceProvider.GetRequiredService<IEnumerable<ILoggerProvider>>()
                .OfType<FileLoggerProvider>()
                .Where(provider => provider.Context == this);
        }

        public Task GetCompletion(IServiceProvider serviceProvider)
        {
            return Task.WhenAll(GetProviders(serviceProvider).Select(provider => provider.Completion));
        }
    }
}
