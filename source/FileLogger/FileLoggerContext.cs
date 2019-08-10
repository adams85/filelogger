using System;
using System.Threading;
using System.Threading.Tasks;

namespace Karambolo.Extensions.Logging.File
{
    public interface IFileLoggerContext
    {
        DateTimeOffset GetTimestamp();

        TimeSpan WriteRetryDelay { get; }
        TimeSpan CompletionTimeout { get; }

        CancellationToken CompleteToken { get; }
        event Action<IFileLoggerProcessor, Task> Complete;

        void OnComplete(IFileLoggerProcessor sender, Task completionTask);
    }

    public class FileLoggerContext : IFileLoggerContext
    {
        public static readonly FileLoggerContext Default = new FileLoggerContext(default);

        public FileLoggerContext(CancellationToken completeToken)
            : this(completeToken, TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(500)) { }

        public FileLoggerContext(CancellationToken completeToken, TimeSpan completionTimeout, TimeSpan writeRetryDelay)
        {
            CompleteToken = completeToken;
            CompletionTimeout = completionTimeout;
            WriteRetryDelay = writeRetryDelay;
        }

        public virtual DateTimeOffset GetTimestamp() => DateTimeOffset.UtcNow;

        public virtual TimeSpan WriteRetryDelay { get; }

        public virtual TimeSpan CompletionTimeout { get; }

        public CancellationToken CompleteToken { get; }

        public event Action<IFileLoggerProcessor, Task> Complete;

        void IFileLoggerContext.OnComplete(IFileLoggerProcessor sender, Task completionTask)
        {
            Complete?.Invoke(sender, completionTask);
        }
    }
}
