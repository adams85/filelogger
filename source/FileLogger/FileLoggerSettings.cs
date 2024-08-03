using System;
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

        public FileLoggerOptions(FileLoggerOptions other) : base(other)
        {
            FileAppender = other.FileAppender;
            BasePath = other.BasePath;

            if (other.Files != null)
            {
                var files = (LogFileOptions[])other._files.Clone();
                for (var i = 0; i < files.Length; i++)
                    files[i] = files[i].Clone();
                _files = files;
            }
        }

        public IFileAppender FileAppender { get; set; }

        public string RootPath
        {
            get => (FileAppender as PhysicalFileAppender)?.FileProvider.Root;
            set => FileAppender = new PhysicalFileAppender(value);
        }

        public string BasePath { get; set; }

        private LogFileOptions[] _files;
        public LogFileOptions[] Files
        {
            get => _files;
            set => SetFiles(value);
        }

        protected virtual void SetFiles(LogFileOptions[] value)
        {
            _files = value;
        }

        ILogFileSettings[] IFileLoggerSettings.Files => _files;

        IFileLoggerSettings IFileLoggerSettings.Freeze()
        {
            if (_isFrozen)
                return this;

            FileLoggerOptions clone = Clone();
            clone._isFrozen = true;
            return clone;
        }

        protected virtual FileLoggerOptions Clone()
        {
            if (GetType() != typeof(FileLoggerOptions))
            {
                throw new InvalidOperationException($"Inheritors of {nameof(FileLoggerOptions)} must override the {nameof(Clone)} method and provide an implementation that creates a clone of the subclass instance.");
            }

            return new FileLoggerOptions(this);
        }

#if NET8_0_OR_GREATER
        // NOTE: Unfortunately, it seems that there is no way to ignore properties from configuration binding at the moment,
        // so using source generated configuration binding would result in a bunch of warnings.
        // We can work around the issue by defining a wrapper class for configuration binding.
        public abstract class BindingWrapper<TOptions> : LogFileSettingsBase.BindingWrapperBase<TOptions>
            where TOptions : FileLoggerOptions
        {
            protected BindingWrapper(TOptions options) : base(options) { }

            public string BasePath { set => Options.BasePath = value; }
        }

        internal sealed class BindingWrapper : BindingWrapper<FileLoggerOptions>
        {
            public BindingWrapper() : this(new FileLoggerOptions()) { }

            public BindingWrapper(FileLoggerOptions options) : base(options) { }

            private LogFileOptions.BindingWrapper[] _files;
            public LogFileOptions.BindingWrapper[] Files
            {
                get => _files;
                set => Options.Files = (_files = value)?.Select(file => file.Options).ToArray();
            }
        }
#endif
    }
}
