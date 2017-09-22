using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Karambolo.Extensions.Logging.File
{
    public interface IFileLoggerSettingsBase
    {
        string BasePath { get; }
        bool EnsureBasePath { get; }
        Encoding FileEncoding { get; }
        string DateFormat { get; }
        string CounterFormat { get; }
        int? MaxFileSize { get; }
        IFileLogEntryTextBuilder TextBuilder { get; }
        bool IncludeScopes { get; }
        int MaxQueueSize { get; }

        string MapToFileName(string categoryName, string fallbackFileName);
        Func<string, LogLevel, bool> BuildFilter(string categoryName);

        IFileLoggerSettingsBase ToImmutable();
    }

    public interface IFileLoggerSettings : IFileLoggerSettingsBase
    {
        IFileLoggerSettings Reload();
        IChangeToken ChangeToken { get; }
    }

    public class FileNameMapping
    {
        public FileNameMapping() { }

        public FileNameMapping(FileNameMapping mapping)
        {
            Prefix = mapping.Prefix;
            FileName = mapping.FileName;
        }

        public string Prefix { get; set; }
        public string FileName { get; set; }
    }

    public abstract class FileLoggerSettingsBase : IFileLoggerSettingsBase
    {
        internal delegate bool TryGetLogLevel(string categoryName, out LogLevel level);

        public const string DefaultCategoryName = "Default";

        public static IEnumerable<string> GetPrefixes(string categoryName, bool returnDefault = true)
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

        internal static Func<string, LogLevel, bool> BuildFilter(string categoryName, TryGetLogLevel tryGetLogLevel)
        {
            foreach (var prefix in GetPrefixes(categoryName))
                if (tryGetLogLevel(prefix, out LogLevel level))
                    return (c, l) => l >= level;

            return (c, l) => false;
        }

        bool _immutable;

        public string BasePath { get; set; } = string.Empty;
        public bool EnsureBasePath { get; set; }
        public Encoding FileEncoding { get; set; } = Encoding.UTF8;
        public FileNameMapping[] FileNameMappings { get; set; }
        public string DateFormat { get; set; }
        public string CounterFormat { get; set; }
        public int? MaxFileSize { get; set; }
        public IFileLogEntryTextBuilder TextBuilder { get; set; }
        public bool IncludeScopes { get; set; }
        public int MaxQueueSize { get; set; } = 64;

        public virtual string MapToFileName(string categoryName, string fallbackFileName)
        {
            if (FileNameMappings != null)
            {
                var categoryPrefixes = GetPrefixes(categoryName).ToArray();

                var n = FileNameMappings.Length;
                var m = categoryPrefixes.Length;
                for (var i = 0; i < n; i++)
                {
                    var mapping = FileNameMappings[i];
                    for (var j = 0; j < m; j++)
                        if (categoryPrefixes[j] == mapping.Prefix)
                            return mapping.FileName ?? fallbackFileName;
                }
            }

            return fallbackFileName;
        }

        public abstract Func<string, LogLevel, bool> BuildFilter(string categoryName);

        protected abstract FileLoggerSettingsBase CreateClone();

        public IFileLoggerSettingsBase ToImmutable()
        {
            if (_immutable)
                return this;

            var clone = CreateClone();
            clone._immutable = true;

            clone.BasePath = BasePath;
            clone.EnsureBasePath = EnsureBasePath;
            clone.FileEncoding = FileEncoding;

            if (FileNameMappings != null)
            {
                var n = FileNameMappings.Length;
                clone.FileNameMappings = new FileNameMapping[n];
                for (var i = 0; i < n; i++)
                    clone.FileNameMappings[i] = new FileNameMapping(FileNameMappings[i]);
            }

            clone.FileNameMappings = FileNameMappings;
            clone.DateFormat = DateFormat;
            clone.CounterFormat = CounterFormat;
            clone.MaxFileSize = MaxFileSize;
            clone.TextBuilder = TextBuilder;
            clone.IncludeScopes = IncludeScopes;
            clone.MaxQueueSize = MaxQueueSize;

            return clone;
        }
    }

    public class FileLoggerOptions : FileLoggerSettingsBase
    {
        public string FileEncodingName
        {
            get => FileEncoding?.WebName;
            set => FileEncoding = !string.IsNullOrEmpty(value) ? Encoding.GetEncoding(value) : null;
        }

        public string TextBuilderType
        {
            get => TextBuilder?.GetType().AssemblyQualifiedName;
            set
            {
                if (string.IsNullOrEmpty(value))
                    TextBuilder = null;

                var type = Type.GetType(value, throwOnError: true);

                if (!type.GetTypeInfo().ImplementedInterfaces.Contains(typeof(IFileLogEntryTextBuilder)))
                    throw new ArgumentException($"Type must implement the {typeof(IFileLogEntryTextBuilder).Name} interface.", nameof(value));

                if (!type.GetTypeInfo().DeclaredConstructors.Any(ci => ci.GetParameters().Length == 0))
                    throw new ArgumentException("Type must provide a parameterless constructor.", nameof(value));

                TextBuilder = (IFileLogEntryTextBuilder)Activator.CreateInstance(type);
            }
        }

        public override Func<string, LogLevel, bool> BuildFilter(string categoryName)
        {
            return (c, l) => true;
        }

        protected override FileLoggerSettingsBase CreateClone()
        {
            return new FileLoggerOptions();
        }
    }

    public class FileLoggerSettings : FileLoggerSettingsBase, IFileLoggerSettings
    {
        public IDictionary<string, LogLevel> Switches { get; set; } = new Dictionary<string, LogLevel>();

        public IChangeToken ChangeToken { get; set; }

        public IFileLoggerSettings Reload()
        {
            return this;
        }

        protected virtual bool TryGetSwitch(string categoryName, out LogLevel level)
        {
            return Switches.TryGetValue(categoryName, out level);
        }

        public sealed override Func<string, LogLevel, bool> BuildFilter(string categoryName)
        {
            return BuildFilter(categoryName, TryGetSwitch);
        }

        protected override FileLoggerSettingsBase CreateClone()
        {
            return new FileLoggerSettings
            {
                Switches = Switches != null ? new Dictionary<string, LogLevel>(Switches) : null
            };
        }
    }

    public class ConfigurationFileLoggerSettings : IFileLoggerSettings
    {
        public const string LogLevelSectionName = "LogLevel";

        readonly FileLoggerOptions _options;

        public ConfigurationFileLoggerSettings(IConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            Configuration = configuration;

            _options = CreateLoggerOptions();
            LoadOptions();

            ChangeToken = Configuration.GetReloadToken();
        }

        void LoadOptions()
        {
            SetIfNotNull(
                Configuration[nameof(FileLoggerOptions.BasePath)],
                v => _options.BasePath = v);

            var stringValue = Configuration[nameof(FileLoggerOptions.EnsureBasePath)];
            SetIfNotNull(
                !string.IsNullOrEmpty(stringValue) ? bool.Parse(stringValue) : (bool?)null,
                v => _options.EnsureBasePath = v.Value);

            SetIfNotNull(
                Configuration[nameof(FileLoggerOptions.FileEncodingName)],
                v => _options.FileEncodingName = v);

            SetIfNotNull(
                Configuration.GetSection(nameof(FileLoggerOptions.FileNameMappings))
                    .GetChildren()
                    .Select(cs => new { Section = cs, Index = int.TryParse(cs.Key, out int result) ? result : (int?)null })
                    .Where(cs => cs.Index != null)
                    .Select(cs => new FileNameMapping { Prefix = cs.Section[nameof(FileNameMapping.Prefix)], FileName = cs.Section[nameof(FileNameMapping.FileName)] })
                    .ToArray(),
                v => _options.FileNameMappings = v);

            SetIfNotNull(
                Configuration[nameof(FileLoggerOptions.DateFormat)],
                v => _options.DateFormat = v);

            SetIfNotNull(
                Configuration[nameof(FileLoggerOptions.CounterFormat)],
                v => _options.CounterFormat = v);

            stringValue = Configuration[nameof(FileLoggerOptions.MaxFileSize)];
            SetIfNotNull(
                !string.IsNullOrEmpty(stringValue) ? int.Parse(stringValue) : (int?)null,
                v => _options.MaxFileSize = v);

            SetIfNotNull(
                Configuration[nameof(FileLoggerOptions.TextBuilderType)],
                v => _options.TextBuilderType = v);

            stringValue = Configuration[nameof(FileLoggerOptions.IncludeScopes)];
            SetIfNotNull(
                !string.IsNullOrEmpty(stringValue) ? bool.Parse(stringValue) : (bool?)null,
                v => _options.IncludeScopes = v.Value);

            stringValue = Configuration[nameof(FileLoggerOptions.MaxQueueSize)];
            SetIfNotNull(
                !string.IsNullOrEmpty(stringValue) ? int.Parse(stringValue) : (int?)null,
                v => _options.MaxQueueSize = v.Value);

            void SetIfNotNull<T>(T value, Action<T> setter)
            {
                if (value != null)
                    setter(value);
            }
        }

        protected IConfiguration Configuration { get; }

        public string BasePath => _options.BasePath;
        public bool EnsureBasePath => _options.EnsureBasePath;
        public Encoding FileEncoding => _options.FileEncoding;
        public string DateFormat => _options.DateFormat;
        public string CounterFormat => _options.CounterFormat;
        public int? MaxFileSize => _options.MaxFileSize;
        public IFileLogEntryTextBuilder TextBuilder => _options.TextBuilder;
        public bool IncludeScopes => _options.IncludeScopes;
        public int MaxQueueSize => _options.MaxQueueSize;

        public IChangeToken ChangeToken { get; private set; }

        protected virtual FileLoggerOptions CreateLoggerOptions()
        {
            return new FileLoggerOptions();
        }

        protected virtual ConfigurationFileLoggerSettings CreateLoggerSettings()
        {
            return new ConfigurationFileLoggerSettings(Configuration);
        }

        public string MapToFileName(string categoryName, string fallbackFileName)
        {
            return _options.MapToFileName(categoryName, fallbackFileName);
        }

        protected virtual bool TryGetSwitch(string categoryName, out LogLevel level)
        {
            var section = Configuration.GetSection(LogLevelSectionName);
            string text;
            if (section == null || string.IsNullOrEmpty(text = section[categoryName]))
            {
                level = LogLevel.None;
                return false;
            }

            if (Enum.TryParse(text, true, out level))
                return true;

            throw new InvalidOperationException($"Configuration value '{text}' for category '{categoryName}' is not supported.");
        }

        public Func<string, LogLevel, bool> BuildFilter(string categoryName)
        {
            return FileLoggerSettingsBase.BuildFilter(categoryName, TryGetSwitch);
        }

        public IFileLoggerSettings Reload()
        {
            ChangeToken = null;
            return CreateLoggerSettings();
        }

        public IFileLoggerSettingsBase ToImmutable()
        {
            return this;
        }
    }
}
