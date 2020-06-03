using System.Threading;
using System.Threading.Tasks;

namespace issue11
{
    public interface IBackgroundTaskQueue
    {
        Task EnqueueAsync(object order, CancellationToken ct);
        Task<object> DequeueAsync(CancellationToken ct);
    }
}
