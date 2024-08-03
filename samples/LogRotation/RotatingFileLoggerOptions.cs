using System.Linq;
using Karambolo.Extensions.Logging.File;

namespace LogRotation
{
    public class RotatingFileLoggerOptions : FileLoggerOptions
    {
        public RotatingFileLoggerOptions() { }

        public RotatingFileLoggerOptions(RotatingFileLoggerOptions other) : base(other)
        {
            MaxFiles = other.MaxFiles;
        }

        public new RotatingLogFileOptions[] Files
        {
            get => (RotatingLogFileOptions[])base.Files;
            set => base.SetFiles(value);
        }

        protected override void SetFiles(LogFileOptions[] value)
        {
            // NOTE: Reflection-based configuration binding calls the setter of the Files property
            // in the base class even though it's shadowed by this subclass. So, we make it a no-op
            // to prevent the value of Files from being overwritten.
        }

        public int? MaxFiles { get; set; }

        protected override FileLoggerOptions Clone() => new RotatingFileLoggerOptions(this);

        // This wrapper class is for supporting configuration binding source generation.
        // (Only necessary if the application is published as self-contained trimmed or Native AOT.)
        public class BindingWrapper : BindingWrapper<RotatingFileLoggerOptions>
        {
            public BindingWrapper() : this(new RotatingFileLoggerOptions()) { }

            public BindingWrapper(RotatingFileLoggerOptions options) : base(options) { }

            private RotatingLogFileOptions.BindingWrapper[] _files;
            public RotatingLogFileOptions.BindingWrapper[] Files
            {
                get => _files;
                set => Options.Files = (_files = value)?.Select(file => file.Options).ToArray();
            }

            public int? MaxFiles { set => Options.MaxFiles = value; }
        }
    }
}
