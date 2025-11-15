using System;
using System.Diagnostics.CodeAnalysis;
using Karambolo.Extensions.Logging.File;
using Karambolo.Extensions.Logging.File.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Logging
{
    public static partial class JsonFileLoggerExtensions
    {
#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
        private const string TrimmingRequiresUnreferencedCodeMessage = $"{nameof(FileLoggerOptions)}'s dependent types may have their members trimmed. Ensure all required members are preserved.";
#endif

        private static ILoggingBuilder ConfigureTextBuilder(this ILoggingBuilder builder, JsonFileLogEntryTextBuilder textBuilder, string optionsName)
        {
            builder.Services.Configure<FileLoggerOptions>(optionsName, options => options.TextBuilder = textBuilder);
            return builder;
        }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
        [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder)
        {
            return builder.AddJsonFile(context: null, JsonFileLogEntryTextBuilder.Default);
        }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
        [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder, JsonFileLogFormatOptions formatOptions)
        {
            return builder.AddJsonFile(context: null, new JsonFileLogEntryTextBuilder(formatOptions));
        }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
        [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            return builder.AddJsonFile(context: null, JsonFileLogEntryTextBuilder.Default, configure);
        }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
        [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder, JsonFileLogFormatOptions formatOptions, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            return builder.AddJsonFile(context: null, new JsonFileLogEntryTextBuilder(formatOptions), configure);
        }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
        [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder, FileLoggerContext context, JsonFileLogFormatOptions formatOptions, Action<FileLoggerOptions> configure)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            return builder.AddJsonFile(context, new JsonFileLogEntryTextBuilder(formatOptions), configure);
        }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
        [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
        public static ILoggingBuilder AddJsonFile(this ILoggingBuilder builder, FileLoggerContext context = null, JsonFileLogEntryTextBuilder textBuilder = null,
            Action<FileLoggerOptions> configure = null)
        {
            (context == null ? builder.AddFile() : builder.AddFile(context))
                .ConfigureTextBuilder(textBuilder ?? JsonFileLogEntryTextBuilder.Default, Options.Options.DefaultName);

            if (configure != null)
                builder.Services.Configure(configure);

            return builder;
        }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
        [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
        public static ILoggingBuilder AddJsonFile<
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider
#else
            TProvider
#endif
        >(this ILoggingBuilder builder, FileLoggerContext context = null, JsonFileLogEntryTextBuilder textBuilder = null,
            Action<FileLoggerOptions> configure = null, string optionsName = null)
            where TProvider : FileLoggerProvider
        {
            builder.AddFile<TProvider>(context, configure: null, optionsName)
                .ConfigureTextBuilder(textBuilder ?? JsonFileLogEntryTextBuilder.Default, optionsName ?? typeof(TProvider).ToString());

            if (configure != null)
                builder.Services.Configure(configure);

            return builder;
        }

#if NET5_0_OR_GREATER
        [RequiresUnreferencedCode($"{nameof(TOptions)}'s dependent types may have their members trimmed. Ensure all required members are preserved.")]
#if NET7_0_OR_GREATER
        [RequiresDynamicCode($"Binding {nameof(TOptions)} to configuration values may require generating dynamic code at runtime.")]
#endif
#endif
        public static ILoggingBuilder AddJsonFile<
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider, TOptions
#else
            TProvider, TOptions
#endif
        >(this ILoggingBuilder builder, FileLoggerContext context = null, JsonFileLogEntryTextBuilder textBuilder = null,
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

        public static ILoggingBuilder AddJsonFile<
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider, TOptions
#else
            TProvider, TOptions
#endif
        >(this ILoggingBuilder builder, Action<TOptions, IConfiguration> bindOptions, FileLoggerContext context = null, JsonFileLogEntryTextBuilder textBuilder = null,
            Action<TOptions> configure = null, string optionsName = null)
            where TProvider : FileLoggerProvider
            where TOptions : FileLoggerOptions
        {
            builder.AddFile<TProvider, TOptions>(bindOptions, context, configure: null, optionsName)
                .ConfigureTextBuilder(textBuilder ?? JsonFileLogEntryTextBuilder.Default, optionsName ?? typeof(TProvider).ToString());

            if (configure != null)
                builder.Services.Configure(configure);

            return builder;
        }
    }
}
