using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File.Test.Mocks
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

            var dirPath = (MemoryFileInfo)FileProvider.GetFileInfo(Path.GetDirectoryName(memoryFileInfo.LogicalPath));
            if (dirPath.Exists)
                return Task.FromResult(false);

            FileProvider.CreateDir(dirPath.LogicalPath);

            return Task.FromResult(true);
        }

        public Stream CreateAppendStream(IFileInfo fileInfo)
        {
            var memoryFileInfo = (MemoryFileInfo)fileInfo;

            if (!memoryFileInfo.Exists)
                FileProvider.CreateFile(memoryFileInfo.LogicalPath);

            MemoryStream stream = FileProvider.GetStream(memoryFileInfo.LogicalPath);
            stream.Seek(0, SeekOrigin.End);
            return stream;
        }
    }
}
