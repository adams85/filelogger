using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Karambolo.Extensions.Logging.File.Test.Helpers;
using Karambolo.Extensions.Logging.File.Test.Mocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Karambolo.Extensions.Logging.File.Test;

public class SettingsTest
{
    [Fact]
    public void ParsingOptions()
    {
        string configJson =
            $$"""
            {
                "{{FileLoggerProvider.Alias}}": {
                    "{{nameof(FileLoggerOptions.RootPath)}}": "{{Path.DirectorySeparatorChar.ToString().Replace(@"\", @"\\")}}",
                    "{{nameof(FileLoggerOptions.BasePath)}}": "Logs",
                    "{{nameof(FileLoggerOptions.FileAccessMode)}}": "{{LogFileAccessMode.OpenTemporarily}}",
                    "{{nameof(FileLoggerOptions.FileEncodingName)}}": "utf-8",
                    "{{nameof(FileLoggerOptions.Files)}}": [
                    {
                        "{{nameof(LogFileOptions.Path)}}": "logger.log",
                        "{{nameof(LogFileOptions.MinLevel)}}": {
                            "Karambolo.Extensions.Logging.File": "{{LogLevel.Warning}}",
                            "{{LogFileOptions.DefaultCategoryName}}": "{{LogLevel.None}}",
                        },
                    },
                    {
                        "{{nameof(LogFileOptions.Path)}}": "test.log",
                        "{{nameof(LogFileOptions.MinLevel)}}": {
                            "Karambolo.Extensions.Logging.File.Test": "{{LogLevel.Debug}}",
                            "{{LogFileOptions.DefaultCategoryName}}": "{{LogLevel.None}}",
                        },
                    }],
                    "{{nameof(FileLoggerOptions.DateFormat)}}": "yyyyMMdd",
                    "{{nameof(FileLoggerOptions.CounterFormat)}}": "000",
                    "{{nameof(FileLoggerOptions.MaxFileSize)}}": 10,
                    "{{nameof(FileLoggerOptions.TextBuilderType)}}": "{{typeof(CustomLogEntryTextBuilder).AssemblyQualifiedName}}",
                    "{{nameof(FileLoggerOptions.IncludeScopes)}}": true,
                    "{{nameof(FileLoggerOptions.MaxQueueSize)}}": 100,
                }
            }
            """;

        var fileProvider = new MemoryFileProvider();
        fileProvider.CreateFile("config.json", configJson);

        var cb = new ConfigurationBuilder();
        cb.AddJsonFile(fileProvider, "config.json", optional: false, reloadOnChange: false);
        IConfigurationRoot config = cb.Build();

        var services = new ServiceCollection();
        services.AddLogging(lb =>
        {
            lb.AddConfiguration(config);
            lb.AddFile();
        });
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        IFileLoggerSettings settings = serviceProvider.GetRequiredService<IOptions<FileLoggerOptions>>().Value;

        Assert.True(settings.FileAppender is PhysicalFileAppender);
        Assert.Equal(Path.GetPathRoot(Environment.CurrentDirectory), ((PhysicalFileAppender)settings.FileAppender).FileProvider.Root);
        Assert.Equal("Logs", settings.BasePath);
        Assert.Equal(LogFileAccessMode.OpenTemporarily, settings.FileAccessMode);
        Assert.Equal(Encoding.UTF8, settings.FileEncoding);

        Assert.NotNull(settings.Files);
        Assert.Equal(2, settings.Files.Length);

        ILogFileSettings? fileSettings = Array.Find(settings.Files, f => f.Path == "logger.log");
        Assert.NotNull(fileSettings);
        Assert.Equal(LogLevel.None, fileSettings.GetMinLevel(typeof(string).ToString()));
        Assert.Equal(LogLevel.Warning, fileSettings.GetMinLevel(typeof(FileLogger).ToString()));
        Assert.Equal(LogLevel.Warning, fileSettings.GetMinLevel(typeof(SettingsTest).ToString()));

        fileSettings = Array.Find(settings.Files, f => f.Path == "test.log");
        Assert.NotNull(fileSettings);
        Assert.Equal(LogLevel.None, fileSettings.GetMinLevel(typeof(string).ToString()));
        Assert.Equal(LogLevel.None, fileSettings.GetMinLevel(typeof(FileLogger).ToString()));
        Assert.Equal(LogLevel.Debug, fileSettings.GetMinLevel(typeof(SettingsTest).ToString()));

        Assert.Equal("yyyyMMdd", settings.DateFormat);
        Assert.Equal("000", settings.CounterFormat);
        Assert.Equal(10, settings.MaxFileSize);
        Assert.NotNull(settings.TextBuilder);
        Assert.Equal(typeof(CustomLogEntryTextBuilder), settings.TextBuilder.GetType());
        Assert.True(settings.IncludeScopes);
        Assert.Equal(100, settings.MaxQueueSize);
    }

