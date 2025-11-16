using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Karambolo.Extensions.Logging.File;

public partial class FileLoggerProcessor : IFileLoggerProcessor
{
    protected internal partial class LogFileInfo
    {
        internal Task WriteTextAsync(string text, Encoding encoding, CancellationToken cancellationToken)
        {
            Debug.Assert(_appendStream is not null);

            byte[] bytes = encoding.GetBytes(text);
            return _appendStream!.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        }

        internal Task WriteBytesAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            Debug.Assert(_appendStream is not null);

            return _appendStream!.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        }
    }
}
