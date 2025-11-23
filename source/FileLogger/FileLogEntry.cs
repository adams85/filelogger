using System;

namespace Karambolo.Extensions.Logging.File;

public readonly struct FileLogEntry
{
    public FileLogEntry(object data, DateTimeOffset timestamp)
    {
        Data = data;
        Timestamp = timestamp;
    }

    public object Data { get; }
    public string Text { get => Data?.ToString() ?? string.Empty; }
    public DateTimeOffset Timestamp { get; }
}
