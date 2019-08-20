using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Karambolo.Extensions.Logging.File.Test.Helpers;
using Karambolo.Extensions.Logging.File.Test.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Karambolo.Extensions.Logging.File.Test
{
    public class EdgeCasesTest
    {
        [Fact]
        public async Task FailingEntryDontGetStuck()
        {
            var logsDirName = Guid.NewGuid().ToString("D");

            var tempPath = Path.Combine(Path.GetTempPath());
            var logPath = Path.Combine(tempPath, logsDirName);

            if (Directory.Exists(logPath))
                Directory.Delete(logPath, recursive: true);

            var fileProvider = new PhysicalFileProvider(tempPath);

            var options = new FileLoggerOptions
            {
                FileAppender = new PhysicalFileAppender(fileProvider),
                BasePath = logsDirName,
                Files = new[]
                {
                    new LogFileOptions
                    {
                        Path = "default.log",
                    },
                },
            };
            var optionsMonitor = new DelegatedOptionsMonitor<FileLoggerOptions>(_ => options);

            var completeCts = new CancellationTokenSource();
            var completionTimeoutMs = 2000;
            var context = new TestFileLoggerContext(completeCts.Token, TimeSpan.FromMilliseconds(completionTimeoutMs), writeRetryDelay: TimeSpan.FromMilliseconds(250));
            context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging(b => b.AddFile(context));
            services.AddSingleton<IOptionsMonitor<FileLoggerOptions>>(optionsMonitor);

            string filePath = Path.Combine(logPath, "default.log");

            try
            {
                var ex = new Exception();

                FileLoggerProvider[] providers;

                using (ServiceProvider sp = services.BuildServiceProvider())
                {
                    providers = context.GetProviders(sp).ToArray();
                    Assert.Equal(1, providers.Length);

                    var resetTasks = new List<Task>();
                    foreach (FileLoggerProvider provider in providers)
                        provider.Reset += (s, e) => resetTasks.Add(e);

                    ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    ILogger logger = loggerFactory.CreateLogger("X");

                    logger.LogInformation("This should get through.");

                    optionsMonitor.Reload();
                    // ensuring that reset has been finished and the new settings are effective
                    await Task.WhenAll(resetTasks);

                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        logger.LogInformation("This shouldn't get through.");

                        Task completion = context.GetCompletion(sp);
                        Assert.False(completion.IsCompleted);

                        completeCts.Cancel();

                        Assert.Equal(completion, await Task.WhenAny(completion, Task.Delay(TimeSpan.FromMilliseconds(completionTimeoutMs * 2))));
                        Assert.Equal(TaskStatus.RanToCompletion, completion.Status);
                    }
                }

                IFileInfo logFile = fileProvider.GetFileInfo($"{logsDirName}/default.log");
                Assert.True(logFile.Exists && !logFile.IsDirectory);

                var lines = logFile.ReadAllText(out Encoding encoding).Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                Assert.Equal(Encoding.UTF8, encoding);
                Assert.Equal(lines, new[]
                {
                    $"info: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
                    $"      This should get through.",
                    ""
                });
            }
            finally
            {
                Directory.Delete(logPath, recursive: true);
            }
        }
    }
}
