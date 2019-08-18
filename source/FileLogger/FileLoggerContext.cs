using System;
using System.Collections.Generic;
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
        Task Completion { get; }

        IDisposable RegisterCompleteTask(Task completeTask);
    }

    public class FileLoggerContext : IFileLoggerContext
    {
        public static readonly FileLoggerContext Default = new FileLoggerContext(default);

        private readonly HashSet<Task> _completeTasks;

        public FileLoggerContext(CancellationToken completeToken)
            : this(completeToken, TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(500)) { }

        public FileLoggerContext(CancellationToken completeToken, TimeSpan completionTimeout, TimeSpan writeRetryDelay)
        {
            CompletionTimeout = completionTimeout;
            WriteRetryDelay = writeRetryDelay;
            CompleteToken = completeToken;

            _completeTasks = new HashSet<Task>();
        }

        public virtual DateTimeOffset GetTimestamp() => DateTimeOffset.UtcNow;

        public virtual TimeSpan WriteRetryDelay { get; }

        public virtual TimeSpan CompletionTimeout { get; }

        public CancellationToken CompleteToken { get; }

        public Task Completion
        {
            get
            {
                lock (_completeTasks)
                    return Task.WhenAll(_completeTasks);
            }
        }

        IDisposable IFileLoggerContext.RegisterCompleteTask(Task completeTask)
        {
            if (completeTask == null)
                throw new ArgumentNullException(nameof(completeTask));

            lock (_completeTasks)
                _completeTasks.Add(completeTask);

            return new CompleteTaskRegistration(this, completeTask);
        }

        private void UnregisterCompleteTask(Task completeTask)
        {
            lock (_completeTasks)
                _completeTasks.Remove(completeTask);
        }

        private class CompleteTaskRegistration : IDisposable
        {
            private FileLoggerContext _context;
            private Task _completeTask;

            public CompleteTaskRegistration(FileLoggerContext context, Task completeTask)
            {
                _context = context;
                _completeTask = completeTask;
            }

            public void Dispose()
            {
                if (_completeTask != null)
                {
                    _context.UnregisterCompleteTask(_completeTask);
                    _completeTask = null;
                    _context = null;
                }
            }
        }
    }
}
