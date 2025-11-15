using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File;

public enum LogFileAccessMode
{
    KeepOpenAndAutoFlush,
    KeepOpen,
    OpenTemporarily,

    Default = KeepOpenAndAutoFlush
}

public interface ILogFileSettingsBase
{
    LogFileAccessMode? FileAccessMode { get; }
    Encoding FileEncoding { get; }
    string DateFormat { get; }
    string CounterFormat { get; }
    long? MaxFileSize { get; }
    IFileLogEntryTextBuilder TextBuilder { get; }
    bool? IncludeScopes { get; }
    int? MaxQueueSize { get; }
    LogFilePathPlaceholderResolver PathPlaceholderResolver { get; }
}

public interface ILogFileSettings : ILogFileSettingsBase
{
    string Path { get; }

    LogLevel GetMinLevel(string categoryName);
}

public abstract class LogFileSettingsBase : ILogFileSettingsBase
{
    private static ConcurrentDictionary<Type, IFileLogEntryTextBuilder> TextBuilderCache
    {
        get => LazyInitializer.EnsureInitialized(ref field, () => new ConcurrentDictionary<Type, IFileLogEntryTextBuilder>());
        set;
    }

    public LogFileSettingsBase() { }

    protected LogFileSettingsBase(LogFileSettingsBase other)
    {
        FileAccessMode = other.FileAccessMode;
        FileEncoding = other.FileEncoding;
        DateFormat = other.DateFormat;
        CounterFormat = other.CounterFormat;
        MaxFileSize = other.MaxFileSize;
        TextBuilder = other.TextBuilder;
        IncludeScopes = other.IncludeScopes;
        MaxQueueSize = other.MaxQueueSize;
        PathPlaceholderResolver = other.PathPlaceholderResolver;
    }

    public LogFileAccessMode? FileAccessMode { get; set; }

    public Encoding FileEncoding { get; set; }

    public string FileEncodingName
    {
        get => FileEncoding?.WebName;
        set => FileEncoding = !string.IsNullOrEmpty(value) ? Encoding.GetEncoding(value) : null;
    }

    public string DateFormat { get; set; }

    public string CounterFormat { get; set; }

    public long? MaxFileSize { get; set; }

    public IFileLogEntryTextBuilder TextBuilder { get; set; }

    public string TextBuilderType
    {
        get => TextBuilder?.GetType().AssemblyQualifiedName;
#if NET5_0_OR_GREATER
        [RequiresUnreferencedCode($"{nameof(TextBuilderType)} is not compatible with trimming. Use the {nameof(TextBuilder)} property instead.")]
#endif
        set
        {
            if (string.IsNullOrEmpty(value))
                TextBuilder = null;

            var type = Type.GetType(value, throwOnError: true);

            // it's important to return the same instance of a given text builder type
            // because FileLogger use the instance in its internal cache (FileGroups) as a part of the key
            TextBuilder = TextBuilderCache.GetOrAdd(type, type =>
            {
                if (!type.GetTypeInfo().ImplementedInterfaces.Contains(typeof(IFileLogEntryTextBuilder)))
                    throw new ArgumentException($"Type must implement the {typeof(IFileLogEntryTextBuilder).Name} interface.", nameof(value));

                ConstructorInfo ctor = type.GetTypeInfo().DeclaredConstructors.FirstOrDefault(ci => ci.GetParameters().Length == 0)
                    ?? throw new ArgumentException("Type must provide a parameterless constructor.", nameof(value));

                return (IFileLogEntryTextBuilder)ctor.Invoke(null);
            });
        }
    }

    public bool? IncludeScopes { get; set; }

    public int? MaxQueueSize { get; set; }

