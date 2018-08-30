using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File
{
    public interface IFileLoggerContext
    {
        IFileProvider FileProvider { get; }

        string FallbackFileName { get; }

        DateTimeOffset GetTimestamp();

        CancellationToken CompleteToken { get; }
        event Action<IFileLoggerProcessor, Task> Complete;

        void OnComplete(IFileLoggerProcessor sender, Task completionTask);

        Task<bool> EnsureDirAsync(IFileInfo fileInfo);
        Task AppendAllTextAsync(IFileInfo fileInfo, string text, Encoding encoding);
    }

    public class FileLoggerContext : IFileLoggerContext
    {
        readonly IFileAppender _fileAppender;

        public FileLoggerContext(string rootPath, string fallbackFileName, CancellationToken completeToken = default)
            : this(new PhysicalFileAppender(rootPath ?? throw new ArgumentNullException(nameof(rootPath))), fallbackFileName, completeToken) { }

        public FileLoggerContext(IFileProvider fileProvider, string fallbackFileName, CancellationToken completeToken = default)
            : this(fileProvider == null ? throw new ArgumentNullException(nameof(fileProvider)) : new PhysicalFileAppender(fileProvider as PhysicalFileProvider ??
                      throw new ArgumentException($"Only {nameof(PhysicalFileProvider)} is supported currently. To use another file provider type, you need to implement {nameof(IFileAppender)}.", nameof(fileProvider))),
                  fallbackFileName, completeToken) { }

        protected FileLoggerContext(IFileAppender fileAppender, string fallbackFileName, CancellationToken completeToken = default)
        {
            if (fallbackFileName == null)
                throw new ArgumentNullException(nameof(fallbackFileName));

            _fileAppender = fileAppender;
            FallbackFileName = fallbackFileName;

            CompleteToken = completeToken;
        }

        public IFileProvider FileProvider => _fileAppender.FileProvider;

        public string FallbackFileName { get; }

        public virtual DateTimeOffset GetTimestamp() => DateTimeOffset.UtcNow;

        public CancellationToken CompleteToken { get; }

        public event Action<IFileLoggerProcessor, Task> Complete;

        public virtual void OnComplete(IFileLoggerProcessor sender, Task completionTask)
        {
            Complete?.Invoke(sender, completionTask);
        }

        public virtual Task<bool> EnsureDirAsync(IFileInfo fileInfo)
        {
            return _fileAppender.EnsureDirAsync(fileInfo);
        }

        public virtual Task AppendAllTextAsync(IFileInfo fileInfo, string text, Encoding encoding)
        {
            return _fileAppender.AppendAllTextAsync(fileInfo, text, encoding);
        }
    }
}
