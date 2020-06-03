using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Itg.Persistence.Secondary;
using Microsoft.Extensions.Logging;

namespace issue11
{
    public class ShopArticleService
    {
        private readonly ILoggerFactory _loggerFactory;

        public ShopArticleService(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public async Task<ArticleCollection> GetArticleCollectionAsync()
        {
            var logger = _loggerFactory.CreateLogger("NHibernate.SomeNHibernateComponent");

            // simulating heavy logging of DB access
            for (int i = 0; i < 1_000; i++)
            {
                await Task.Yield(); // kind of a no-op simulating async control flow

                logger.LogInformation("#{COUNT} NHibernate message", i);
            }

            var result = new ArticleCollection
            {
                Data = new byte[2 * 1024] // 2kb data
            };

            new Random(0).NextBytes(result.Data);

            return result;
        }
    }
}
