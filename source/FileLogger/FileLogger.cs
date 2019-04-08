using System;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions.Internal;

namespace Karambolo.Extensions.Logging.File
{
    public class FileLogger : ILogger
    {
        protected class UpdatableState
        {
            public string FileName;
            public Func<string, LogLevel, bool> Filter;
            public IExternalScopeProvider ScopeProvider;
            public IFileLogEntryTextBuilder TextBuilder;
        }

        [ThreadStatic]
        private static StringBuilder s_stringBuilder;
        private UpdatableState _state;

        private UpdatableState State
        {
            get => Interlocked.CompareExchange(ref _state, null, null);
            set => Interlocked.Exchange(ref _state, value);
        }

        private readonly IFileLoggerProcessor _processor;
        private readonly Func<DateTimeOffset> _timestampGetter;

        public FileLogger(string categoryName, string fallbackFileName, IFileLoggerProcessor processor, IFileLoggerSettingsBase settings,
            Func<DateTimeOffset> timestampGetter = null)
            : this(categoryName, fallbackFileName, processor, settings, settings.IncludeScopes ? new LoggerExternalScopeProvider() : null, timestampGetter) { }

        public FileLogger(string categoryName, string fallbackFileName, IFileLoggerProcessor processor, IFileLoggerSettingsBase settings,
            IExternalScopeProvider scopeProvider = null, Func<DateTimeOffset> timestampGetter = null)
            : this(
                categoryName ?? throw new ArgumentNullException(nameof(categoryName)),
                settings?.MapToFileName(categoryName, fallbackFileName ?? throw new ArgumentNullException(nameof(fallbackFileName))) ??
                    throw new ArgumentNullException(nameof(settings)),
                processor,
                settings.BuildFilter(categoryName),
                scopeProvider,
                settings.TextBuilder,
                timestampGetter: timestampGetter)
        { }

        public FileLogger(string categoryName, string fileName, IFileLoggerProcessor processor, Func<string, LogLevel, bool> filter = null,
            bool includeScopes = false, IFileLogEntryTextBuilder textBuilder = null, Func<DateTimeOffset> timestampGetter = null)
            : this(categoryName, fileName, processor, filter, includeScopes ? new LoggerExternalScopeProvider() : null, textBuilder, timestampGetter) { }

        public FileLogger(string categoryName, string fileName, IFileLoggerProcessor processor, Func<string, LogLevel, bool> filter = null,
            IExternalScopeProvider scopeProvider = null, IFileLogEntryTextBuilder textBuilder = null, Func<DateTimeOffset> timestampGetter = null)
        {
            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));
            if (processor == null)
                throw new ArgumentNullException(nameof(processor));

            CategoryName = categoryName;

            _processor = processor;

            _state = new UpdatableState
            {
                FileName = fileName,
                Filter = filter ?? ((c, l) => true),
                ScopeProvider = scopeProvider,
                TextBuilder = textBuilder ?? FileLogEntryTextBuilder.Instance,
            };

            _timestampGetter = timestampGetter ?? (() => DateTimeOffset.UtcNow);
        }

        public string CategoryName { get; }

        public string FileName => State.FileName;

        public Func<string, LogLevel, bool> Filter => State.Filter;

        public bool IncludeScopes => State.ScopeProvider != null;

        protected virtual UpdatableState CreateState(IFileLoggerSettingsBase settings)
        {
            return new UpdatableState();
        }

        public void Update(string fallbackFileName, IFileLoggerSettingsBase settings)
        {
            Update(fallbackFileName, settings, settings.IncludeScopes ? new LoggerExternalScopeProvider() : null);
        }

        public void Update(string fallbackFileName, IFileLoggerSettingsBase settings, IExternalScopeProvider scopeProvider)
        {
            // full thread synchronization is omitted for performance reasons
            // as it is considered non-critical (ConsoleLogger is implemented in a similar way)

            UpdatableState state = CreateState(settings);

            state.FileName = settings.MapToFileName(CategoryName, fallbackFileName);
            state.Filter = settings.BuildFilter(CategoryName);
            state.ScopeProvider = scopeProvider;
            state.TextBuilder = settings.TextBuilder ?? FileLogEntryTextBuilder.Instance;

            State = state;
        }

        protected virtual bool IsEnabled(UpdatableState state, LogLevel logLevel)
        {
            return state.Filter(CategoryName, logLevel);
        }

        public virtual bool IsEnabled(LogLevel logLevel)
        {
            return IsEnabled(State, logLevel);
        }

        protected virtual FileLogEntry CreateLogEntry()
        {
            return new FileLogEntry();
        }

        protected virtual string FormatState<TState>(TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            return formatter(state, exception);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            UpdatableState objState = State;

            if (!IsEnabled(objState, logLevel))
                return;

            if (formatter == null)
                throw new ArgumentNullException(nameof(formatter));

            DateTimeOffset timestamp = _timestampGetter();
            var message = FormatState(state, exception, formatter);
            IExternalScopeProvider logScope = objState.ScopeProvider;

            StringBuilder sb = s_stringBuilder;
            s_stringBuilder = null;
            if (sb == null)
                sb = new StringBuilder();

            objState.TextBuilder.BuildEntryText(sb, CategoryName, logLevel, eventId, message, exception, logScope, timestamp);

            if (sb.Length > 0)
            {
                FileLogEntry entry = CreateLogEntry();

                entry.Text = sb.ToString();
                entry.Timestamp = timestamp;

                _processor.Enqueue(objState.FileName, entry);
            }

            sb.Clear();
            if (sb.Capacity > 1024)
                sb.Capacity = 1024;

            s_stringBuilder = sb;
        }

        public virtual IDisposable BeginScope<TState>(TState state)
        {
            return State.ScopeProvider?.Push(state) ?? NullScope.Instance;
        }
    }
}
