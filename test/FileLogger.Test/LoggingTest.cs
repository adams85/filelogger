using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Karambolo.Extensions.Logging.File.Test.MockObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Karambolo.Extensions.Logging.File.Test
{
    public class LoggingTest
    {
        const string logsDirName = "Logs";
        public const string FallbackFileName = "default.log";

        [Fact]
        public void LoggingToMemoryUsingFactory()
        {
            var fileProvider = new MemoryFileProvider();

            var settings = new FileLoggerSettings
            {
                FileAppender = new MemoryFileAppender(fileProvider),
                BasePath = logsDirName,
                EnsureBasePath = true,
                FileEncoding = Encoding.UTF8,
                MaxQueueSize = 100,
                DateFormat = "yyyyMMdd",
                CounterFormat = "000",
                MaxFileSize = 10,
                Switches = new Dictionary<string, LogLevel>
                {
                    { FileLoggerSettingsBase.DefaultCategoryName, LogLevel.Information }
                },
                FileNameMappings = new Dictionary<string, string>
                {
                    { "Karambolo.Extensions.Logging.File.Test", "test.log" },
                    { "Karambolo.Extensions.Logging.File", "logger.log" },
                },
                TextBuilder = new CustomLogEntryTextBuilder(),
                IncludeScopes = true,
            };

            var cts = new CancellationTokenSource();
            var context = new TestFileLoggerContext(cts.Token);

            var completionTasks = new List<Task>();
            context.Complete += (s, e) => completionTasks.Add(e);

            context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var ex = new Exception();
            using (var loggerFactory = new LoggerFactory())
            {
                loggerFactory.AddFile(context, settings);
                var logger1 = loggerFactory.CreateLogger<LoggingTest>();

                logger1.LogInformation("This is a nice logger.");
                using (logger1.BeginScope("SCOPE"))
                {
                    logger1.LogWarning(1, "This is a smart logger.");
                    logger1.LogTrace("This won't make it.");

                    using (logger1.BeginScope("NESTED SCOPE"))
                    {
                        var logger2 = loggerFactory.CreateLogger("X");
                        logger2.LogError(0, ex, "Some failure!");
                    }
                }

                // ensuring that all entries are processed
                cts.Cancel();
                Assert.Equal(1, completionTasks.Count);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();
            }

            var logFile = (MemoryFileInfo)fileProvider.GetFileInfo($@"{logsDirName}\test-{context.GetTimestamp().ToLocalTime():yyyyMMdd}-000.log");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            var lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(new[]
            {
                $"[info]: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"        This is a nice logger.",
                ""
            }, lines);

            logFile = (MemoryFileInfo)fileProvider.GetFileInfo($@"{logsDirName}\test-{context.GetTimestamp().ToLocalTime():yyyyMMdd}-001.log");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(new[]
            {
                $"[warn]: {typeof(LoggingTest).FullName}[1] @ {context.GetTimestamp().ToLocalTime():o}",
                $"        => SCOPE",
                $"        This is a smart logger.",
                ""
            }, lines);

            logFile = (MemoryFileInfo)fileProvider.GetFileInfo(
                $@"{logsDirName}\{Path.ChangeExtension(FallbackFileName, null)}-{context.GetTimestamp().ToLocalTime():yyyyMMdd}-000{Path.GetExtension(FallbackFileName)}");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(new[]
            {
                $"[fail]: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"        => SCOPE => NESTED SCOPE",
                $"        Some failure!",
            }
            .Concat(ex.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None))
            .Append(""), lines);
        }

        [Fact]
        public void LoggingToPhysicalUsingProvider()
        {
            var configData = new Dictionary<string, string>
            {
                [$"{nameof(FileLoggerOptions.BasePath)}"] = logsDirName,
                [$"{nameof(FileLoggerOptions.EnsureBasePath)}"] = "true",
                [$"{nameof(FileLoggerOptions.FileEncodingName)}"] = "UTF-16",
                [$"{nameof(FileLoggerOptions.MaxQueueSize)}"] = "100",
                [$"{nameof(FileLoggerOptions.DateFormat)}"] = "yyyyMMdd",
                [$"{ConfigurationFileLoggerSettings.LogLevelSectionName}:{FileLoggerSettingsBase.DefaultCategoryName}"] = "Information",
                [$"{nameof(FileLoggerOptions.FileNameMappings)}:Karambolo.Extensions.Logging.File.Test"] = "test.log",
                [$"{nameof(FileLoggerOptions.FileNameMappings)}:Karambolo.Extensions.Logging.File"] = "logger.log",
            };

            var cb = new ConfigurationBuilder();
            cb.AddInMemoryCollection(configData);
            var config = cb.Build();

            var cts = new CancellationTokenSource();

            var tempPath = Path.GetTempPath();
            var logPath = Path.Combine(tempPath, logsDirName);

            var context = new TestFileLoggerContext(new PhysicalFileProvider(tempPath), "fallback.log", cts.Token);

            var completionTasks = new List<Task>();
            context.Complete += (s, e) => completionTasks.Add(e);

            context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging(b => b.AddFile(context));
            services.Configure<FileLoggerOptions>(config);

            if (Directory.Exists(logPath))
                Directory.Delete(logPath, recursive: true);

            try
            {
                var ex = new Exception();
                var serviceProvider = services.BuildServiceProvider();

                var logger1 = serviceProvider.GetService<ILogger<LoggingTest>>();

                logger1.LogInformation("This is a nice logger.");
                using (logger1.BeginScope("SCOPE"))
                {
                    logger1.LogWarning(1, "This is a smart logger.");
                    logger1.LogTrace("This won't make it.");

                    using (logger1.BeginScope("NESTED SCOPE"))
                    {
                        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                        var logger2 = loggerFactory.CreateLogger("X");
                        logger2.LogError(0, ex, "Some failure!");
                    }
                }

                // ensuring that all entries are processed
                cts.Cancel();
                Assert.Equal(1, completionTasks.Count);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();

#pragma warning disable CS0618 // Type or member is obsolete
                var logFile = context.FileProvider.GetFileInfo($@"{logsDirName}\test-{context.GetTimestamp().ToLocalTime():yyyyMMdd}.log");
#pragma warning restore CS0618 // Type or member is obsolete
                Assert.True(logFile.Exists && !logFile.IsDirectory);

                var lines = ReadContent(logFile, out Encoding encoding).Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                Assert.Equal(Encoding.Unicode, encoding);
                Assert.Equal(new[]
                {
                    $"info: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                    $"      This is a nice logger.",
                    $"warn: {typeof(LoggingTest).FullName}[1] @ {context.GetTimestamp().ToLocalTime():o}",
                    $"      This is a smart logger.",
                    ""
                }, lines);

#pragma warning disable CS0618 // Type or member is obsolete
                logFile = context.FileProvider.GetFileInfo(
                    $@"{logsDirName}\{Path.ChangeExtension(context.FallbackFileName, null)}-{context.GetTimestamp().ToLocalTime():yyyyMMdd}{Path.GetExtension(context.FallbackFileName)}");
#pragma warning restore CS0618 // Type or member is obsolete
                Assert.True(logFile.Exists && !logFile.IsDirectory);

                lines = ReadContent(logFile, out encoding).Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                Assert.Equal(Encoding.Unicode, encoding);
                Assert.Equal(new[]
                {
                    $"fail: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
                    $"      Some failure!",
                }
                .Concat(ex.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                .Append(""), lines);
            }
            finally
            {
                Directory.Delete(logPath, recursive: true);
            }

            string ReadContent(IFileInfo fileInfo, out Encoding encoding)
            {
                using (var stream = fileInfo.CreateReadStream())
                using (var reader = new StreamReader(stream))
                {
                    var result = reader.ReadToEnd();
                    encoding = reader.CurrentEncoding;
                    return result; 
                }
            }
        }
    }
}
