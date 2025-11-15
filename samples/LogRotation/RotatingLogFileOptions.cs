using Karambolo.Extensions.Logging.File;

namespace LogRotation;

public class RotatingLogFileOptions : LogFileOptions
{
    public RotatingLogFileOptions() { }

    public RotatingLogFileOptions(RotatingLogFileOptions other) : base(other)
    {
        MaxFiles = other.MaxFiles;
    }

    public int? MaxFiles { get; set; }

    protected override LogFileOptions Clone() => new RotatingLogFileOptions(this);

    // This wrapper class is for supporting configuration binding source generation.
    // (Only necessary if the application is published as self-contained trimmed or Native AOT.)
    public class BindingWrapper : BindingWrapperBase<RotatingLogFileOptions>
    {
        public BindingWrapper() : this(new RotatingLogFileOptions()) { }

        public BindingWrapper(RotatingLogFileOptions options) : base(options) { }

        public int? MaxFiles
        {
            get => Options.MaxFiles;
            set => Options.MaxFiles = value;
        }
    }
}
