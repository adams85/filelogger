using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LogRotation
{
    // This sample demonstrates how to extend the options and behavior of the file logger by
    // subclassing FileLoggerOptions and FileLoggerProcessor.
    // More specifically, in this sample we implement an extended logger processor which
    // does log rotation based on the following rules:
    // * fixed number of log files, where the number is configurable through the standard options API
    // * the latest file is always log.0.txt, the next oldest is log.1.txt, etc.
    // * rotate on max file size
    // * when rotation happens, shift log files:
    //   * log.N.txt is deleted
    //   * log.N-1.txt is renamed to log.N.txt
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

                // register our customized file logger (with the extended options) instead of the "standard" one
                builder.AddFile<RotatingFileLoggerProvider, RotatingFileLoggerOptions>(
                    // the next line is required only if the application is published as self-contained trimmed or Native AOT
                    bindOptions: (o, cfg) => cfg.Bind(new RotatingFileLoggerOptions.BindingWrapper(o)),
                    configure: o => o.RootPath = AppContext.BaseDirectory);
            });

            await using (ServiceProvider sp = services.BuildServiceProvider())
            {
                // create logger
                ILogger<Program> logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();

                Console.WriteLine("Press ENTER to log an entry. Press Ctrl+C to end the program.");
                for (int i = 1; Console.ReadLine() != null; i++)
                {
                    logger.LogInformation("This is info message nr. {NUM}", i);
                }
            }
        }
    }
 }