    [Fact]
    public async Task ReloadOptionsSettings()
    {
        string configJson =
            $$"""
            {
                "{{FileLoggerProvider.Alias}}": {
                    "{{nameof(FileLoggerOptions.IncludeScopes)}}" : true,
                    "{{nameof(FileLoggerOptions.Files)}}": [
                    {
                        "{{nameof(LogFileOptions.Path)}}": "test.log",
                    }],
                    "{{nameof(LoggerFilterRule.LogLevel)}}": { 
                        "{{LogFileOptions.DefaultCategoryName}}": "{{LogLevel.Trace}}" 
                    }
                }
            }
            """;

        var fileProvider = new MemoryFileProvider();
        fileProvider.CreateFile("config.json", configJson, Encoding.UTF8);

        var cb = new ConfigurationBuilder();
        cb.AddJsonFile(fileProvider, "config.json", optional: false, reloadOnChange: true);
        IConfigurationRoot config = cb.Build();

        var completeCts = new CancellationTokenSource();
        var context = new TestFileLoggerContext(completeCts.Token, completionTimeout: Timeout.InfiniteTimeSpan);
        context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(b =>
        {
            b.AddConfiguration(config);
            b.AddFile(context);
        });

        var fileAppender = new MemoryFileAppender(fileProvider);
        services.Configure<FileLoggerOptions>(o => o.FileAppender ??= fileAppender);

        FileLoggerProvider[] providers;

        using (ServiceProvider sp = services.BuildServiceProvider())
        {
            providers = context.GetProviders(sp).ToArray();
            Assert.Single(providers);

            var resetTasks = new List<Task>();
            foreach (FileLoggerProvider provider in providers)
                provider.Reset += (s, e) => resetTasks.Add(e);

            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            ILogger<SettingsTest> logger1 = loggerFactory.CreateLogger<SettingsTest>();

            using (logger1.BeginScope("SCOPE"))
            {
                logger1.LogTrace("This is a nice logger.");

                using (logger1.BeginScope("NESTED SCOPE"))
                {
                    logger1.LogInformation("This is a smart logger.");

                    // changing switch and scopes inclusion
                    configJson =
                        $$"""
                        {
                            "{{FileLoggerProvider.Alias}}": {
                                "{{nameof(FileLoggerOptions.Files)}}": [
                                {
                                    "{{nameof(LogFileOptions.Path)}}": "test.log",
                                }],
                                "{{nameof(LoggerFilterRule.LogLevel)}}": { 
                                    "{{LogFileOptions.DefaultCategoryName}}": "{{LogLevel.Information}}" 
                                }
                            }
                        }
                        """;

                    Assert.Empty(resetTasks);
                    fileProvider.WriteContent("config.json", configJson);

                    // reload is triggered twice due to a bug in the framework (https://github.com/aspnet/Logging/issues/874)
                    Assert.Equal(1 * 2, resetTasks.Count);

                    // ensuring that reset has been finished and the new settings are effective
                    await Task.WhenAll(resetTasks);

                    logger1 = loggerFactory.CreateLogger<SettingsTest>();

                    logger1.LogInformation("This one shouldn't include scopes.");
                    logger1.LogTrace("This one shouldn't be included at all.");
                }
            }

            completeCts.Cancel();

            // ensuring that all entries are processed
            await context.GetCompletion(sp);
            Assert.True(providers.All(provider => provider.Completion.IsCompleted));
        }

        var logFile = (MemoryFileInfo)fileProvider.GetFileInfo("test.log");
        Assert.True(logFile.Exists && !logFile.IsDirectory);

        string[] lines = logFile.ReadAllText(out Encoding encoding).Split([Environment.NewLine], StringSplitOptions.None);
        Assert.Equal(Encoding.UTF8, encoding);
        Assert.Equal(new[]
        {
            $"trce: {typeof(SettingsTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
            $"      => SCOPE",
            $"      This is a nice logger.",
            $"info: {typeof(SettingsTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
            $"      => SCOPE => NESTED SCOPE",
            $"      This is a smart logger.",
            $"info: {typeof(SettingsTest).FullName}[0] @ {context.GetTimestamp().ToLocalTime():o}",
            $"      This one shouldn't include scopes.",
            ""
        }, lines);
    }

