using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace issue11
{
    internal class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly Channel<object> _queue = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false,
        });

        public async Task EnqueueAsync(object order, CancellationToken ct) => await _queue.Writer.WriteAsync(order, ct);

        public async Task<object> DequeueAsync(CancellationToken ct) => await _queue.Reader.ReadAsync(ct);
    }
}
