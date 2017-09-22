using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;

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
        public FileLoggerContext(string rootPath, string fallbackFileName, CancellationToken completeToken = default(CancellationToken))
            : this(new PhysicalFileProvider(rootPath), fallbackFileName, completeToken) { }

        public FileLoggerContext(IFileProvider fileProvider, string fallbackFileName, CancellationToken completeToken = default(CancellationToken))
        {
            if (fileProvider == null)
                throw new ArgumentNullException(nameof(fileProvider));

            if (fallbackFileName == null)
                throw new ArgumentNullException(nameof(fallbackFileName));

            FileProvider = fileProvider;
            FallbackFileName = fallbackFileName;

            CompleteToken = completeToken;
        }

        public IFileProvider FileProvider { get; }

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
            if (!(fileInfo is PhysicalFileInfo))
                throw new NotSupportedException("File system is not supported.");

            var dirPath = Path.GetDirectoryName(fileInfo.PhysicalPath);
            if (Directory.Exists(dirPath))
                return Task.FromResult(false);

            Directory.CreateDirectory(dirPath);
            return Task.FromResult(true);
        }

        public virtual async Task AppendAllTextAsync(IFileInfo fileInfo, string text, Encoding encoding)
        {
            if (!(fileInfo is PhysicalFileInfo))
                throw new NotSupportedException("File system is not supported.");

            using (var fileStream = new FileStream(fileInfo.PhysicalPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                if (fileStream.Length == 0)
                {
                    var preamble = encoding.GetPreamble();
                    await fileStream.WriteAsync(preamble, 0, preamble.Length).ConfigureAwait(false);
                }

                var data = encoding.GetBytes(text);
                await fileStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
            }
        }
    }
}