    [ProviderAlias(Alias)]
    private class OtherFileLoggerProvider : FileLoggerProvider
    {
        public new const string Alias = "OtherFile";

        public OtherFileLoggerProvider(FileLoggerContext context, IOptionsMonitor<FileLoggerOptions> options, string optionsName)
            : base(context, options, optionsName) { }
    }

    [Fact]
    public async Task ReloadOptionsSettingsMultipleProviders()
    {
        var fileProvider = new MemoryFileProvider();
        var fileAppender = new MemoryFileAppender(fileProvider);

        const string defaultProviderFilePath = "one.log";
        const string otherProviderFilePath = "other.log";

        static string BuildConfigJson(LogLevel defaultProviderLevel, LogLevel otherProviderLevel)
        {
            using var stream = new MemoryStream();
            var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject(); // start root object

            // Global filters
            writer.WritePropertyName(nameof(LoggerFilterRule.LogLevel));
            writer.WriteStartObject();
            writer.WriteString(LogFileOptions.DefaultCategoryName, LogLevel.None.ToString());
            writer.WriteEndObject();

            // Default provider options
            writer.WritePropertyName(FileLoggerProvider.Alias);
            writer.WriteStartObject(); // start options object

            writer.WritePropertyName(nameof(LoggerFilterRule.LogLevel));
            writer.WriteStartObject();
            writer.WriteString(LogFileOptions.DefaultCategoryName, defaultProviderLevel.ToString());
            writer.WriteEndObject();

            writer.WritePropertyName(nameof(FileLoggerOptions.Files));
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString(nameof(LogFileOptions.Path), defaultProviderFilePath);
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteEndObject(); // end options object

            // Other provider options
            writer.WritePropertyName(OtherFileLoggerProvider.Alias);
            writer.WriteStartObject(); // start options object

            writer.WritePropertyName(nameof(LoggerFilterRule.LogLevel));
            writer.WriteStartObject();
            writer.WriteString(LogFileOptions.DefaultCategoryName, otherProviderLevel.ToString());
            writer.WriteEndObject();

            writer.WritePropertyName(nameof(FileLoggerOptions.Files));
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString(nameof(LogFileOptions.Path), otherProviderFilePath);
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteEndObject(); // end options object

            writer.WriteEndObject(); // end root object

            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        string configJson = BuildConfigJson(LogLevel.Warning, LogLevel.Information);

        fileProvider.CreateFile("config.json", configJson);

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddJsonFile(fileProvider, "config.json", optional: false, reloadOnChange: true)
            .Build();

        var context = new TestFileLoggerContext(completeToken: default, completionTimeout: Timeout.InfiniteTimeSpan);

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(lb =>
        {
            lb.AddConfiguration(config);
            lb.AddFile(context, o => o.FileAppender ??= fileAppender);
            lb.AddFile<OtherFileLoggerProvider>(context, o => o.FileAppender ??= fileAppender);
        });

        FileLoggerProvider[] providers;

        using (ServiceProvider sp = services.BuildServiceProvider())
        {
            providers = context.GetProviders(sp).ToArray();
            Assert.Equal(2, providers.Length);

            var resetTasks = new List<Task>();
            foreach (FileLoggerProvider provider in providers)
                provider.Reset += (s, e) => resetTasks.Add(e);

            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            ILogger logger = loggerFactory.CreateLogger("X");

            logger.LogInformation("This is an info.");
            logger.LogWarning("This is a warning.");

            configJson = BuildConfigJson(LogLevel.Information, LogLevel.Warning);

            Assert.Empty(resetTasks);
            fileProvider.WriteContent("config.json", configJson);

            // reload is triggered twice due to a bug in the framework (https://github.com/aspnet/Logging/issues/874)
            Assert.Equal(2 * 2, resetTasks.Count);

            // ensuring that reset has been finished and the new settings are effective
            await Task.WhenAll(resetTasks);

            logger.LogInformation("This is another info.");
            logger.LogWarning("This is another warning.");
        }

        Assert.True(providers.All(provider => provider.Completion.IsCompleted));

        var logFile = (MemoryFileInfo)fileProvider.GetFileInfo(defaultProviderFilePath);
        Assert.True(logFile.Exists && !logFile.IsDirectory);

        string[] lines = logFile.ReadAllText(out Encoding encoding).Split([Environment.NewLine], StringSplitOptions.None);
        Assert.Equal(Encoding.UTF8, encoding);
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

        logFile = (MemoryFileInfo)fileProvider.GetFileInfo(otherProviderFilePath);
        Assert.True(logFile.Exists && !logFile.IsDirectory);

        lines = logFile.ReadAllText(out encoding).Split([Environment.NewLine], StringSplitOptions.None);
        Assert.Equal(Encoding.UTF8, encoding);
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

    [Fact]
    public void Issue39_GenericAddJsonFileAppliesUserConfigurationToNamedOptions()
    {
        const string optionsName = "MyJsonProvider";
        const string basePath = "some-sentinel-base-path";

        AssertUserConfigurationAppliesToNamedOptions(
            lb => lb.AddJsonFile<OtherFileLoggerProvider>(configure: o => o.BasePath = basePath, optionsName: optionsName),
            optionsName, basePath);
    }

    [Fact]
    public void Issue39_GenericAddJsonFileWithOptionsAppliesUserConfigurationToNamedOptions()
    {
        const string optionsName = "MyJsonProvider";
        const string basePath = "some-sentinel-base-path";

        AssertUserConfigurationAppliesToNamedOptions(
            lb => lb.AddJsonFile<OtherFileLoggerProvider, FileLoggerOptions>(configure: o => o.BasePath = basePath, optionsName: optionsName),
            optionsName, basePath);
    }

    [Fact]
    public void Issue39_GenericAddJsonFileWithBindOptionsAppliesUserConfigurationToNamedOptions()
    {
        const string optionsName = "MyJsonProvider";
        const string basePath = "some-sentinel-base-path";

        AssertUserConfigurationAppliesToNamedOptions(
            lb => lb.AddJsonFile<OtherFileLoggerProvider, FileLoggerOptions>(bindOptions: (o, c) => { }, configure: o => o.BasePath = basePath, optionsName: optionsName),
            optionsName, basePath);
    }

    private static void AssertUserConfigurationAppliesToNamedOptions(
        Action<ILoggingBuilder> configureLogging, string optionsName, string expectedBasePath)
    {
        var services = new ServiceCollection();
        services.AddLogging(configureLogging);

        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        // The provider resolves its settings via IOptionsMonitor.Get(optionsName), so the user
        // configuration callback must be applied to the named options instance, not Options.DefaultName.
        IOptionsMonitor<FileLoggerOptions> optionsMonitor =
            serviceProvider.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>();

        Assert.Equal(expectedBasePath, optionsMonitor.Get(optionsName).BasePath);
        Assert.NotEqual(expectedBasePath, optionsMonitor.Get(Options.DefaultName).BasePath);
    }
}
