using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File
{
    public static partial class FileLoggerExtensions
    {
        public static ILoggerFactory AddFile(this ILoggerFactory factory, IFileLoggerContext context, IFileLoggerSettings settings)
        {
            factory.AddProvider(new FileLoggerProvider(context, settings));
            return factory;
        }

        public static ILoggerFactory AddFile(this ILoggerFactory factory, IFileLoggerContext context, IConfiguration configuration)
        {
            var settings = new ConfigurationFileLoggerSettings(configuration);
            return factory.AddFile(context, settings);
        }

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
