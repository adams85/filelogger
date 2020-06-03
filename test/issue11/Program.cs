using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Itg.Persistence.Secondary;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace issue11
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            using (var scope = host.Services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

                // we don't resolve FileKeyValueStore from the DI container
                // because we don't want to pollute the logs at this point
                var store = new FileKeyValueStore<ArticleCollection>(
                    new FileKeyValueStoreOptions(),
                    scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
                    NullLogger<FileKeyValueStore<ArticleCollection>>.Instance);

                var random = new Random(0);

                var article = new ArticleCollection
                {
                    Data = new byte[1024 * 1024]
                };

                logger.LogInformation($"Preparing {store.FileInfo.PhysicalPath}...");

                var fileInfo = new FileInfo(store.FileInfo.PhysicalPath);
                if (fileInfo.Exists)
                    fileInfo.Delete();

                for (int i = 0; !fileInfo.Exists || fileInfo.Length < 686 * 1024; fileInfo.Refresh(), i++)
                {
                    random.NextBytes(article.Data);
                    await store.Set("key_" + i, article);
                }
            }

            host.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                    logging.ClearProviders();
                    logging.AddConsole();
#if DEBUG
                    logging.AddDebug();
#endif
                    logging.AddFile(o => o.RootPath = context.HostingEnvironment.ContentRootPath);
                });
    }
}
