using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File
{
    public interface IFileLoggerContext
    {
        CancellationToken CompleteToken { get; }
        TimeSpan CompletionTimeout { get; }
        TimeSpan WriteRetryDelay { get; }

        DateTimeOffset GetTimestamp();
        Task GetCompletion(IServiceProvider serviceProvider);
    }

    public class FileLoggerContext : IFileLoggerContext
    {
        public static readonly FileLoggerContext Default = new FileLoggerContext(default);

        public FileLoggerContext(CancellationToken completeToken)
            : this(completeToken, TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(500)) { }

        public FileLoggerContext(CancellationToken completeToken, TimeSpan completionTimeout, TimeSpan writeRetryDelay)
        {
            CompletionTimeout = completionTimeout;
            WriteRetryDelay = writeRetryDelay;
            CompleteToken = completeToken;
        }

        public virtual DateTimeOffset GetTimestamp() => DateTimeOffset.UtcNow;

        public virtual TimeSpan WriteRetryDelay { get; }

        public virtual TimeSpan CompletionTimeout { get; }

        public CancellationToken CompleteToken { get; }

        public Task GetCompletion(IServiceProvider serviceProvider)
        {
            IEnumerable<FileLoggerProvider> providers = serviceProvider.GetRequiredService<IEnumerable<ILoggerProvider>>()
                .OfType<FileLoggerProvider>()
                .Where(provider => provider.Context == this);

            return Task.WhenAll(providers.Select(provider => provider.Completion));
        }
    }
}
