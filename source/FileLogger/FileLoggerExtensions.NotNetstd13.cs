using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File
{
    public static partial class FileLoggerExtensions
    {
        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, IFileLoggerContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            builder.Services.AddSingleton<IFileLoggerContext>(context);
            builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
            return builder;
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, IFileLoggerContext context, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            builder.AddFile(context);
            builder.Services.Configure(configure);
            return builder;
        }
    }
}
