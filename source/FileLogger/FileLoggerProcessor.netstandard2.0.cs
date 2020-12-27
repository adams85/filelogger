using System.Threading;
using System.Threading.Tasks;

namespace Karambolo.Extensions.Logging.File
{
    public partial class FileLoggerProcessor : IFileLoggerProcessor
    {
        protected partial class LogFileInfo
        {
            internal Task WriteBytesAsync(byte[] bytes, CancellationToken cancellationToken)
            {
                return _appendStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            }
        }
    }
}
