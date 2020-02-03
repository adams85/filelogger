using System.Threading;
using System.Threading.Tasks;

namespace Karambolo.Extensions.Logging.File
{
    public partial class FileLoggerProcessor : IFileLoggerProcessor
    {
        protected virtual async Task WriteEntryCoreAsync(LogFileInfo logFile, FileLogEntry entry, CancellationToken cancellationToken)
        {
            if (logFile.AppendStream.Length == 0)
            {
                var preamble = logFile.Encoding.GetPreamble();
                await logFile.AppendStream.WriteAsync(preamble, 0, preamble.Length, cancellationToken).ConfigureAwait(false);
            }

            var data = logFile.Encoding.GetBytes(entry.Text);
            await logFile.AppendStream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
        }
    }
}
