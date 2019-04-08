using System;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SplitByLogLevel
{
    [ProviderAlias("InfoFile")] // use this alias in appsettings.json to configure this provider
    class InfoFileLoggerProvider : FileLoggerProvider
    {
        public InfoFileLoggerProvider(IFileLoggerContext context, IOptionsMonitor<FileLoggerOptions> options, string optionsName) : base(context, options, optionsName) { }

        protected override FileLogger CreateLoggerCore(string categoryName)
        {
            // we instantiate our derived file logger which is modified to log only messages with log level information or below
            return new InfoFileLogger(categoryName, GetFallbackFileName(categoryName), Processor, Settings, GetScopeProvider(), Context.GetTimestamp);
        }
    }

    class InfoFileLogger : FileLogger
    {
        public InfoFileLogger(string categoryName, string fallbackFileName, IFileLoggerProcessor processor, IFileLoggerSettingsBase settings, IExternalScopeProvider scopeProvider = null, Func<DateTimeOffset> timestampGetter = null)
            : base(categoryName, fallbackFileName, processor, settings, scopeProvider, timestampGetter) { }

        protected override bool IsEnabled(UpdatableState state, LogLevel logLevel)
        {
            return
                logLevel <= LogLevel.Information &&  // don't allow messages more severe than information to pass through
                base.IsEnabled(state, logLevel);
        }
    }
}
