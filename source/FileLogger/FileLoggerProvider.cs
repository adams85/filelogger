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
            Settings = ((IFileLoggerSettings)options.Get(_optionsName)).Freeze();

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
            ResetProcessor();
            Processor.Dispose();
        }

        protected IFileLoggerContext Context { get; }
        protected IFileLoggerSettings Settings { get; private set; }
        protected FileLoggerProcessor Processor { get; }

        protected virtual FileLoggerProcessor CreateProcessor(IFileLoggerSettings settings)
        {
            return new FileLoggerProcessor(Context);
        }

        private void ResetProcessor()
        {
            Processor.CompleteAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private void HandleOptionsChanged(IFileLoggerSettings options, string optionsName)
        {
            if (optionsName != _optionsName)
                return;

            lock (_loggers)
            {
                if (_isDisposed)
                    return;

                Settings = options.Freeze();

                foreach (FileLogger logger in _loggers.Values)
                    logger.Update(Settings);

                // we must try to wait for the current queues to complete to avoid concurrent file I/O
                ResetProcessor();
            }
        }

        protected virtual FileLogger CreateLoggerCore(string categoryName)
        {
            return new FileLogger(categoryName, Processor, Settings, GetScopeProvider(), Context.GetTimestamp);
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
            return _scopeProvider;
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            lock (_loggers)
            {
                if (_isDisposed)
                    return;

                _scopeProvider = scopeProvider;

                foreach (FileLogger logger in _loggers.Values)
                    logger.Update(scopeProvider);
            }
        }
    }
}