    public LogFilePathPlaceholderResolver PathPlaceholderResolver { get; set; }

#if NET8_0_OR_GREATER
    public abstract class BindingWrapperBase<TOptions>
        where TOptions : LogFileSettingsBase
    {
        public readonly TOptions Options;

        protected BindingWrapperBase(TOptions options)
        {
            Options = options;
        }

        public LogFileAccessMode? FileAccessMode
        {
            get => Options.FileAccessMode;
            set => Options.FileAccessMode = value;
        }

        public string FileEncodingName
        {
            get => Options.FileEncodingName;
            set => Options.FileEncodingName = value;
        }

        public string DateFormat
        {
            get => Options.DateFormat;
            set => Options.DateFormat = value;
        }

        public string CounterFormat
        {
            get => Options.CounterFormat;
            set => Options.CounterFormat = value;
        }

        public long? MaxFileSize
        {
            get => Options.MaxFileSize;
            set => Options.MaxFileSize = value;
        }

        public string TextBuilderType
        {
            get => Options.TextBuilderType;
            [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
                Justification = "For non-trimmed applications, this property must be included in configuration binding. For trimmed applications, the situation is handled by adding a warning to the documentation.")]
            set => Options.TextBuilderType = value;
        }

        public bool? IncludeScopes
        {
            get => Options.IncludeScopes;
            set => Options.IncludeScopes = value;
        }

        public int? MaxQueueSize
        {
            get => Options.MaxQueueSize;
            set => Options.MaxQueueSize = value;
        }
    }
#endif
}

public class LogFileOptions : LogFileSettingsBase, ILogFileSettings
{
    protected internal const string DefaultCategoryName = "Default";

    protected internal static IEnumerable<string> GetPrefixes(string categoryName, bool returnDefault = true)
    {
        while (!string.IsNullOrEmpty(categoryName))
        {
            yield return categoryName;

            int index = categoryName.LastIndexOf('.');
            if (index == -1)
            {
                if (returnDefault)
                    yield return DefaultCategoryName;

                break;
            }

            categoryName = categoryName.Substring(0, index);
        }
    }

    public LogFileOptions() { }

    public LogFileOptions(LogFileOptions other) : base(other)
    {
        Path = other.Path;

        if (other.MinLevel is not null)
            MinLevel = new Dictionary<string, LogLevel>(other.MinLevel);
    }

    public string Path { get; set; }

    public Dictionary<string, LogLevel> MinLevel { get; set; }

    LogLevel ILogFileSettings.GetMinLevel(string categoryName)
    {
        if (MinLevel is null)
            return LogLevel.Trace;

        foreach (string prefix in GetPrefixes(categoryName))
        {
            if (MinLevel.TryGetValue(prefix, out LogLevel level))
                return level;
        }

        return LogLevel.None;
    }

    protected internal virtual LogFileOptions Clone()
    {
        if (GetType() != typeof(LogFileOptions))
        {
            throw new InvalidOperationException($"Inheritors of {nameof(LogFileOptions)} must override the {nameof(Clone)} method and provide an implementation that creates a clone of the subclass instance.");
        }

        return new LogFileOptions(this);
    }

#if NET8_0_OR_GREATER
    // NOTE: Unfortunately, it seems that there is no way to ignore properties from configuration binding at the moment,
    // so using source generated configuration binding would result in a bunch of warnings.
    // We can work around the issue by defining a wrapper class for configuration binding.
    public new abstract class BindingWrapperBase<TOptions> : LogFileSettingsBase.BindingWrapperBase<TOptions>
        where TOptions : LogFileOptions
    {
        protected BindingWrapperBase(TOptions options) : base(options) { }

        public string Path
        {
            get => Options.Path;
            set => Options.Path = value;
        }

        public Dictionary<string, LogLevel> MinLevel
        {
            get => Options.MinLevel;
            set => Options.MinLevel = value;
        }
    }

    internal sealed class BindingWrapper : BindingWrapperBase<LogFileOptions>
    {
        public BindingWrapper() : this(new LogFileOptions()) { }

        public BindingWrapper(LogFileOptions options) : base(options) { }
    }
#endif
}
