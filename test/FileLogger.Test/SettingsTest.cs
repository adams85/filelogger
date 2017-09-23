using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Karambolo.Extensions.Logging.File.Test.MockObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Karambolo.Extensions.Logging.File.Test
{
    public class SettingsTest
    {
        [Fact]
        public void ParsingConfigurationSettings()
        {
            var configData = new Dictionary<string, string>
            {
                [$"{nameof(FileLoggerOptions.BasePath)}"] = "Logs",
                [$"{nameof(FileLoggerOptions.EnsureBasePath)}"] = "true",
                [$"{nameof(FileLoggerOptions.FileEncodingName)}"] = "UTF-8",
                [$"{nameof(FileLoggerOptions.FileNameMappings)}:Karambolo.Extensions.Logging.File"] = "logger.log",
                [$"{nameof(FileLoggerOptions.FileNameMappings)}:Karambolo.Extensions.Logging.File.Test"] = "test.log",
                [$"{nameof(FileLoggerOptions.DateFormat)}"] = "yyyyMMdd",
                [$"{nameof(FileLoggerOptions.CounterFormat)}"] = "000",
                [$"{nameof(FileLoggerOptions.MaxFileSize)}"] = "10",
                [$"{nameof(FileLoggerOptions.TextBuilderType)}"] = typeof(CustomLogEntryTextBuilder).AssemblyQualifiedName,
                [$"{ConfigurationFileLoggerSettings.LogLevelSectionName}:Karambolo.Extensions.Logging.File"] = "Warning",
                [$"{ConfigurationFileLoggerSettings.LogLevelSectionName}:Karambolo.Extensions.Logging.File.Test"] = "Information",
                [$"{nameof(FileLoggerOptions.IncludeScopes)}"] = "true",
                [$"{nameof(FileLoggerOptions.MaxQueueSize)}"] = "100",
            };

            var cb = new ConfigurationBuilder();
            cb.AddInMemoryCollection(configData);
            var config = cb.Build();

            var settings = new ConfigurationFileLoggerSettings(config);

            Assert.Equal("Logs", settings.BasePath);
            Assert.Equal(true, settings.EnsureBasePath);
            Assert.Equal(Encoding.UTF8, settings.FileEncoding);
            Assert.Equal("test.log", settings.MapToFileName(typeof(SettingsTest).FullName, "default.log"));
            Assert.Equal("logger.log", settings.MapToFileName(typeof(FileLogger).FullName, "default.log"));
            Assert.Equal("default.log", settings.MapToFileName("X.Y", "default.log"));
            Assert.Equal("yyyyMMdd", settings.DateFormat);
            Assert.Equal("000", settings.CounterFormat);
            Assert.Equal(10, settings.MaxFileSize);
            Assert.Equal(typeof(CustomLogEntryTextBuilder), settings.TextBuilder.GetType());
            Assert.True(settings.TryGetSwitch(typeof(SettingsTest).Namespace, out LogLevel logLevel));
            Assert.Equal(LogLevel.Information, logLevel);
            Assert.True(settings.TryGetSwitch(typeof(FileLogger).Namespace, out logLevel));
            Assert.Equal(LogLevel.Warning, logLevel);
            Assert.False(settings.TryGetSwitch("X.Y", out logLevel));
            Assert.Equal(true, settings.IncludeScopes);
            Assert.Equal(100, settings.MaxQueueSize);
        }

        [Fact]
        public void ParsingOptions()
        {
            var configJson =
$@"{{ 
    '{nameof(FileLoggerOptions.BasePath)}': 'Logs',
    '{nameof(FileLoggerOptions.EnsureBasePath)}': true,
    '{nameof(FileLoggerOptions.FileEncodingName)}': 'utf-8',
    '{nameof(FileLoggerOptions.FileNameMappings)}': {{
        'Karambolo.Extensions.Logging.File': 'logger.log',
        'Karambolo.Extensions.Logging.File.Test': 'test.log',
    }},
    '{nameof(FileLoggerOptions.DateFormat)}': 'yyyyMMdd',
    '{nameof(FileLoggerOptions.CounterFormat)}': '000',
    '{nameof(FileLoggerOptions.MaxFileSize)}': 10,
    '{nameof(FileLoggerOptions.TextBuilderType)}': '{typeof(CustomLogEntryTextBuilder).AssemblyQualifiedName}',
    '{nameof(FileLoggerOptions.IncludeScopes)}': true,
    '{nameof(FileLoggerOptions.IncludeScopes)}': true,
    '{nameof(FileLoggerOptions.MaxQueueSize)}': 100,
}}";

            var fileProvider = new MemoryFileProvider();
            fileProvider.CreateFile("config.json", configJson, Encoding.UTF8);

            var cb = new ConfigurationBuilder();
            cb.AddJsonFile(fileProvider, "config.json", optional: false, reloadOnChange: false);
            var config = cb.Build();

            var services = new ServiceCollection();
            services.AddOptions();
            services.Configure<FileLoggerOptions>(config);
            var serviceProvider = services.BuildServiceProvider();

            var options = serviceProvider.GetService<IOptions<FileLoggerOptions>>().Value;

            Assert.Equal("Logs", options.BasePath);
            Assert.Equal(true, options.EnsureBasePath);
            Assert.Equal(Encoding.UTF8, options.FileEncoding);
            Assert.Equal("test.log", options.MapToFileName(typeof(SettingsTest).FullName, "default.log"));
            Assert.Equal("logger.log", options.MapToFileName(typeof(FileLogger).FullName, "default.log"));
            Assert.Equal("default.log", options.MapToFileName("X.Y", "default.log"));
            Assert.Equal("yyyyMMdd", options.DateFormat);
            Assert.Equal("000", options.CounterFormat);
            Assert.Equal(10, options.MaxFileSize);
            Assert.Equal(typeof(CustomLogEntryTextBuilder), options.TextBuilder.GetType());
            Assert.Equal(true, options.IncludeScopes);
            Assert.Equal(100, options.MaxQueueSize);
        }

        [Fact]
        public void ReloadSettings()
        {
            var cts = new CancellationTokenSource();

            var settings = new FileLoggerSettings
            {
                Switches = new Dictionary<string, LogLevel>
                {
                    { FileLoggerSettingsBase.DefaultCategoryName, LogLevel.Information }
                },
                ChangeToken = new CancellationChangeToken(cts.Token)
            };

            var context = new TestFileLoggerContext(CancellationToken.None);

            var completionTasks = new List<Task>();
            context.Complete += (s, e) => completionTasks.Add(e);

            context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            using (var loggerFactory = new LoggerFactory())
            {
                loggerFactory.AddFile(context, settings);
                var logger1 = loggerFactory.CreateLogger<LoggingTest>();

                logger1.LogInformation("This is a nice logger.");

                // changing text format
                settings.TextBuilder = new CustomLogEntryTextBuilder();

                var newCts = new CancellationTokenSource();
                settings.ChangeToken = new CancellationChangeToken(newCts.Token);
                cts.Cancel();
                cts = newCts;
                Assert.Equal(1, completionTasks.Count);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();

                logger1.LogInformation("This is a smart logger.");

                // changing base path, file encoding and filename mapping
                settings.BasePath = "Logs";
                settings.EnsureBasePath = true;
                settings.FileEncoding = Encoding.Unicode;
                settings.FileNameMappings = new Dictionary<string, string>
                {
                    { typeof(LoggingTest).FullName, "test.log" }
                };

                completionTasks.Clear();
                newCts = new CancellationTokenSource();
                settings.ChangeToken = new CancellationChangeToken(newCts.Token);
                cts.Cancel();
                cts = newCts;
                Assert.Equal(1, completionTasks.Count);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();

                logger1.LogWarning("This goes to another file.");

                // ensuring that the entry is processed
                completionTasks.Clear();
                cts.Cancel();
                Assert.Equal(1, completionTasks.Count);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();
            }

            var logFile = (MemoryFileInfo)context.FileProvider.GetFileInfo($@"fallback.log");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            var lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(lines, new[]
            {
                $"info: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is a nice logger.",
                $"[info]: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"        This is a smart logger.",
                ""
            });

            logFile = (MemoryFileInfo)context.FileProvider.GetFileInfo($@"Logs\test.log");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.Unicode, logFile.Encoding);
            Assert.Equal(lines, new[]
            {
                $"[warn]: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"        This goes to another file.",
                ""
            });
        }

        [Fact]
        public void ReloadConfigurationSettings()
        {
            var configJson =
$@"{{ 
    '{ConfigurationFileLoggerSettings.LogLevelSectionName}': {{
        '{FileLoggerSettingsBase.DefaultCategoryName}': '{LogLevel.Information}',
    }}
}}";

            var fileProvider = new MemoryFileProvider();
            fileProvider.CreateFile("config.json", configJson, Encoding.UTF8);

            var cb = new ConfigurationBuilder();
            cb.AddJsonFile(fileProvider, "config.json", optional: false, reloadOnChange: true);
            var config = cb.Build();

            var settings = new ConfigurationFileLoggerSettings(config);

            var cts = new CancellationTokenSource();
            var context = new TestFileLoggerContext(cts.Token);

            var completionTasks = new List<Task>();
            context.Complete += (s, e) => completionTasks.Add(e);

            context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            using (var loggerFactory = new LoggerFactory())
            {
                loggerFactory.AddFile(context, settings);
                var logger1 = loggerFactory.CreateLogger<LoggingTest>();

                logger1.LogInformation("This is a nice logger.");

                // changing date format, counter format and max file size
                configJson =
$@"{{
    '{nameof(ConfigurationFileLoggerSettings.DateFormat)}' : 'yyyyMMdd',
    '{nameof(ConfigurationFileLoggerSettings.MaxFileSize)}' : 0,
    '{nameof(ConfigurationFileLoggerSettings.CounterFormat)}' : '00',
    '{ConfigurationFileLoggerSettings.LogLevelSectionName}': {{
        '{FileLoggerSettingsBase.DefaultCategoryName}': '{LogLevel.Information}',
    }}
}}";
                fileProvider.WriteContent("config.json", configJson);

                Assert.Equal(1, completionTasks.Count);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();

                logger1.LogInformation("This is a smart logger.");

                // ensuring that the entry is processed
                completionTasks.Clear();
                cts.Cancel();
                Assert.Equal(1, completionTasks.Count);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();
            }

            var logFile = (MemoryFileInfo)context.FileProvider.GetFileInfo($@"fallback.log");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            var lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(lines, new[]
            {
                $"info: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is a nice logger.",
                ""
            });

            logFile = (MemoryFileInfo)context.FileProvider.GetFileInfo($@"fallback-{context.GetTimestamp().ToLocalTime():yyyyMMdd}-00.log");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(lines, new[]
            {
                $"info: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is a smart logger.",
                ""
            });
        }
    }
}
