using System;
using System.Buffers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Karambolo.Extensions.Logging.File
{
    public partial class FileLoggerProcessor : IFileLoggerProcessor
    {
        protected internal partial class LogFileInfo
        {
            internal async ValueTask WriteTextAsync(string text, Encoding encoding, CancellationToken cancellationToken)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(encoding.GetMaxByteCount(text.Length));

                try
                {
                    var byteCount = encoding.GetBytes(text, buffer);
                    await _appendStream.WriteAsync(buffer.AsMemory(0, byteCount), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            internal Task WriteBytesAsync(byte[] bytes, CancellationToken cancellationToken)
            {
                return _appendStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            }
        }
    }
}
