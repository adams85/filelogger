using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomBehavior
{
    [ProviderAlias(Alias)]
    public class CustomFileLoggerProvider : FileLoggerProvider
    {
        public CustomFileLoggerProvider(FileLoggerContext context, IOptionsMonitor<FileLoggerOptions> options, string optionsName)
            : base(context, options, optionsName) { }

        protected override IFileLoggerProcessor CreateProcessor(IFileLoggerSettings settings) => new CustomFileLoggerProcessor(Context);
    }
}
