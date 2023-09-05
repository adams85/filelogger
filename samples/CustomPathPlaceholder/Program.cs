using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CustomBehavior
{
    // This sample demonstrates how to customize the behavior of log message processing by
    // subclassing FileLoggerProcessor and overriding its virtual methods.
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));

                // register our customized file logger instead of the "standard" one
                builder.AddFile<CustomFileLoggerProvider>(configure: o => o.RootPath = AppContext.BaseDirectory);
            });

            await using (ServiceProvider sp = services.BuildServiceProvider())
            {
                // create logger factory
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

                // generate a larger amount of messages
                Parallel.For(0, 1000, i =>
                {
                    var logger = loggerFactory.CreateLogger("Thread" + Thread.CurrentThread.ManagedThreadId);
                    var logLevel = (LogLevel)(i % (int)(LogLevel.Critical + 1));
                    logger.Log(logLevel, 0, "Msg" + i, null, (s, _) => s);
                });
            }
        }
    }
 }
