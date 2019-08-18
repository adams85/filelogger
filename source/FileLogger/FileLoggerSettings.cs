using System.Linq;

namespace Karambolo.Extensions.Logging.File
{
    public interface IFileLoggerSettings : ILogFileSettingsBase
    {
        IFileAppender FileAppender { get; }
        string BasePath { get; }

        ILogFileSettings[] Files { get; }

        IFileLoggerSettings Freeze();
    }

    public class FileLoggerOptions : LogFileSettingsBase, IFileLoggerSettings
    {
        private bool _isFrozen;

        public FileLoggerOptions() { }

        protected FileLoggerOptions(FileLoggerOptions other) : base(other)
        {
            FileAppender = other.FileAppender;
            BasePath = other.BasePath;

            if (other.Files != null)
                Files = other.Files.Select(file => new LogFileOptions(file)).ToArray();
        }

        public IFileAppender FileAppender { get; set; }

        public string RootPath
        {
            get => (FileAppender as PhysicalFileAppender)?.FileProvider.Root;
            set => FileAppender = new PhysicalFileAppender(value);
        }

        public string BasePath { get; set; }

        public LogFileOptions[] Files { get; set; }

        ILogFileSettings[] IFileLoggerSettings.Files => Files;

        IFileLoggerSettings IFileLoggerSettings.Freeze()
        {
            return _isFrozen ? this : new FileLoggerOptions(this) { _isFrozen = true };
        }
    }
}
