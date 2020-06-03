using System.Threading;
using System.Threading.Tasks;
using Itg.Persistence.Secondary;
using Microsoft.Extensions.Logging;

namespace issue11
{
    public static class CreateCollectionBackgroundWork
    {
        public class Order : IBackgroundWorkOrder<Order, Worker>
        {  
            /* contains parameters for it's worker*/ 
        }

        public class Worker : IBackgroundWorker<Order, Worker>
        {
            private readonly ILogger _logger;
            private readonly ShopArticleService _articleService;
            private readonly IKeyValueStore<ArticleCollection> _collectionStore;

            public Worker(
              ILoggerFactory loggerFactory,
              ShopArticleService articleService,
              /* ^ logging works inside this; into integra-<counter> ^ */
              IKeyValueStore<ArticleCollection> collectionStore
              /* ^ the logger in this (FileKeyValueStore) is the one not logging to the file, but does to console, and logs also appear when the config file is reloaded ^ */)
            {
                this._logger = loggerFactory.CreateLogger(nameof(CreateCollectionBackgroundWork));
                // ^ this is actually still logging to integra-<counter> file ^

                _articleService = articleService;
                _collectionStore = collectionStore;
            }

            public async Task DoWork(Order order, CancellationToken ct)
            {
                var model = await _articleService.GetArticleCollectionAsync();

                await _collectionStore.Set("test", model, ct);
            }
        }
    }
}
