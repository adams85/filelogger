using System;
using System.IO;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File.Test.Mocks;

internal class MemoryFileInfo : IFileInfo
{
    private readonly MemoryFileProvider _owner;

    public MemoryFileInfo(MemoryFileProvider owner, string path)
    {
        _owner = owner;
        LogicalPath = MemoryFileProvider.NormalizePath(path);
        Name = Path.GetFileName(LogicalPath);
    }

    public bool Exists => _owner.Exists(LogicalPath);

    public long Length => _owner.GetLength(LogicalPath);

    public string LogicalPath { get; }

    public string PhysicalPath => null;

    public string Name { get; }

    public DateTimeOffset LastModified => throw new NotImplementedException();

    public bool IsDirectory => _owner.IsDirectory(LogicalPath);

    public Stream CreateReadStream() => _owner.GetStream(LogicalPath);
}
