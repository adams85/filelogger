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

namespace Karambolo.Extensions.Logging.File.Test;

public class EdgeCasesTest
{
    [PlatformTheory(AssertOn = [OSPlatformEnum.Windows])]
    [InlineData(LogFileWriteStrategy.QueuedAsyncWrite)]
    [InlineData(LogFileWriteStrategy.QueuedSyncWrite)]
    [InlineData(LogFileWriteStrategy.DirectSyncWrite)]
    public async Task FailingEntriesDoesNotGetStuck(LogFileWriteStrategy writeStrategy)
    {
        string logsDirName = Guid.NewGuid().ToString("D");

        string tempPath = Path.Combine(Path.GetTempPath());
        string logPath = Path.Combine(tempPath, logsDirName);

        if (Directory.Exists(logPath))
            Directory.Delete(logPath, recursive: true);

        var fileProvider = new PhysicalFileProvider(tempPath);

        var options = new FileLoggerOptions
        {
            FileAppender = new PhysicalFileAppender(fileProvider),
            BasePath = logsDirName,
            Files =
            [
                new LogFileOptions
                {
                    Path = "default.log",
                },
            ],
            WriteStrategy = writeStrategy
        };
        var optionsMonitor = new DelegatedOptionsMonitor<FileLoggerOptions>(_ => options);

        var completeCts = new CancellationTokenSource();
        int completionTimeoutMs = 2000;
        var context = new TestFileLoggerContext(completeCts.Token, TimeSpan.FromMilliseconds(completionTimeoutMs), writeRetryDelay: TimeSpan.FromMilliseconds(250));
        context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(b => b.AddFile(context));
        services.AddSingleton<IOptionsMonitor<FileLoggerOptions>>(optionsMonitor);

        string filePath = Path.Combine(logPath, "default.log");

        try
        {
            FileLoggerProvider[] providers;
            Task? stuckWriteTask = null;

            using (ServiceProvider sp = services.BuildServiceProvider())
            {
                providers = context.GetProviders(sp).ToArray();
                Assert.Single(providers);

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
                    if (writeStrategy != LogFileWriteStrategy.DirectSyncWrite)
                    {
                        logger.LogInformation("This shouldn't get through.");
                        stuckWriteTask = Task.CompletedTask;
                    }
                    else
                    {
                        stuckWriteTask = Task.Run(() => logger.LogInformation("This shouldn't get through."));
                        await Task.Delay(1500); // allow some time for the thread pool to execute the task
                    }

                    Task completion = context.GetCompletion(sp);
                    Assert.False(completion.IsCompleted);

                    completeCts.Cancel();

                    Assert.Equal(completion, await Task.WhenAny(completion, Task.Delay(TimeSpan.FromMilliseconds(completionTimeoutMs * 2))));
                    Assert.Equal(TaskStatus.RanToCompletion, completion.Status);

                    await stuckWriteTask;
                }
            }

            IFileInfo logFile = fileProvider.GetFileInfo($"{logsDirName}/default.log");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            string[] lines = logFile.ReadAllText(out Encoding encoding).Split([Environment.NewLine], StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, encoding);
            Assert.Equal(new[]
            {
                $"info: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This should get through.",
                ""
            }, lines);
        }
        finally
        {
            Directory.Delete(logPath, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectSyncWriteDoesNotCauseDeadlock(bool reloadOptions)
    {
        const string logsDirName = "Logs";

        var fileProvider = new MemoryFileProvider();

        long state = 0; // disposed flag (1 bit) + counter (63 bits)

        var context = new TestFileLoggerContext(CancellationToken.None, completionTimeout: Timeout.InfiniteTimeSpan);
        context.DiagnosticEvent += e =>
        {
            if (e is FileLoggerDiagnosticEvent.LogEntryDropped eventArgs
                && ReferenceEquals(eventArgs.FormattableMessage.Format, Properties.Resources.DiagnosticEvent_LogEntryDropped_Disposing))
            {
                long oldState, newState;
                do
                {
                    oldState = Interlocked.Read(ref state);
                    long counter = oldState & 0x7FFF_FFFF_FFFF_FFFF;
                    newState = unchecked((long)0x8000_0000_0000_0000) | (counter - 1);
                }
                while (Interlocked.CompareExchange(ref state, newState, oldState) != oldState);
            }
        };

        var options = new FileLoggerOptions
        {
            FileAppender = new MemoryFileAppender(fileProvider),
            BasePath = logsDirName,
            Files =
            [
                new LogFileOptions
                {
                    Path = "default.log",
                },
            ],
            WriteStrategy = LogFileWriteStrategy.DirectSyncWrite
        };
        var optionsMonitor = new DelegatedOptionsMonitor<FileLoggerOptions>(_ => options);

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(b => b.AddFile(context));
        services.AddSingleton<IOptionsMonitor<FileLoggerOptions>>(optionsMonitor);

        string filePath = Path.Combine(logsDirName, "default.log");

        Thread[] threads;

        using (ServiceProvider sp = services.BuildServiceProvider())
        {
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            ILogger logger = loggerFactory.CreateLogger("X");

            threads = Enumerable
                .Range(0, Math.Max(Environment.ProcessorCount, 4))
                .Select(_ => new Thread(() =>
                {
                    for (; ; )
                    {
                        long oldState, newState;
                        do
                        {
                            oldState = Interlocked.Read(ref state);
                            if ((oldState & unchecked((long)0x8000_0000_0000_0000)) != 0)
                            {
                                return;
                            }
                            newState = oldState + 1;
                        }
                        while (Interlocked.CompareExchange(ref state, newState, oldState) != oldState);

                        logger.LogInformation("Message {NUMBER}", newState);
                    }
                }))
                .ToArray();

            Array.ForEach(threads, t => t.Start());

            if (reloadOptions)
            {
                Thread.Sleep(500);

                options.Files[0].Path = "another.log";
                optionsMonitor.Reload();

                Thread.Sleep(500);
            }
            else
            {
                Thread.Sleep(1000);
            }
        }

        Array.ForEach(threads, t => t.Join());

        long expectedCount = state & 0x7FFF_FFFF_FFFF_FFFF;
        long actualCount = 0;

        foreach (string logFilePath in reloadOptions ? new[] { $"{logsDirName}/default.log", $"{logsDirName}/another.log" } : new[] { $"{logsDirName}/default.log" })
        {
            IFileInfo logFile = fileProvider.GetFileInfo(logFilePath);
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            string content = logFile.ReadAllText(out Encoding encoding);
            Assert.Equal(Encoding.UTF8, encoding);

            string entryText = $"info: X[0] @ {context.GetTimestamp().ToLocalTime():o}{Environment.NewLine}      Message ";
            for (int index = 0; (index = content.IndexOf(entryText, index, StringComparison.Ordinal)) >= 0; index += entryText.Length)
            {
                actualCount++;
            }
        }

        Assert.Equal(expectedCount, actualCount);
    }
}
