﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File
{
    using FileGroup = KeyValuePair<(IFileLogEntryTextBuilder TextBuilder, bool IncludeScopes), (ILogFileSettings Settings, LogLevel MinLevel)[]>;

    public class FileLogger : ILogger
    {
        protected class UpdatableState
        {
            public IFileLoggerSettings Settings { get; set; }
            public FileGroup[] FileGroups { get; set; }
            public IExternalScopeProvider ScopeProvider { get; set; }
        }

        [ThreadStatic]
        private static StringBuilder s_stringBuilder;

        private readonly IFileLoggerProcessor _processor;
        private readonly Func<DateTimeOffset> _timestampGetter;

        public FileLogger(string categoryName, IFileLoggerProcessor processor, IFileLoggerSettings settings, IExternalScopeProvider scopeProvider = null,
            Func<DateTimeOffset> timestampGetter = null)
        {
            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));
            if (processor == null)
                throw new ArgumentNullException(nameof(processor));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            CategoryName = categoryName;

            _processor = processor;

            UpdatableState state = CreateState(null, settings);
            state.Settings = settings;
            state.FileGroups = GetFileGroups(settings);
            state.ScopeProvider = scopeProvider;
            _state = state;

            _timestampGetter = timestampGetter ?? (() => DateTimeOffset.UtcNow);
        }

        private volatile UpdatableState _state;

        public string CategoryName { get; }

        public IEnumerable<string> FilePaths => _state.FileGroups.SelectMany(fileGroup => fileGroup.Value.Select(file => file.Settings.Path));

        private FileGroup[] GetFileGroups(IFileLoggerSettings settings)
        {
            return (settings.Files ?? Enumerable.Empty<ILogFileSettings>())
                .Where(file => file != null && !string.IsNullOrEmpty(file.Path))
                .Select(file =>
                    (Settings: file,
                     MinLevel: file.GetMinLevel(CategoryName)))
                .Where(file => file.MinLevel != LogLevel.None)
                .GroupBy(
                    file =>
                        (file.Settings.TextBuilder ?? settings.TextBuilder ?? FileLogEntryTextBuilder.Instance,
                         file.Settings.IncludeScopes ?? settings.IncludeScopes ?? false),
                    (key, group) => new FileGroup(key, group.ToArray()))
                .ToArray();
        }

        protected virtual UpdatableState CreateState(UpdatableState currentState, IFileLoggerSettings settings)
        {
            return new UpdatableState();
        }

        public void Update(IFileLoggerSettings settings)
        {
            FileGroup[] fileGroups = GetFileGroups(settings);

            UpdatableState currentState = _state;
            for (; ; )
            {
                UpdatableState newState = CreateState(currentState, settings);
                newState.Settings = settings;
                newState.FileGroups = fileGroups;
                newState.ScopeProvider = currentState.ScopeProvider;

                UpdatableState originalState = Interlocked.CompareExchange(ref _state, newState, currentState);
                if (currentState == originalState)
                    return;

                currentState = originalState;
            }
        }

        public void Update(IExternalScopeProvider scopeProvider)
        {
            UpdatableState currentState = _state;
            for (; ; )
            {
                UpdatableState newState = CreateState(currentState, null);
                newState.Settings = currentState.Settings;
                newState.FileGroups = currentState.FileGroups;
                newState.ScopeProvider = scopeProvider;

                UpdatableState originalState = Interlocked.CompareExchange(ref _state, newState, currentState);
                if (currentState == originalState)
                    return;

                currentState = originalState;
            }
        }

        public virtual bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
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
            if (!IsEnabled(logLevel))
                return;

            if (formatter == null)
                throw new ArgumentNullException(nameof(formatter));

            UpdatableState currentState = _state;

            DateTimeOffset timestamp = _timestampGetter();

            var message = FormatState(state, exception, formatter);

            StringBuilder sb = s_stringBuilder;
            s_stringBuilder = null;
            if (sb == null)
                sb = new StringBuilder();

            var fileGroups = currentState.FileGroups;
            for (int i = 0; i < fileGroups.Length; i++)
            {
                FileGroup fileGroup = fileGroups[i];
                (ILogFileSettings Settings, LogLevel MinLevel)[] files = fileGroup.Value;
                FileLogEntry entry = null;

                for (int j = 0; j < files.Length; j++)
                {
                    (ILogFileSettings fileSettings, LogLevel minLevel) = files[j];

                    if (logLevel < minLevel)
                        continue;

                    if (entry == null)
                    {
                        (IFileLogEntryTextBuilder textBuilder, bool includeScopes) = fileGroup.Key;
                        IExternalScopeProvider logScope = includeScopes ? currentState.ScopeProvider : null;

                        if (textBuilder is StructuredFileLogEntryTextBuilder structuredTextBuilder)
                            structuredTextBuilder.BuildEntryText(sb, CategoryName, logLevel, eventId, message, state, exception, logScope, timestamp);
                        else
                            textBuilder.BuildEntryText(sb, CategoryName, logLevel, eventId, message, exception, logScope, timestamp);

                        if (sb.Length > 0)
                        {
                            entry = CreateLogEntry();
                            entry.Text = sb.ToString();
                            entry.Timestamp = timestamp;
                            sb.Clear();
                        }
                        else
                            break;
                    }

                    _processor.Enqueue(entry, fileSettings, currentState.Settings);
                }
            }

            if (sb.Capacity > 1024)
                sb.Capacity = 1024;

            s_stringBuilder = sb;
        }

        public virtual IDisposable BeginScope<TState>(TState state)
        {
            return _state.ScopeProvider?.Push(state) ?? NullScope.Instance;
        }
    }
}
