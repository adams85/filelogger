using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Karambolo.Extensions.Logging.File
{
    [ProviderAlias(Alias)]
    public class FileLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        public const string Alias = "File";
        private readonly Dictionary<string, FileLogger> _loggers;
        private readonly string _optionsName;
        private readonly IDisposable _settingsChangeToken;
        private IExternalScopeProvider _scopeProvider;
        private bool _isDisposed;

        public FileLoggerProvider(IOptionsMonitor<FileLoggerOptions> options)
            : this(null, options) { }

        public FileLoggerProvider(IFileLoggerContext context, IOptionsMonitor<FileLoggerOptions> options)
            : this(context, options, null) { }

        public FileLoggerProvider(IFileLoggerContext context, IOptionsMonitor<FileLoggerOptions> options, string optionsName)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            Context = context ?? FileLoggerContext.Default;

            _optionsName = optionsName ?? Options.DefaultName;
            // TODO
            Settings = ((IFileLoggerSettingsBase)options.Get(_optionsName)).Freeze();

            Processor = CreateProcessor(Settings);

            _loggers = new Dictionary<string, FileLogger>();

            _settingsChangeToken = options.OnChange(HandleOptionsChanged);
        }

        public void Dispose()
        {
            lock (_loggers)
                if (!_isDisposed)
                {
                    DisposeCore();
                    _isDisposed = true;
                }
        }

        protected virtual void DisposeCore()
        {
            _settingsChangeToken?.Dispose();

            // blocking in Dispose() seems to be a design flaw, however ConsoleLoggerProcess.Dispose() implemented this way as well
            ResetProcessor(null);
            Processor.Dispose();
        }

        protected IFileLoggerContext Context { get; }
        protected IFileLoggerSettingsBase Settings { get; private set; }
        protected FileLoggerProcessor Processor { get; }

        protected virtual FileLoggerProcessor CreateProcessor(IFileLoggerSettingsBase settings)
        {
            return new FileLoggerProcessor(Context, settings);
        }

        private void ResetProcessor(IFileLoggerSettingsBase newSettings)
        {
            Processor.CompleteAsync(Settings).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        protected virtual string GetFallbackFileName(string categoryName)
        {
            return Settings.FallbackFileName ?? "default.log";
        }

        private void HandleOptionsChanged(IFileLoggerSettingsBase options, string optionsName)
        {
            if (optionsName != _optionsName)
                return;

            lock (_loggers)
            {
                if (_isDisposed)
                    return;

                Settings = options.Freeze();

                IExternalScopeProvider scopeProvider = GetScopeProvider();
                foreach (FileLogger logger in _loggers.Values)
                    logger.Update(GetFallbackFileName(logger.CategoryName), Settings, scopeProvider);

                // we must try to wait for the current queues to complete to avoid concurrent file I/O
                ResetProcessor(Settings);
            }
        }

        protected virtual FileLogger CreateLoggerCore(string categoryName)
        {
            return new FileLogger(categoryName, GetFallbackFileName(categoryName), Processor, Settings, GetScopeProvider(), Context.GetTimestamp);
        }

        public ILogger CreateLogger(string categoryName)
        {
            FileLogger logger;

            lock (_loggers)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(FileLoggerProvider));

                if (!_loggers.TryGetValue(categoryName, out logger))
                    _loggers.Add(categoryName, logger = CreateLoggerCore(categoryName));
            }

            return logger;
        }

        protected IExternalScopeProvider GetScopeProvider()
        {
            if (_scopeProvider == null && Settings.IncludeScopes)
                _scopeProvider = new LoggerExternalScopeProvider();

            return Settings.IncludeScopes ? _scopeProvider : null;
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }
    }
}
