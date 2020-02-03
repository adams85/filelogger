using System.Threading;
using System.Threading.Tasks;

namespace Karambolo.Extensions.Logging.File
{
    public partial class FileLoggerProcessor : IFileLoggerProcessor
    {
        protected static Task WriteBytesAsync(LogFileInfo logFile, byte[] bytes, CancellationToken cancellationToken)
        {
            return logFile.AppendStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        }
    }
}
