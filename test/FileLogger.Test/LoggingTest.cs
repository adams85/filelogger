using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Karambolo.Extensions.Logging.File.Test.Helpers;
using Karambolo.Extensions.Logging.File.Test.Mocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Karambolo.Extensions.Logging.File.Test;

public class LoggingTest
{
    [Theory]
    [InlineData(LogFileAccessMode.KeepOpen)]
    [InlineData(LogFileAccessMode.KeepOpenAndAutoFlush)]
    [InlineData(LogFileAccessMode.OpenTemporarily)]
    public async Task LoggingToMemoryWithoutDI(LogFileAccessMode accessMode)
    {
        const string logsDirName = "Logs";

        var fileProvider = new MemoryFileProvider();

        var filterOptions = new LoggerFilterOptions { MinLevel = LogLevel.Trace };

        var options = new FileLoggerOptions
        {
            FileAppender = new MemoryFileAppender(fileProvider),
            BasePath = logsDirName,
            FileAccessMode = accessMode,
            FileEncoding = Encoding.UTF8,
            MaxQueueSize = 100,
            DateFormat = "yyMMdd",
            CounterFormat = "000",
            MaxFileSize = 10,
            Files =
            [
                new LogFileOptions
                {
                    Path = "<date>/<date:MM>/logger.log",
                    DateFormat = "yyyy",
                    MinLevel = new Dictionary<string, LogLevel>
                    {
                        ["Karambolo.Extensions.Logging.File"] = LogLevel.None,
                        [LogFileOptions.DefaultCategoryName] = LogLevel.Information,
                    }
                },
                new LogFileOptions
                {
                    Path = "test-<date>-<counter>.log",
                    MinLevel = new Dictionary<string, LogLevel>
                    {
                        ["Karambolo.Extensions.Logging.File"] = LogLevel.Information,
                        [LogFileOptions.DefaultCategoryName] = LogLevel.None,
                    }
                },
            ],
            TextBuilder = new CustomLogEntryTextBuilder(),
            IncludeScopes = true,
        };

        var context = new TestFileLoggerContext(CancellationToken.None, completionTimeout: Timeout.InfiniteTimeSpan);

        context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        bool diagnosticEventReceived = false;
        context.DiagnosticEvent += _ => diagnosticEventReceived = true;

        var ex = new Exception();

        var provider = new FileLoggerProvider(context, Options.Create(options));

        await using (provider)
        using (var loggerFactory = new LoggerFactory([provider], filterOptions))
        {
            ILogger<LoggingTest> logger1 = loggerFactory.CreateLogger<LoggingTest>();

            logger1.LogInformation("This is a nice logger.");
            using (logger1.BeginScope("SCOPE"))
            {
                logger1.LogWarning(1, "This is a smart logger.");
                logger1.LogTrace("This won't make it.");

                using (logger1.BeginScope("NESTED SCOPE"))
                {
                    ILogger logger2 = loggerFactory.CreateLogger("X");
                    logger2.LogWarning("Some warning.");
                    logger2.LogError(0, ex, "Some failure!");
                }
            }
        }

        Assert.True(provider.Completion.IsCompleted);

        Assert.False(diagnosticEventReceived);

        var logFile = (MemoryFileInfo)fileProvider.GetFileInfo($"{logsDirName}/test-{context.GetTimestamp().ToLocalTime():yyMMdd}-000.log");
        Assert.True(logFile.Exists && !logFile.IsDirectory);

        string[] lines = logFile.ReadAllText(out Encoding encoding).Split([Environment.NewLine], StringSplitOptions.None);
        Assert.Equal(Encoding.UTF8, encoding);
        Assert.Equal(new[]
        {
            $"[info]: {typeof(LoggingTest)}[0] @ {context.GetTimestamp().ToLocalTime():o}",
            $"        This is a nice logger.",
            ""
        }, lines);

        logFile = (MemoryFileInfo)fileProvider.GetFileInfo($"{logsDirName}/test-{context.GetTimestamp().ToLocalTime():yyMMdd}-001.log");
        Assert.True(logFile.Exists && !logFile.IsDirectory);

        lines = logFile.ReadAllText(out encoding).Split([Environment.NewLine], StringSplitOptions.None);
        Assert.Equal(Encoding.UTF8, encoding);
        Assert.Equal(new[]
        {
            $"[warn]: {typeof(LoggingTest)}[1] @ {context.GetTimestamp().ToLocalTime():o}",
            $"        => SCOPE",
            $"        This is a smart logger.",
            ""
        }, lines);

        logFile = (MemoryFileInfo)fileProvider.GetFileInfo(
            $"{logsDirName}/{context.GetTimestamp().ToLocalTime():yyyy}/{context.GetTimestamp().ToLocalTime():MM}/logger.log");
        Assert.True(logFile.Exists && !logFile.IsDirectory);

        lines = logFile.ReadAllText(out encoding).Split([Environment.NewLine], StringSplitOptions.None);
        Assert.Equal(Encoding.UTF8, encoding);
        Assert.Equal(new[]
        {
            $"[warn]: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
            $"        => SCOPE => NESTED SCOPE",
            $"        Some warning.",
            $"[fail]: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
            $"        => SCOPE => NESTED SCOPE",
            $"        Some failure!",
        }
        .Concat(ex.ToString().Split([Environment.NewLine], StringSplitOptions.None))
        .Concat([""]), lines);
    }

