using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File
{
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
        int? MaxFileSize { get; }
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
        private static ConcurrentDictionary<Type, IFileLogEntryTextBuilder> s_textBuilderCache;
        private static ConcurrentDictionary<Type, IFileLogEntryTextBuilder> TextBuilderCache =>
            LazyInitializer.EnsureInitialized(ref s_textBuilderCache, () => new ConcurrentDictionary<Type, IFileLogEntryTextBuilder>());

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

        public int? MaxFileSize { get; set; }

        public IFileLogEntryTextBuilder TextBuilder { get; set; }

        public string TextBuilderType
        {
            get => TextBuilder?.GetType().AssemblyQualifiedName;
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

                    if (!type.GetTypeInfo().DeclaredConstructors.Any(ci => ci.GetParameters().Length == 0))
                        throw new ArgumentException("Type must provide a parameterless constructor.", nameof(value));

                    return (IFileLogEntryTextBuilder)Activator.CreateInstance(type);
                });
            }
        }

        public bool? IncludeScopes { get; set; }

        public int? MaxQueueSize { get; set; }

        public LogFilePathPlaceholderResolver PathPlaceholderResolver { get; set; }
    }

    public class LogFileOptions : LogFileSettingsBase, ILogFileSettings
    {
        protected internal const string DefaultCategoryName = "Default";

        protected internal static IEnumerable<string> GetPrefixes(string categoryName, bool returnDefault = true)
        {
            while (!string.IsNullOrEmpty(categoryName))
            {
                yield return categoryName;

                var index = categoryName.LastIndexOf('.');
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

            if (other.MinLevel != null)
                MinLevel = new Dictionary<string, LogLevel>(other.MinLevel);
        }

        public string Path { get; set; }

        public Dictionary<string, LogLevel> MinLevel { get; set; }

        LogLevel ILogFileSettings.GetMinLevel(string categoryName)
        {
            if (MinLevel == null)
                return LogLevel.Trace;

            foreach (var prefix in GetPrefixes(categoryName))
                if (MinLevel.TryGetValue(prefix, out LogLevel level))
                    return level;

            return LogLevel.None;
        }
    }
}
