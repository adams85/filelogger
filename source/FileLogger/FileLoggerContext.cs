using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File
{
    public interface IFileLoggerContext
    {
        [Obsolete("This property will be removed in a future version.")]
        IFileProvider FileProvider { get; }

        [Obsolete("This property will be removed in a future version.")]
        string FallbackFileName { get; }

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

        [Obsolete("This constructor is obsolete and will be removed in a future version. Apart from completeToken, configure " +
            nameof(FileLoggerOptions) + "." + nameof(FileLoggerOptions.RootPath) + " and " + nameof(FileLoggerOptions) + "." + nameof(FileLoggerOptions.FallbackFileName) + " instead.")]
        public FileLoggerContext(string rootPath, string fallbackFileName, CancellationToken completeToken = default)
            : this(new PhysicalFileProvider(rootPath ?? throw new ArgumentNullException(nameof(rootPath))), fallbackFileName, completeToken) { }

        [Obsolete("This constructor is obsolete and will be removed in a future version. Apart from completeToken, configure " +
            nameof(FileLoggerOptions) + "." + nameof(FileLoggerOptions.FileAppender) + " and " + nameof(FileLoggerOptions) + "." + nameof(FileLoggerOptions.FallbackFileName) + " instead.")]
        public FileLoggerContext(IFileProvider fileProvider, string fallbackFileName, CancellationToken completeToken = default)
            : this(completeToken)
        {
            if (fileProvider == null)
                throw new ArgumentNullException(nameof(fileProvider));

            if (fallbackFileName == null)
                throw new ArgumentNullException(nameof(fallbackFileName));

            if (!(fileProvider is PhysicalFileProvider))
                throw new ArgumentException($"Only {nameof(PhysicalFileProvider)} is supported currently. To use another file provider type, you need to implement {nameof(IFileAppender)}.", nameof(fileProvider));

            FileProvider = fileProvider;
            FallbackFileName = fallbackFileName;
        }

        [Obsolete("This property will be removed in a future version.")]
        public IFileProvider FileProvider { get; }

        [Obsolete("This property will be removed in a future version.")]
        public string FallbackFileName { get; }

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
