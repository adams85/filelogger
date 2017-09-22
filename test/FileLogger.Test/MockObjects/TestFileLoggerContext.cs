using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File.Test.MockObjects
{
    class TestFileLoggerContext : FileLoggerContext
    {
        public TestFileLoggerContext() : this(CancellationToken.None) { }

        public TestFileLoggerContext(IFileProvider fileProvider) : this(fileProvider, CancellationToken.None) { }

        public TestFileLoggerContext(CancellationToken completeToken) : this(new MemoryFileProvider(), completeToken) { }

        public TestFileLoggerContext(IFileProvider fileProvider, CancellationToken completeToken)
            : base(fileProvider, "fallback.log", completeToken) { }

        DateTimeOffset _timestamp;
        public override DateTimeOffset GetTimestamp() => _timestamp;
        public void SetTimestamp(DateTimeOffset value) => _timestamp = value;

        public override Task AppendAllTextAsync(IFileInfo fileInfo, string text, Encoding encoding)
        {
            if (FileProvider is MemoryFileProvider memoryFileProvider)
            {
                var memoryFileInfo = (MemoryFileInfo)fileInfo;

                if (!memoryFileInfo.Exists)
                    memoryFileProvider.CreateFile(fileInfo.PhysicalPath, null, encoding);
                else if (memoryFileInfo.IsDirectory)
                    throw new InvalidOperationException("A directory with the same path already exists.");

                memoryFileProvider.WriteContent(fileInfo.PhysicalPath, text, append: true);

                return Task.FromResult<object>(null);
            }
            else
                return base.AppendAllTextAsync(fileInfo, text, encoding);
        }

        public override Task<bool> EnsureDirAsync(IFileInfo fileInfo)
        {
            if (FileProvider is MemoryFileProvider memoryFileProvider)
            {
                var memoryFileInfo = (MemoryFileInfo)fileInfo;

                var dirPath = memoryFileProvider.GetFileInfo(Path.GetDirectoryName(fileInfo.PhysicalPath));
                if (dirPath.Exists)
                    return Task.FromResult(false);

                memoryFileProvider.CreateDir(dirPath.PhysicalPath);
                return Task.FromResult(true);
            }
            else
                return base.EnsureDirAsync(fileInfo);
        }
    }
}
