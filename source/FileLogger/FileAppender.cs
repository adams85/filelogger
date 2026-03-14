using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File;

public readonly struct FileAppenderStreamCreationOptions
{
    public FileAppenderStreamCreationOptions(bool useAsyncIO, bool disableBuffering)
    {
        _useSyncIO = !useAsyncIO;
        DisableBuffering = disableBuffering;
    }

    private readonly bool _useSyncIO;
    public bool UseAsyncIO { get => !_useSyncIO; }

    public bool DisableBuffering { get; }
}

public interface IFileAppender
{
    IFileProvider FileProvider { get; }

    bool EnsureDir(IFileInfo fileInfo);
    Task<bool> EnsureDirAsync(IFileInfo fileInfo, CancellationToken cancellationToken = default);

    Stream CreateAppendStream(IFileInfo fileInfo, FileAppenderStreamCreationOptions options = default);
}

public class PhysicalFileAppender : IFileAppender, IDisposable
{
    private readonly int _appendStreamBufferSize;
    private readonly FileOptions _appendStreamFileOptions;
    private readonly bool _isOwner;

    public PhysicalFileAppender(string root)
        : this(new PhysicalFileProvider(root), isOwner: true) { }

    public PhysicalFileAppender(PhysicalFileProvider fileProvider, bool isOwner = false)
        : this(fileProvider, 4096, FileOptions.None, isOwner) { }

    public PhysicalFileAppender(PhysicalFileProvider fileProvider, int appendStreamBufferSize, FileOptions appendStreamFileOptions, bool isOwner = false)
    {
        FileProvider = fileProvider ?? throw new ArgumentNullException(nameof(fileProvider));
        _appendStreamBufferSize = appendStreamBufferSize;
        _appendStreamFileOptions = (appendStreamFileOptions & ~FileOptions.RandomAccess) | FileOptions.SequentialScan | FileOptions.Asynchronous;
        _isOwner = isOwner;
    }

    public void Dispose()
    {
        if (_isOwner)
            FileProvider.Dispose();
    }

    public PhysicalFileProvider FileProvider { get; }

    IFileProvider IFileAppender.FileProvider => FileProvider;

    public bool EnsureDir(IFileInfo fileInfo)
    {
        string dirPath = Path.GetDirectoryName(fileInfo.PhysicalPath)!;
        if (Directory.Exists(dirPath))
            return false;

        Directory.CreateDirectory(dirPath);
        return true;
    }

    public Task<bool> EnsureDirAsync(IFileInfo fileInfo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EnsureDir(fileInfo));
    }

    public Stream CreateAppendStream(IFileInfo fileInfo, FileAppenderStreamCreationOptions options = default)
    {
        return new FileStream(fileInfo.PhysicalPath!, FileMode.Append, FileAccess.Write, FileShare.Read,
#if NET6_0_OR_GREATER
            !options.DisableBuffering ? _appendStreamBufferSize : 0,
#else
            _appendStreamBufferSize,
#endif
            options.UseAsyncIO ? _appendStreamFileOptions : (_appendStreamFileOptions & ~FileOptions.Asynchronous));
    }
}
