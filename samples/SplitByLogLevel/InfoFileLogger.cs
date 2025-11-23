using System;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SplitByLogLevel;

[ProviderAlias("InfoFile")] // use this alias in appsettings.json to configure this provider
internal class InfoFileLoggerProvider : FileLoggerProvider
{
    public InfoFileLoggerProvider(FileLoggerContext context, IOptionsMonitor<FileLoggerOptions> options, string optionsName) : base(context, options, optionsName) { }

    protected override FileLogger CreateLoggerCore(string categoryName)
    {
        // we instantiate our derived file logger which is modified to log only messages with log level information or below
        return new InfoFileLogger(categoryName, Processor, Settings, GetScopeProvider(), Context.GetTimestamp);
    }
}

internal class InfoFileLogger : FileLogger
{
    public InfoFileLogger(string categoryName, IFileLoggerProcessor processor, IFileLoggerSettings settings, IExternalScopeProvider? scopeProvider = null, Func<DateTimeOffset>? timestampProvider = null)
        : base(categoryName, processor, settings, scopeProvider, timestampProvider) { }

    public override bool IsEnabled(LogLevel logLevel)
    {
        return logLevel <= LogLevel.Information // don't allow messages more severe than information to pass through
            && base.IsEnabled(logLevel);
    }
}
