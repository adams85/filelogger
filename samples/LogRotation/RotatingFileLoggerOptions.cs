using System.Linq;
using Karambolo.Extensions.Logging.File;

namespace LogRotation
{
    public class RotatingLogFileOptions : LogFileOptions, ILogFileSettings
    {
        public RotatingLogFileOptions() { }

        public RotatingLogFileOptions(RotatingLogFileOptions other) : base(other)
        {
            MaxFiles = other.MaxFiles;
        }

        public int? MaxFiles { get; set; }
    }

    public class RotatingFileLoggerOptions : FileLoggerOptions, IFileLoggerSettings
    {
        public RotatingFileLoggerOptions() { }

        public RotatingFileLoggerOptions(RotatingFileLoggerOptions other) : base(other)
        {
            MaxFiles = other.MaxFiles;

            base.Files = null;
            if (other.Files != null)
                Files = other.Files.Select(file => new RotatingLogFileOptions(file)).ToArray();
        }

        public new RotatingLogFileOptions[] Files { get; set; }
        ILogFileSettings[] IFileLoggerSettings.Files => Files;

        public int? MaxFiles { get; set; }
    }
}
