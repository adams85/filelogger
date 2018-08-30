using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File
{
    public interface IFileAppender
    {
        IFileProvider FileProvider { get; }

        Task<bool> EnsureDirAsync(IFileInfo fileInfo, CancellationToken cancellationToken = default);
        Task AppendAllTextAsync(IFileInfo fileInfo, string text, Encoding encoding, CancellationToken cancellationToken = default);
    }

    public class PhysicalFileAppender : IFileAppender, IDisposable
    {
        readonly bool _isOwner;

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

        public async Task AppendAllTextAsync(IFileInfo fileInfo, string text, Encoding encoding, CancellationToken cancellationToken = default)
        {
            using (var fileStream = new FileStream(fileInfo.PhysicalPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                if (fileStream.Length == 0)
                {
                    var preamble = encoding.GetPreamble();
                    await fileStream.WriteAsync(preamble, 0, preamble.Length, cancellationToken).ConfigureAwait(false);
                }

                var data = encoding.GetBytes(text);
                await fileStream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
