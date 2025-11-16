using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Karambolo.Extensions.Logging.File;

public partial class FileLoggerProcessor : IFileLoggerProcessor
{
    protected internal partial class LogFileInfo
    {
        internal async ValueTask WriteTextAsync(string text, Encoding encoding, CancellationToken cancellationToken)
        {
            Debug.Assert(_appendStream is not null);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(encoding.GetMaxByteCount(text.Length));
            try
            {
                int byteCount = encoding.GetBytes(text, buffer);
                await _appendStream.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, byteCount), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        internal ValueTask WriteBytesAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            Debug.Assert(_appendStream is not null);

            return _appendStream.WriteAsync(new ReadOnlyMemory<byte>(bytes), cancellationToken);
        }
    }
}
