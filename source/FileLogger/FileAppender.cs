using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File
{
    public interface IFileAppender
    {
        IFileProvider FileProvider { get; }

        Task<bool> EnsureDirAsync(IFileInfo fileInfo, CancellationToken cancellationToken = default);
        Stream CreateAppendStream(IFileInfo fileInfo);
    }

    public class PhysicalFileAppender : IFileAppender, IDisposable
    {
        private readonly bool _isOwner;

        public PhysicalFileAppender(string root)
            : this(new PhysicalFileProvider(root), isOwner: true) { }

        public PhysicalFileAppender(PhysicalFileProvider fileProvider, bool isOwner = false)
        {
            if (fileProvider == null)
                throw new ArgumentNullException(nameof(fileProvider));

            FileProvider = fileProvider;
            _isOwner = isOwner;
        }

        public void Dispose()
        {
            if (_isOwner)
                FileProvider.Dispose();
        }

        public PhysicalFileProvider FileProvider { get; }

        IFileProvider IFileAppender.FileProvider => FileProvider;

        public Task<bool> EnsureDirAsync(IFileInfo fileInfo, CancellationToken cancellationToken = default)
        {
            var dirPath = Path.GetDirectoryName(fileInfo.PhysicalPath);
            if (Directory.Exists(dirPath))
                return Task.FromResult(false);

            Directory.CreateDirectory(dirPath);
            return Task.FromResult(true);
        }

        public Stream CreateAppendStream(IFileInfo fileInfo)
        {
            return new FileStream(fileInfo.PhysicalPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        }
    }
}
