using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Karambolo.Extensions.Logging.File.Test.MockObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
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
                [$"{nameof(FileLoggerOptions.RootPath)}"] = Path.DirectorySeparatorChar.ToString(),
                [$"{nameof(FileLoggerOptions.BasePath)}"] = "Logs",
                [$"{nameof(FileLoggerOptions.EnsureBasePath)}"] = "true",
                [$"{nameof(FileLoggerOptions.FileEncodingName)}"] = "UTF-8",
                [$"{nameof(FileLoggerOptions.FallbackFileName)}"] = "other.log",
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

            Assert.True(settings.FileAppender is PhysicalFileAppender);
            Assert.Equal(Path.GetPathRoot(Environment.CurrentDirectory), ((PhysicalFileAppender)settings.FileAppender).FileProvider.Root);
            Assert.Equal("Logs", settings.BasePath);
            Assert.True(settings.EnsureBasePath);
            Assert.Equal(Encoding.UTF8, settings.FileEncoding);
            Assert.Equal("other.log", settings.FallbackFileName);
            Assert.Equal("test.log", settings.MapToFileName(typeof(SettingsTest).FullName, "default.log"));
            Assert.Equal("logger.log", settings.MapToFileName(typeof(FileLogger).FullName, "default.log"));
            Assert.Equal("default.log", settings.MapToFileName("X.Y", "default.log"));
            Assert.Equal("yyyyMMdd", settings.DateFormat);
            Assert.Equal("000", settings.CounterFormat);
            Assert.Equal(10, settings.MaxFileSize);
            Assert.True(settings.TextBuilder is CustomLogEntryTextBuilder);
            Assert.True(settings.TryGetSwitch(typeof(SettingsTest).Namespace, out LogLevel logLevel));
            Assert.Equal(LogLevel.Information, logLevel);
            Assert.True(settings.TryGetSwitch(typeof(FileLogger).Namespace, out logLevel));
            Assert.Equal(LogLevel.Warning, logLevel);
            Assert.False(settings.TryGetSwitch("X.Y", out logLevel));
            Assert.True(settings.IncludeScopes);
            Assert.Equal(100, settings.MaxQueueSize);
        }

        [Fact]
        public void ParsingOptions()
        {
            var configJson =
$@"{{ 
    '{nameof(FileLoggerOptions.RootPath)}': '{Path.DirectorySeparatorChar.ToString().Replace(@"\", @"\\")}',
    '{nameof(FileLoggerOptions.BasePath)}': 'Logs',
    '{nameof(FileLoggerOptions.EnsureBasePath)}': true,
    '{nameof(FileLoggerOptions.FileEncodingName)}': 'utf-8',
    '{nameof(FileLoggerOptions.FallbackFileName)}': 'other.log',
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

            Assert.True(options.FileAppender is PhysicalFileAppender);
            Assert.Equal(Path.GetPathRoot(Environment.CurrentDirectory), ((PhysicalFileAppender)options.FileAppender).FileProvider.Root);
            Assert.Equal("Logs", options.BasePath);
            Assert.True(options.EnsureBasePath);
            Assert.Equal(Encoding.UTF8, options.FileEncoding);
            Assert.Equal("other.log", options.FallbackFileName);
            Assert.Equal("test.log", options.MapToFileName(typeof(SettingsTest).FullName, "default.log"));
            Assert.Equal("logger.log", options.MapToFileName(typeof(FileLogger).FullName, "default.log"));
            Assert.Equal("default.log", options.MapToFileName("X.Y", "default.log"));
            Assert.Equal("yyyyMMdd", options.DateFormat);
            Assert.Equal("000", options.CounterFormat);
            Assert.Equal(10, options.MaxFileSize);
            Assert.Equal(typeof(CustomLogEntryTextBuilder), options.TextBuilder.GetType());
            Assert.True(options.IncludeScopes);
            Assert.Equal(100, options.MaxQueueSize);
        }

        [Fact]
        public void ReloadSettings()
        {
            var fileProvider = new MemoryFileProvider();

            var cts = new CancellationTokenSource();

            var settings = new FileLoggerSettings
            {
                FileAppender = new MemoryFileAppender(fileProvider),
                Switches = new Dictionary<string, LogLevel>
                {
                    { FileLoggerSettingsBase.DefaultCategoryName, LogLevel.Information }
                },
                ChangeToken = new CancellationChangeToken(cts.Token)
            };

            var context = new TestFileLoggerContext();

            var completionTasks = new List<Task>();
            context.Complete += (s, e) => completionTasks.Add(e);

            context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var otherFileAppender = new MemoryFileAppender();
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
                Assert.Single(completionTasks);
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

                newCts = new CancellationTokenSource();
                settings.ChangeToken = new CancellationChangeToken(newCts.Token);
                cts.Cancel();
                cts = newCts;
                Assert.Equal(2, completionTasks.Count);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();

                logger1.LogWarning("This goes to another file.");

                // changing file appender and fallback filename
                settings.FileAppender = otherFileAppender;
                settings.FallbackFileName = "test.log";
                settings.FileNameMappings = null;

                newCts = new CancellationTokenSource();
                settings.ChangeToken = new CancellationChangeToken(newCts.Token);
                cts.Cancel();
                cts = newCts;
                Assert.Equal(3, completionTasks.Count);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();

                logger1.LogWarning("This goes to another file provider.");

                // ensuring that the entry is processed
                cts.Cancel();
                Assert.Equal(4, completionTasks.Count);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();
            }

            var logFile = (MemoryFileInfo)fileProvider.GetFileInfo(LoggingTest.FallbackFileName);
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            var lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(new[]
            {
                $"info: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is a nice logger.",
                $"[info]: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"        This is a smart logger.",
                ""
            }, lines);

            logFile = (MemoryFileInfo)fileProvider.GetFileInfo($@"Logs\test.log");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.Unicode, logFile.Encoding);
            Assert.Equal(new[]
            {
                $"[warn]: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"        This goes to another file.",
                ""
            }, lines);

            logFile = (MemoryFileInfo)otherFileAppender.FileProvider.GetFileInfo($@"Logs\test.log");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.Unicode, logFile.Encoding);
            Assert.Equal(new[]
            {
                $"[warn]: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"        This goes to another file provider.",
                ""
            }, lines);
        }

        [Fact]
        public void ReloadConfigurationSettings()
        {
            var configJson =
$@"{{ 
    '{nameof(ConfigurationFileLoggerSettings.MaxFileSize)}' : 10000,
    '{ConfigurationFileLoggerSettings.LogLevelSectionName}': {{
        '{FileLoggerSettingsBase.DefaultCategoryName}': '{LogLevel.Information}',
    }}
}}";

            var fileProvider = new MemoryFileProvider();
            fileProvider.CreateFile("config.json", configJson, Encoding.UTF8);

            var cb = new ConfigurationBuilder();
            cb.AddJsonFile(fileProvider, "config.json", optional: false, reloadOnChange: true);
            var config = cb.Build();

            var fileAppender = new MemoryFileAppender(fileProvider);
            var settings = new ConfigurationFileLoggerSettings(config, o => o.FileAppender = o.FileAppender ?? fileAppender);

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
    '{nameof(ConfigurationFileLoggerSettings.MaxFileSize)}' : 1,
    '{nameof(ConfigurationFileLoggerSettings.CounterFormat)}' : '00',
    '{ConfigurationFileLoggerSettings.LogLevelSectionName}': {{
        '{FileLoggerSettingsBase.DefaultCategoryName}': '{LogLevel.Information}',
    }}
}}";
                fileProvider.WriteContent("config.json", configJson);

                Assert.Single(completionTasks);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();

                logger1.LogInformation("This is a smart logger.");

                // ensuring that the entry is processed
                completionTasks.Clear();
                cts.Cancel();
                Assert.Single(completionTasks);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();
            }

            var logFile = (MemoryFileInfo)fileProvider.GetFileInfo($@"{Path.ChangeExtension(LoggingTest.FallbackFileName, null)}-0{Path.GetExtension(LoggingTest.FallbackFileName)}");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            var lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(new[]
            {
                $"info: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is a nice logger.",
                ""
            }, lines);

            logFile = (MemoryFileInfo)fileProvider.GetFileInfo($@"{Path.ChangeExtension(LoggingTest.FallbackFileName, null)}-{context.GetTimestamp().ToLocalTime():yyyyMMdd}-00{Path.GetExtension(LoggingTest.FallbackFileName)}");
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(new[]
            {
                $"info: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is a smart logger.",
                ""
            }, lines);
        }

        [Fact]
        public void ReloadOptionsSettings()
        {
            var configJson =
$@"{{
    '{FileLoggerProvider.Alias}': {{
        '{nameof(ConfigurationFileLoggerSettings.IncludeScopes)}' : true,
        '{ConfigurationFileLoggerSettings.LogLevelSectionName}': {{
            '{FileLoggerSettingsBase.DefaultCategoryName}': '{LogLevel.Trace}',
        }}
    }}
}}";

            var fileProvider = new MemoryFileProvider();
            fileProvider.CreateFile("config.json", configJson, Encoding.UTF8);

            var cb = new ConfigurationBuilder();
            cb.AddJsonFile(fileProvider, "config.json", optional: false, reloadOnChange: true);
            var config = cb.Build();

            var cts = new CancellationTokenSource();
            var context = new TestFileLoggerContext(cts.Token);

            var completionTasks = new List<Task>();
            context.Complete += (s, e) => completionTasks.Add(e);

            context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging(b =>
            {
                b.AddConfiguration(config);
                b.AddFile(context);
            });

            var fileAppender = new MemoryFileAppender(fileProvider);
            services.Configure<FileLoggerOptions>(o => o.FileAppender = o.FileAppender ?? fileAppender);

            using (var serviceProvider = services.BuildServiceProvider())
            {
                var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                var logger1 = loggerFactory.CreateLogger<LoggingTest>();

                using (logger1.BeginScope("SCOPE"))
                {
                    logger1.LogTrace("This is a nice logger.");

                    using (logger1.BeginScope("NESTED SCOPE"))
                    {
                        logger1.LogInformation("This is a smart logger.");

                        // changing switch and scopes inclusion
                        configJson =
$@"{{
    '{FileLoggerProvider.Alias}': {{
        '{ConfigurationFileLoggerSettings.LogLevelSectionName}': {{
            '{FileLoggerSettingsBase.DefaultCategoryName}': '{LogLevel.Information}',
        }}
    }}
}}";
                        fileProvider.WriteContent("config.json", configJson);

                        // reload is triggered twice due to a bug in the framework (https://github.com/aspnet/Logging/issues/874)
                        Assert.Equal(1 * 2, completionTasks.Count);
                        Task.WhenAll(completionTasks).GetAwaiter().GetResult();

                        logger1 = loggerFactory.CreateLogger<LoggingTest>();

                        logger1.LogInformation("This one shouldn't include scopes.");
                        logger1.LogTrace("This one shouldn't be included at all.");
                    }
                }

                // ensuring that the entry is processed
                completionTasks.Clear();
                cts.Cancel();
                Assert.Single(completionTasks);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();
            }

            var logFile = (MemoryFileInfo)fileProvider.GetFileInfo(LoggingTest.FallbackFileName);
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            var lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(new[]
            {
                $"trce: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      => SCOPE",
                $"      This is a nice logger.",
                $"info: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      => SCOPE => NESTED SCOPE",
                $"      This is a smart logger.",
                $"info: {typeof(LoggingTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This one shouldn't include scopes.",
                ""
            }, lines);
        }

        [ProviderAlias(Alias)]
        class OtherFileLoggerProvider : FileLoggerProvider
        {
            public new const string Alias = "OtherFile";

            public OtherFileLoggerProvider(IFileLoggerContext context, IOptionsMonitor<FileLoggerOptions> options, string optionsName)
                : base(context, options, optionsName) { }
        }

        [Fact]
        public void ReloadOptionsSettingsMultipleProviders()
        {
            var fileProvider = new MemoryFileProvider();
            var fileAppender = new MemoryFileAppender(fileProvider);

            dynamic settings = new JObject();
            var globalFilters = settings[ConfigurationFileLoggerSettings.LogLevelSectionName] = new JObject();
            globalFilters[FileLoggerSettingsBase.DefaultCategoryName] = LogLevel.None.ToString();

            settings[FileLoggerProvider.Alias] = new JObject();
            var fileFilters = settings[FileLoggerProvider.Alias][ConfigurationFileLoggerSettings.LogLevelSectionName] = new JObject();
            fileFilters[FileLoggerSettingsBase.DefaultCategoryName] = LogLevel.Warning.ToString();

            settings[OtherFileLoggerProvider.Alias] = new JObject();
            settings[OtherFileLoggerProvider.Alias][nameof(FileLoggerOptions.FallbackFileName)] = "fallback.log";
            var otherFileFilters = settings[OtherFileLoggerProvider.Alias][ConfigurationFileLoggerSettings.LogLevelSectionName] = new JObject();
            otherFileFilters[FileLoggerSettingsBase.DefaultCategoryName] = LogLevel.Information.ToString();
            var settingsJson = ((JObject)settings).ToString();

            fileProvider.CreateFile("config.json", settingsJson);

            var config = new ConfigurationBuilder()
                .AddJsonFile(fileProvider, "config.json", optional: false, reloadOnChange: true)
                .Build();

            var context = new TestFileLoggerContext();

            var completionTasks = new List<Task>();
            context.Complete += (s, e) => completionTasks.Add(e);

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging(lb =>
            {
                lb.AddConfiguration(config);
                lb.AddFile(context, o => o.FileAppender = o.FileAppender ?? fileAppender);
                lb.AddFile<OtherFileLoggerProvider>(context, o => o.FileAppender = o.FileAppender ?? fileAppender);
            });

            using (var sp = services.BuildServiceProvider())
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

                var logger = loggerFactory.CreateLogger("X");

                logger.LogInformation("This is an info.");
                logger.LogWarning("This is a warning.");

                fileFilters[FileLoggerSettingsBase.DefaultCategoryName] = LogLevel.Information.ToString();
                otherFileFilters[FileLoggerSettingsBase.DefaultCategoryName] = LogLevel.Warning.ToString();
                settingsJson = ((JObject)settings).ToString();
                fileProvider.WriteContent("config.json", settingsJson);

                // reload is triggered twice due to a bug in the framework (https://github.com/aspnet/Logging/issues/874)
                Assert.Equal(2 * 2, completionTasks.Count);
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();

                logger.LogInformation("This is another info.");
                logger.LogWarning("This is another warning.");
            }

            var logFile = (MemoryFileInfo)fileProvider.GetFileInfo(LoggingTest.FallbackFileName);
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            var lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(new[]
            {
                $"warn: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is a warning.",
                $"info: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is another info.",
                $"warn: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is another warning.",
                ""
            }, lines);

            logFile = (MemoryFileInfo)fileProvider.GetFileInfo((string)settings[OtherFileLoggerProvider.Alias].FallbackFileName);
            Assert.True(logFile.Exists && !logFile.IsDirectory);

            lines = logFile.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(Encoding.UTF8, logFile.Encoding);
            Assert.Equal(new[]
            {
                $"info: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is an info.",
                $"warn: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is a warning.",
                $"warn: X[0] @ {context.GetTimestamp().ToLocalTime():o}",
                $"      This is another warning.",
                ""
            }, lines);
        }
    }
}
