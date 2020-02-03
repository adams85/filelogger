using System;
using System.Threading.Tasks;

namespace Karambolo.Extensions.Logging.File
{
    public partial class FileLoggerProvider : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (TryDisposeAsync(completeProcessorOnThreadPool: false, out Task completeProcessorTask))
            {
                await completeProcessorTask.ConfigureAwait(false);
                Processor.Dispose();
            }
        }
    }
}
