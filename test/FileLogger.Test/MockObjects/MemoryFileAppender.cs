using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File.Test.MockObjects
{
    internal class MemoryFileAppender : IFileAppender
    {
        public MemoryFileAppender()
            : this(new MemoryFileProvider()) { }

        public MemoryFileAppender(MemoryFileProvider fileProvider)
        {
            if (fileProvider == null)
                throw new ArgumentNullException(nameof(fileProvider));

            FileProvider = fileProvider;
        }

        public MemoryFileProvider FileProvider { get; }

        IFileProvider IFileAppender.FileProvider => FileProvider;

        public Task<bool> EnsureDirAsync(IFileInfo fileInfo, CancellationToken cancellationToken = default)
        {
            var memoryFileInfo = (MemoryFileInfo)fileInfo;

            IFileInfo dirPath = FileProvider.GetFileInfo(Path.GetDirectoryName(fileInfo.PhysicalPath));
            if (dirPath.Exists)
                return Task.FromResult(false);

            FileProvider.CreateDir(dirPath.PhysicalPath);

            return Task.FromResult(true);
        }

        public Task AppendAllTextAsync(IFileInfo fileInfo, string text, Encoding encoding, CancellationToken cancellationToken = default)
        {
            var memoryFileInfo = (MemoryFileInfo)fileInfo;

            if (!memoryFileInfo.Exists)
                FileProvider.CreateFile(fileInfo.PhysicalPath, null, encoding);
            else if (memoryFileInfo.IsDirectory)
                throw new InvalidOperationException("A directory with the same path already exists.");

            FileProvider.WriteContent(fileInfo.PhysicalPath, text, append: true);

            return Task.CompletedTask;
        }
    }
}
