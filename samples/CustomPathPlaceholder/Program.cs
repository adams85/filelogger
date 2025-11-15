using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CustomPathPlaceholder;

// This sample demonstrates how to customize the resolving of placeholders in log file paths.
internal class Program
{
    private static readonly string s_appName = Assembly.GetEntryAssembly().GetName().Name;

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
            builder.AddFile(o =>
            {
                o.RootPath = AppContext.BaseDirectory;
                o.PathPlaceholderResolver = (placeholderName, inlineFormat, context) => placeholderName switch
                {
                    // introduce the custom path variable '<appname>'
                    "appname" => s_appName,
                    // this will offset the counter by 1 -> the counter will start at 1
                    "counter" => (context.Counter + 1).ToString(inlineFormat ?? context.CounterFormat, CultureInfo.InvariantCulture),
                    // in other cases, fallback to the default behavior
                    _ => null,
                };
            });
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
