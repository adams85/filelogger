using System.Threading;
using System.Threading.Tasks;

namespace Karambolo.Extensions.Logging.File
{
    public partial class FileLoggerProcessor : IFileLoggerProcessor
    {
        protected partial class LogFileInfo
        {
            internal ValueTask WriteBytesAsync(byte[] bytes, CancellationToken cancellationToken)
            {
                return _appendStream.WriteAsync(bytes, cancellationToken);
            }
        }
    }
}
