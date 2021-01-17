using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Karambolo.Extensions.Logging.File
{
    [ProviderAlias(Alias)]
    public partial class FileLoggerProvider : ILoggerProvider, ISupportExternalScope, IAsyncDisposable
    {
        public const string Alias = "File";
        private readonly Dictionary<string, FileLogger> _loggers;
        private readonly string _optionsName;
        private readonly IDisposable _settingsChangeToken;
        private IExternalScopeProvider _scopeProvider;
        private Task _resetTask;
        private bool _isDisposed;

        protected FileLoggerProvider(FileLoggerContext context, IFileLoggerSettings settings)
        {
            _loggers = new Dictionary<string, FileLogger>();
            _resetTask = Task.CompletedTask;

            Context = context ?? FileLoggerContext.Default;
            Settings = settings.Freeze();
            Processor = CreateProcessor(Settings);
        }

        public FileLoggerProvider(IOptions<FileLoggerOptions> options)
            : this(null, options) { }

        public FileLoggerProvider(FileLoggerContext context, IOptions<FileLoggerOptions> options)
            : this(context, options != null ? options.Value : throw new ArgumentNullException(nameof(options))) { }

        public FileLoggerProvider(IOptionsMonitor<FileLoggerOptions> options)
            : this(null, options) { }

        public FileLoggerProvider(FileLoggerContext context, IOptionsMonitor<FileLoggerOptions> options)
            : this(context, options, null) { }

        public FileLoggerProvider(FileLoggerContext context, IOptionsMonitor<FileLoggerOptions> options, string optionsName)
            : this(context, options != null ? options.Get(optionsName ?? Options.DefaultName) : throw new ArgumentNullException(nameof(options)))
        {
            _optionsName = optionsName ?? Options.DefaultName;
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

        public async ValueTask DisposeAsync()
        {
            if (TryDisposeAsync(completeProcessorOnThreadPool: false, out Task completeProcessorTask))
            {
                await completeProcessorTask.ConfigureAwait(false);
                Processor.Dispose();
            }
        }

        private bool TryDisposeAsync(bool completeProcessorOnThreadPool, out Task completeProcessorTask)
        {
            lock (_loggers)
                if (!_isDisposed)
                {
                    _settingsChangeToken?.Dispose();

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

        public FileLoggerContext Context { get; }
        protected IFileLoggerSettings Settings { get; private set; }
        protected IFileLoggerProcessor Processor { get; }

        internal event Action<FileLoggerProvider, Task> Reset;

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

            Task resetTask;

            lock (_loggers)
            {
                if (_isDisposed)
                    return;

                _resetTask = resetTask = ResetProcessorAsync(() =>
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

            Reset?.Invoke(this, resetTask);
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
