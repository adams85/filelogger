using System;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Karambolo.Extensions.Logging.File.Json;
using Karambolo.Extensions.Logging.File.Test.Helpers;
using Karambolo.Extensions.Logging.File.Test.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Karambolo.Extensions.Logging.File.Test;

public class JsonLoggingTest
{
    [Fact]
    public async Task CanUseCustomFormatting_JsonLines()
    {
        const string logsDirName = "Logs";

        var fileProvider = new MemoryFileProvider();

        var context = new TestFileLoggerContext(CancellationToken.None, completionTimeout: Timeout.InfiniteTimeSpan);

        context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddJsonFile(context, JsonFileLogFormatOptions.ForJsonLines(JavaScriptEncoder.UnsafeRelaxedJsonEscaping), configure: o =>
        {
            o.FileAppender = new MemoryFileAppender(fileProvider);
            o.BasePath = logsDirName;
            o.Files =
            [
                new LogFileOptions
                {
                    Path = "test.log",
                },
            ];
        }));

#if NETCOREAPP3_1_OR_GREATER
        await
#endif
        using (ServiceProvider sp = services.BuildServiceProvider())
        {
            ILogger<LoggingTest> logger = sp.GetService<ILogger<LoggingTest>>();

            logger.LogInformation("This is a nice logger.");

            logger.LogWarning(1, "This is a smart logger.");
        }

        var logFile = (MemoryFileInfo)fileProvider.GetFileInfo($"{logsDirName}/test.log");
        Assert.True(logFile.Exists && !logFile.IsDirectory);

        string[] lines = logFile.ReadAllText(out Encoding encoding).Split([Environment.NewLine], StringSplitOptions.None);
        Assert.Equal(Encoding.UTF8, encoding);
        Assert.Equal(new[]
        {
            "{\"Timestamp\":\"2017-01-01T01:00:00.0000000+01:00\",\"EventId\":0,\"LogLevel\":\"Information\",\"Category\":\"Karambolo.Extensions.Logging.File.Test.LoggingTest\",\"Message\":\"This is a nice logger.\",\"State\":{\"{OriginalFormat}\":\"This is a nice logger.\"}}",
            "{\"Timestamp\":\"2017-01-01T01:00:00.0000000+01:00\",\"EventId\":1,\"LogLevel\":\"Warning\",\"Category\":\"Karambolo.Extensions.Logging.File.Test.LoggingTest\",\"Message\":\"This is a smart logger.\",\"State\":{\"{OriginalFormat}\":\"This is a smart logger.\"}}",
            "",
        }, lines);
    }

    [Fact]
    public async Task ShouldNotLogMessageTwice()
    {
        const string logsDirName = "Logs";

        var fileProvider = new MemoryFileProvider();

        var context = new TestFileLoggerContext(CancellationToken.None, completionTimeout: Timeout.InfiniteTimeSpan);

        context.SetTimestamp(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var services = new ServiceCollection();
        var formatOptions = new JsonFileLogFormatOptions
        {
            JsonWriterOptions = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
        };
        services.AddLogging(b => b.AddJsonFile(context, formatOptions, configure: o =>
        {
            o.FileAppender = new MemoryFileAppender(fileProvider);
            o.BasePath = logsDirName;
            o.Files =
            [
                new LogFileOptions
                {
                    Path = "test.log",
                },
            ];
        }));

        var ex = new Exception();

#if NETCOREAPP3_1_OR_GREATER
        await
#endif
        using (ServiceProvider sp = services.BuildServiceProvider())
        {
            ILogger<LoggingTest> logger = sp.GetService<ILogger<LoggingTest>>();

            logger.LogInformation("This is a nice logger.");

            logger.Log(LogLevel.Error, 1, "Some error message", ex, delegate { return "Some formatted error message"; });
        }

        var logFile = (MemoryFileInfo)fileProvider.GetFileInfo($"{logsDirName}/test.log");
        Assert.True(logFile.Exists && !logFile.IsDirectory);

        string[] lines = logFile.ReadAllText(out Encoding encoding).Split([Environment.NewLine], StringSplitOptions.None);
        Assert.Equal(Encoding.UTF8, encoding);
        Assert.Equal(new[]
        {
            "{",
            "  \"Timestamp\": \"2017-01-01T01:00:00.0000000+01:00\",",
            "  \"EventId\": 0,",
            "  \"LogLevel\": \"Information\",",
            "  \"Category\": \"Karambolo.Extensions.Logging.File.Test.LoggingTest\",",
            "  \"Message\": \"This is a nice logger.\",",
            "  \"State\": {",
            "    \"{OriginalFormat}\": \"This is a nice logger.\"",
            "  }",
            "},",
            "{",
            "  \"Timestamp\": \"2017-01-01T01:00:00.0000000+01:00\",",
            "  \"EventId\": 1,",
            "  \"LogLevel\": \"Error\",",
            "  \"Category\": \"Karambolo.Extensions.Logging.File.Test.LoggingTest\",",
            "  \"Message\": \"Some formatted error message\",",
            "  \"Exception\": \"System.Exception: Exception of type 'System.Exception' was thrown.\",",
            "  \"State\": {",
            "    \"Message\": \"Some error message\"",
            "  }",
            "},",
            "",
        }, lines);
    }
}
