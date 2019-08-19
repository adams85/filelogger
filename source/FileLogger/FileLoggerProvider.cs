using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Karambolo.Extensions.Logging.File
{
    [ProviderAlias(Alias)]
    public class FileLoggerProvider : ILoggerProvider, ISupportExternalScope
#if NETSTANDARD2_1
        , IAsyncDisposable
#endif
    {
        public const string Alias = "File";
        private readonly Dictionary<string, FileLogger> _loggers;
        private readonly string _optionsName;
        private readonly IDisposable _settingsChangeToken;
        private IExternalScopeProvider _scopeProvider;
        private Task _resetTask;
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

            _loggers = new Dictionary<string, FileLogger>();

            _resetTask = Task.CompletedTask;

            Processor = CreateProcessor(Settings);

            _settingsChangeToken = options.OnChange(HandleOptionsChanged);
        }

        public void Dispose()
        {
            if (TryDisposeAsync(completeProcessorOnThreadPool: true, out Task completeProcessorTask))
            {
                completeProcessorTask.ConfigureAwait(false).GetAwaiter().GetResult();
                Processor.Dispose();
            }
        }

#if NETSTANDARD2_1
        public async ValueTask DisposeAsync()
        {
            if (TryDisposeAsync(completeProcessorOnThreadPool: false, out Task completeProcessorTask))
            {
                await completeProcessorTask.ConfigureAwait(false);
                Processor.Dispose();
            }
        }
#endif

        private bool TryDisposeAsync(bool completeProcessorOnThreadPool, out Task completeProcessorTask)
        {
            lock (_loggers)
                if (!_isDisposed)
                {
                    _settingsChangeToken.Dispose();

                    completeProcessorTask =
                        completeProcessorOnThreadPool ?
                        Task.Run(() => Processor.CompleteAsync()) :
                        Processor.CompleteAsync();

                    DisposeCore();

                    _isDisposed = true;
                    return true;
                }

            completeProcessorTask = null;
            return false;
        }

        protected virtual void DisposeCore() { }

        public IFileLoggerContext Context { get; }
        protected IFileLoggerSettings Settings { get; private set; }
        protected IFileLoggerProcessor Processor { get; }

        public Task Completion => Processor.Completion;

        protected virtual IFileLoggerProcessor CreateProcessor(IFileLoggerSettings settings)
        {
            return new FileLoggerProcessor(Context);
        }

        private async Task ResetProcessorAsync(Action updateSettings)
        {
            await _resetTask.ConfigureAwait(false);

            await Processor.ResetAsync(updateSettings).ConfigureAwait(false);
        }

        private void HandleOptionsChanged(IFileLoggerSettings options, string optionsName)
        {
            if (optionsName != _optionsName)
                return;

            lock (_loggers)
            {
                if (_isDisposed)
                    return;

                _resetTask = ResetProcessorAsync(() =>
                {
                    lock (_loggers)
                    {
                        if (_isDisposed)
                            return;

                        Settings = options.Freeze();

                        foreach (FileLogger logger in _loggers.Values)
                            logger.Update(Settings);
                    }
                });
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
