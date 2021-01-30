using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CustomFormat
{
    // This sample demonstrates how to customize the output of the logger by
    // subclassing FileLogEntryTextBuilder and overriding its virtual methods.
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

                builder.AddFile(configure: o =>
                {
                    o.RootPath = AppContext.BaseDirectory;

                    // This is how the custom text builder is configured by code,
                    // but in this case we do that in appsettings.json.
                    //o.TextBuilder = SingleLineLogEntryTextBuilder.Default;
                });
            });

            await using (ServiceProvider sp = services.BuildServiceProvider())
            {
                // create logger
                var logger = sp.GetRequiredService<ILogger<Program>>();

                logger.LogInformation("A non-scoped message.");
                using (logger.BeginScope("A scope"))
                {
                    logger.LogInformation("A scoped message.");

                    using (logger.BeginScope("Another scope"))
                    {
                        try { throw new ApplicationException("Some error."); }
                        catch (Exception ex) { logger.LogInformation(ex, $"Another scoped multi-line message{Environment.NewLine}with exception."); }
                    }
                }
            }
        }
    }
}
