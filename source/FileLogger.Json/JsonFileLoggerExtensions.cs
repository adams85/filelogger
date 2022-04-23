using System;
using Karambolo.Extensions.Logging.File;
using Karambolo.Extensions.Logging.File.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Logging
{
    public static partial class JsonFileLoggerExtensions
    {
        private static ILoggingBuilder ConfigureTextBuilder(this ILoggingBuilder builder, JsonFileLogEntryTextBuilder textBuilder)
        {
            textBuilder ??= JsonFileLogEntryTextBuilder.Default;
            builder.Services.Configure<FileLoggerOptions>(options => options.TextBuilder = textBuilder);
            return builder;
        }

        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder)
        {
            return builder.AddFile().ConfigureTextBuilder(null);
        }

        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            builder.AddFile().ConfigureTextBuilder(null);
            builder.Services.Configure(configure);
            return builder;
        }

        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder, FileLoggerContext context = null, JsonFileLogEntryTextBuilder textBuilder = null,
            Action<FileLoggerOptions> configure = null)
        {
            (context == null ? builder.AddFile() : builder.AddFile(context)).ConfigureTextBuilder(textBuilder);

            if (configure != null)
                builder.Services.Configure(configure);

            return builder;
        }

        public static ILoggingBuilder AddJsonFile<TProvider>(this ILoggingBuilder builder, FileLoggerContext context = null, JsonFileLogEntryTextBuilder textBuilder = null,
            Action<FileLoggerOptions> configure = null, string optionsName = null)
            where TProvider : FileLoggerProvider
        {
            builder.AddFile<TProvider>(context, configure: null, optionsName).ConfigureTextBuilder(textBuilder);

            if (configure != null)
                builder.Services.Configure(configure);

            return builder;
        }
    }
}
