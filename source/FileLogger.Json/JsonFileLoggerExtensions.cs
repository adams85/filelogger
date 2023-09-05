using System;
using Karambolo.Extensions.Logging.File;
using Karambolo.Extensions.Logging.File.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Logging
{
    public static partial class JsonFileLoggerExtensions
    {
        private static ILoggingBuilder ConfigureTextBuilder(this ILoggingBuilder builder, JsonFileLogEntryTextBuilder textBuilder, string optionsName)
        {
            builder.Services.Configure<FileLoggerOptions>(optionsName, options => options.TextBuilder = textBuilder);
            return builder;
        }

        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder)
        {
            return builder.AddJsonFile(context: null, JsonFileLogEntryTextBuilder.Default);
        }

        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder, JsonFileLogFormatOptions formatOptions)
        {
            return builder.AddJsonFile(context: null, new JsonFileLogEntryTextBuilder(formatOptions));
        }

        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            return builder.AddJsonFile(context: null, JsonFileLogEntryTextBuilder.Default, configure);
        }

        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder, JsonFileLogFormatOptions formatOptions, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            return builder.AddJsonFile(context: null, new JsonFileLogEntryTextBuilder(formatOptions), configure);
        }

        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder, FileLoggerContext context = null, JsonFileLogEntryTextBuilder textBuilder = null,
            Action<FileLoggerOptions> configure = null)
        {
            (context == null ? builder.AddFile() : builder.AddFile(context))
                .ConfigureTextBuilder(textBuilder ?? JsonFileLogEntryTextBuilder.Default, Options.Options.DefaultName);

            if (configure != null)
                builder.Services.Configure(configure);

            return builder;
        }

        public static ILoggingBuilder AddJsonFile<TProvider>(this ILoggingBuilder builder, FileLoggerContext context = null, JsonFileLogEntryTextBuilder textBuilder = null,
            Action<FileLoggerOptions> configure = null, string optionsName = null)
            where TProvider : FileLoggerProvider
        {
            return builder.AddJsonFile<TProvider, FileLoggerOptions>(context, textBuilder, configure, optionsName);
        }

        public static ILoggingBuilder AddJsonFile<TProvider, TOptions>(this ILoggingBuilder builder, FileLoggerContext context = null, JsonFileLogEntryTextBuilder textBuilder = null,
            Action<TOptions> configure = null, string optionsName = null)
            where TProvider : FileLoggerProvider
            where TOptions : FileLoggerOptions
        {
            builder.AddFile<TProvider, TOptions>(context, configure: null, optionsName)
                .ConfigureTextBuilder(textBuilder ?? JsonFileLogEntryTextBuilder.Default, optionsName ?? typeof(TProvider).ToString());

            if (configure != null)
                builder.Services.Configure(configure);

            return builder;
        }
    }
}
