using System;
using System.Linq;
using System.Reflection;

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

        public FileLoggerOptions(FileLoggerOptions other) : base(other)
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
            if (_isFrozen)
                return this;

            ConstructorInfo copyCtor;
            Type type = GetType();

            FileLoggerOptions clone =
                type != typeof(FileLoggerOptions) &&
                (copyCtor = type.GetTypeInfo().DeclaredConstructors.FirstOrDefault(ci =>
                {
                    ParameterInfo[] parameters = ci.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == GetType();
                })) != null ?
                (FileLoggerOptions)copyCtor.Invoke(new[] { this }) :
                new FileLoggerOptions(this);

            clone._isFrozen = true;
            return clone;
        }
    }
}