    [Theory]
    [InlineData(LogFileAccessMode.KeepOpen)]
    [InlineData(LogFileAccessMode.KeepOpenAndAutoFlush)]
    [InlineData(LogFileAccessMode.OpenTemporarily)]
    public async Task LoggingToPhysicalUsingDI(LogFileAccessMode accessMode)
    {
        string logsDirName = Guid.NewGuid().ToString("D");

        var configData = new Dictionary<string, string?>
        {
            [$"{nameof(FileLoggerOptions.BasePath)}"] = logsDirName,
            [$"{nameof(FileLoggerOptions.FileEncodingName)}"] = "UTF-16",
            [$"{nameof(FileLoggerOptions.DateFormat)}"] = "yyMMdd",
            [$"{nameof(FileLoggerOptions.FileAccessMode)}"] = accessMode.ToString(),
            [$"{nameof(FileLoggerOptions.Files)}:0:{nameof(LogFileOptions.Path)}"] = "logger-<date>.log",
            [$"{nameof(FileLoggerOptions.Files)}:0:{nameof(LogFileOptions.MinLevel)}:Karambolo.Extensions.Logging.File"] = LogLevel.None.ToString(),
            [$"{nameof(FileLoggerOptions.Files)}:0:{nameof(LogFileOptions.MinLevel)}:{LogFileOptions.DefaultCategoryName}"] = LogLevel.Information.ToString(),
            [$"{nameof(FileLoggerOptions.Files)}:1:{nameof(LogFileOptions.Path)}"] = "test-<date>.log",
            [$"{nameof(FileLoggerOptions.Files)}:1:{nameof(LogFileOptions.MinLevel)}:Karambolo.Extensions.Logging.File.Test"] = LogLevel.Information.ToString(),
            [$"{nameof(FileLoggerOptions.Files)}:1:{nameof(LogFileOptions.MinLevel)}:{LogFileOptions.DefaultCategoryName}"] = LogLevel.None.ToString(),
        };

        var cb = new ConfigurationBuilder();
        cb.AddInMemoryCollection(configData);
        IConfigurationRoot config = cb.Build();

        string tempPath = Path.Combine(Path.GetTempPath());
        string logPath = Path.Combine(tempPath, logsDirName);

        var fileProvider = new PhysicalFileProvider(tempPath);

        var cts = new CancellationTokenSource();
        var context = new TestFileLoggerContext(cts.Token, completionTimeout: Timeout.InfiniteTimeSpan);

        context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        bool diagnosticEventReceived = false;
        context.DiagnosticEvent += _ => diagnosticEventReceived = true;

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(b => b.AddFile(context, o => o.FileAppender = new PhysicalFileAppender(fileProvider)));
        services.Configure<FileLoggerOptions>(config);

        if (Directory.Exists(logPath))
            Directory.Delete(logPath, recursive: true);

        try
        {
            var ex = new Exception();

            FileLoggerProvider[] providers;

            using (ServiceProvider sp = services.BuildServiceProvider())
            {
                providers = context.GetProviders(sp).ToArray();
                Assert.Single(providers);

                ILogger<LoggingTest> logger1 = sp.GetRequiredService<ILogger<LoggingTest>>();

                logger1.LogInformation("This is a nice logger.");
                using (logger1.BeginScope("SCOPE"))
                {
                    logger1.LogWarning(1, "This is a smart logger.");
                    logger1.LogTrace("This won't make it.");

                    using (logger1.BeginScope("NESTED SCOPE"))
                    {
                        ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                        ILogger logger2 = loggerFactory.CreateLogger("X");
                        logger2.LogError(0, ex, "Some failure!");
                    }
                }

                cts.Cancel();

                // ensuring that all entries are processed
                await context.GetCompletion(sp);
                Assert.True(providers.All(provider => provider.Completion.IsCompleted));
            }

            Assert.False(diagnosticEventReceived);

            IFileInfo logFile = fileProvider.GetFileInfo($"{logsDirName}/test-{context.GetTimestamp().ToLocalTime():yyMMdd}.log");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            string[] lines = logFile.ReadAllText(out Encoding encoding).Split([Environment.NewLine], StringSplitOptions.None);
            Assert.Equal(Encoding.Unicode, encoding);
            Assert.Equal(new[]
            {
                $"info: {typeof(LoggingTest)}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is a nice logger.",
                $"warn: {typeof(LoggingTest)}[1] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is a smart logger.",
                ""
            }, lines);

            logFile = fileProvider.GetFileInfo(
                $"{logsDirName}/logger-{context.GetTimestamp().ToLocalTime():yyMMdd}.log");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            lines = logFile.ReadAllText(out encoding).Split([Environment.NewLine], StringSplitOptions.None);
            Assert.Equal(Encoding.Unicode, encoding);
            Assert.Equal(new[]
            {
                $"fail: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      Some failure!",
            }
            .Concat(ex.ToString().Split([Environment.NewLine], StringSplitOptions.None))
            .Concat([""]), lines);
        }
        finally
        {
            if (Directory.Exists(logPath))
                Directory.Delete(logPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoggingToPhysicalUsingDIAndExpectingDiagnosticEvents()
    {
        string logsDirName = Guid.NewGuid().ToString("D");

        string tempPath = Path.Combine(Path.GetTempPath());
        string logPath = Path.Combine(tempPath, logsDirName);

        var fileProvider = new PhysicalFileProvider(tempPath);

        var cts = new CancellationTokenSource();
        var context = new TestFileLoggerContext(cts.Token, completionTimeout: Timeout.InfiniteTimeSpan);

        context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var diagnosticEvents = new List<IFileLoggerDiagnosticEvent>();
        context.DiagnosticEvent += diagnosticEvents.Add;

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(b => b.AddFile(context, o =>
        {
            o.FileAppender = new PhysicalFileAppender(fileProvider);
            o.BasePath = logsDirName;
            o.FileAccessMode = LogFileAccessMode.KeepOpen;
            o.Files =
            [
                new LogFileOptions
                {
                    Path = "<invalid_filename>.log"
                }
            ];
        }));

        if (Directory.Exists(logPath))
            Directory.Delete(logPath, recursive: true);

        try
        {
            FileLoggerProvider[] providers;

            using (ServiceProvider sp = services.BuildServiceProvider())
            {
                providers = context.GetProviders(sp).ToArray();
                Assert.Single(providers);

                ILogger<LoggingTest> logger1 = sp.GetRequiredService<ILogger<LoggingTest>>();

                logger1.LogInformation("This is a nice logger.");
                logger1.LogWarning(1, "This is a smart logger.");

                cts.Cancel();

                // ensuring that all entries are processed
                await context.GetCompletion(sp);
                Assert.True(providers.All(provider => provider.Completion.IsCompleted));
            }

            Assert.NotEmpty(diagnosticEvents);
            Assert.All(diagnosticEvents, e =>
            {
                Assert.IsType<FileLoggerDiagnosticEvent.LogEntryWriteFailed>(e);
                Assert.IsType<FileLoggerProcessor>(e.Source);
                Assert.NotNull(e.FormattableMessage);
                Assert.NotNull(e.Exception);
            });
        }
        finally
        {
            if (Directory.Exists(logPath))
                Directory.Delete(logPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoggingToMemoryUsingCustomPathPlaceholderResolver()
    {
        const string appName = "myapp";
        const string logsDirName = "Logs";

        var fileProvider = new MemoryFileProvider();

        var cts = new CancellationTokenSource();
        var context = new TestFileLoggerContext(cts.Token, completionTimeout: Timeout.InfiniteTimeSpan);

        context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(b => b.AddFile(context, o =>
        {
            o.FileAppender = new MemoryFileAppender(fileProvider);
            o.BasePath = logsDirName;
            o.Files =
            [
                new LogFileOptions
                {
                    Path = "<appname>-<counter:000>.log"
                }
            ];
            o.PathPlaceholderResolver = (placeholderName, inlineFormat, context) => placeholderName == "appname" ? appName : null;
        }));

        FileLoggerProvider[] providers;

        using (ServiceProvider sp = services.BuildServiceProvider())
        {
            providers = context.GetProviders(sp).ToArray();
            Assert.Single(providers);

            ILogger<LoggingTest> logger1 = sp.GetRequiredService<ILogger<LoggingTest>>();

            logger1.LogInformation("This is a nice logger.");
            logger1.LogWarning(1, "This is a smart logger.");

            cts.Cancel();

            // ensuring that all entries are processed
            await context.GetCompletion(sp);
            Assert.True(providers.All(provider => provider.Completion.IsCompleted));
        }

        var logFile = (MemoryFileInfo)fileProvider.GetFileInfo($"{logsDirName}/{appName}-000.log");
        Assert.True(logFile.Exists && !logFile.IsDirectory);
    }

#if NET8_0_OR_GREATER
    [Fact]
    public async Task LoggingToMemoryWhenTimeProviderIsRegisteredInDI()
    {
        const string appName = "myapp";

        var fileProvider = new MemoryFileProvider();

        var fakeTimeProvider = new TestTimeProvider();
        fakeTimeProvider.SetUtcNow(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(fakeTimeProvider);
        services.AddOptions();
        services.AddLogging(b => b.AddFile(o =>
        {
            o.FileAppender = new MemoryFileAppender(fileProvider);
            o.Files =
            [
                new LogFileOptions
                {
                    Path = "test-<date>.log"
                }
            ];
            o.PathPlaceholderResolver = (placeholderName, inlineFormat, context) => placeholderName == "appname" ? appName : null;
        }));

        await using (ServiceProvider sp = services.BuildServiceProvider())
        {
            ILogger<LoggingTest> logger1 = sp.GetRequiredService<ILogger<LoggingTest>>();

            logger1.LogInformation("This is a nice logger.");
            logger1.LogWarning(1, "This is a smart logger.");
        }

        var logFile = (MemoryFileInfo)fileProvider.GetFileInfo($"test-{fakeTimeProvider.GetUtcNow():yyyyMMdd}.log");
        Assert.True(logFile.Exists && !logFile.IsDirectory);

        string[] lines = logFile.ReadAllText(out Encoding encoding).Split([Environment.NewLine], StringSplitOptions.None);
        Assert.Equal(Encoding.UTF8, encoding);
        Assert.Equal(new[]
        {
            $"info: {typeof(LoggingTest)}[0] @ {fakeTimeProvider.GetUtcNow().ToLocalTime():o}",
            $"      This is a nice logger.",
            $"warn: {typeof(LoggingTest)}[1] @ {fakeTimeProvider.GetUtcNow().ToLocalTime():o}",
            $"      This is a smart logger.",
            ""
        }, lines);
    }
#endif
}
