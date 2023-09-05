using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogRotation
{
    [ProviderAlias(Alias)]
    public class RotatingFileLoggerProvider : FileLoggerProvider
    {
        public RotatingFileLoggerProvider(FileLoggerContext context, IOptionsMonitor<RotatingFileLoggerOptions> options, string optionsName)
            : base(context, options, optionsName) { }

        protected override IFileLoggerProcessor CreateProcessor(IFileLoggerSettings settings) => new RotatingFileLoggerProcessor(Context);
    }
}
