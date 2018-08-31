using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Karambolo.Extensions.Logging.File.Test.MockObjects;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Karambolo.Extensions.Logging.File.Test
{
    public class EdgeCasesTest
    {
        const string logsDirName = "Logs_EC";

        [Fact]
        public void FailingEntryDontGetStuck()
        {
            var tempPath = Path.GetTempPath();
            var logPath = Path.Combine(tempPath, logsDirName);

            if (Directory.Exists(logPath))
                Directory.Delete(logPath, recursive: true);
            Directory.CreateDirectory(logPath);
            try
            {
                var context = new TestFileLoggerContext(new PhysicalFileProvider(tempPath));

                context.SetWriteRetryDelay(TimeSpan.FromMilliseconds(250));
                context.SetCompletionTimeout(TimeSpan.FromMilliseconds(2000));

                var completionTasks = new List<Task>();
                context.Complete += (s, e) => completionTasks.Add(e);

                var cts = new CancellationTokenSource();
                var settings = new FileLoggerSettings
                {
                    BasePath = logsDirName,
                    FileNameMappings = new Dictionary<string, string>
                    {
                        { "Default", "default.log" }
                    },
                    Switches = new Dictionary<string, LogLevel>
                    {
                        { FileLoggerSettingsBase.DefaultCategoryName, LogLevel.Information }
                    },
                    ChangeToken = new CancellationChangeToken(cts.Token)
                };

                var filePath = Path.Combine(logPath, "default.log");
                using (var loggerProvider = new FileLoggerProvider(context, settings))
                {
                    var logger = loggerProvider.CreateLogger("X");

                    logger.LogInformation("This should get through.");

                    var newCts = new CancellationTokenSource();
                    settings.ChangeToken = new CancellationChangeToken(newCts.Token);
                    cts.Cancel();
                    cts = newCts;
                    Assert.Equal(1, completionTasks.Count);
                    Task.WhenAll(completionTasks).GetAwaiter().GetResult();

                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        logger.LogInformation("This shouldn't get through.");

                        cts.Cancel();
                        Assert.Equal(2, completionTasks.Count);

                        var delayTask = Task.Delay(5000);
                        Assert.Equal(completionTasks[1], Task.WhenAny(completionTasks[1], delayTask).GetAwaiter().GetResult());
                        Assert.Equal(TaskStatus.RanToCompletion, completionTasks[1].Status);
                    }
                }

                var lines = System.IO.File.ReadAllLines(filePath);
                Assert.Equal(lines, new[]
                {
                    "info: X[0] @ 0001-01-01T01:00:00.0000000+01:00",
                    "      This should get through.",
                });
            }
            finally
            {
                Directory.Delete(logPath, recursive: true);
            }
        }
    }
}
