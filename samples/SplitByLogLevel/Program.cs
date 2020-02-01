using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SplitByLogLevel
{
    // This sample demonstrates how to use multiple file logger providers with different settings.
    // We set up two provider instances to send messages to different files based on their log levels.
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

                // the "standard" provider which logs all messages with severity warning or above to 'warn+err.log' (see appsettings.json for configuration settings)
                builder.AddFile(o => o.RootPath = AppContext.BaseDirectory);

                // a custom one which only logs messages with level information or below to 'info.log'
                builder.AddFile<InfoFileLoggerProvider>(configure: o => o.RootPath = AppContext.BaseDirectory);
            });

            await using (ServiceProvider sp = services.BuildServiceProvider())
            {
                // create logger
                ILogger<Program> logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();

                logger.LogTrace("This is a trace message. Should be discarded.");
                logger.LogDebug("This is a debug message. Should be discarded.");
                logger.LogInformation("This is an info message. Should go into 'info.log' only.");
                logger.LogWarning("This is a warning message. Should go into 'warn+err.log' only.");
                logger.LogError("This is an error message. Should go into 'warn+err.log' only.");
                logger.LogCritical("This is a critical message. Should go into 'warn+err.log' only.");
            }
        }
    }
 }
