using System;
using System.IO;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File.Test.Mocks
{
    internal class MemoryFileInfo : IFileInfo
    {
        private readonly MemoryFileProvider _owner;

        public MemoryFileInfo(MemoryFileProvider owner, string path)
        {
            _owner = owner;
            PhysicalPath = MemoryFileProvider.NormalizePath(path);
            Name = Path.GetFileName(PhysicalPath);
        }

        public bool Exists => _owner.Exists(PhysicalPath);

        public long Length => _owner.GetLength(PhysicalPath);

        public string PhysicalPath { get; }

        public string Name { get; }

        public DateTimeOffset LastModified => throw new NotImplementedException();

        public bool IsDirectory => _owner.IsDirectory(PhysicalPath);

        public Stream CreateReadStream() => _owner.GetStream(PhysicalPath);
    }
}
