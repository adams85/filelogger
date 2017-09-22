using System;

namespace Karambolo.Extensions.Logging.File
{
    public class FileLogEntry
    {
        public string Text { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }
}
