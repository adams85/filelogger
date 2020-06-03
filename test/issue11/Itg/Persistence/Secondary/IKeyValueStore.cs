using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Itg.Persistence.Secondary
{
    public interface IKeyValueStore<TModel>
    {
        IFileInfo FileInfo { get; }

        Task<TModel> Get(string key, CancellationToken ct = default);

        Task Delete(string key, CancellationToken ct = default);

        Task Set(string key, TModel data, CancellationToken ct = default);

        Task<int> Clear(TimeSpan? olderThan = null, CancellationToken ct = default);
    }
}
