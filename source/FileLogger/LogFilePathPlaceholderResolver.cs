namespace Karambolo.Extensions.Logging.File;

public interface ILogFilePathFormatContext
{
    ref readonly FileLogEntry LogEntry { get; }

    string? DateFormat { get; }
    string? CounterFormat { get; }
    int Counter { get; }

    string FormatDate(string? inlineFormat);
    string FormatCounter(string? inlineFormat);
}

public delegate string? LogFilePathPlaceholderResolver(string placeholderName, string? inlineFormat, ILogFilePathFormatContext context);
