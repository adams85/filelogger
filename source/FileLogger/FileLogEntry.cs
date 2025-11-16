using System;

namespace Karambolo.Extensions.Logging.File;

/// <remarks>
/// Properties are initialized after instantiation.
/// </remarks>
public class FileLogEntry
{
    public string Text { get => field!; set; }
    public DateTimeOffset Timestamp { get; set; }
}
