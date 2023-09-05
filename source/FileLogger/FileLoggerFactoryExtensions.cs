using System;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging
{
    internal sealed class FileLoggerOptionsChangeTokenSource<TProvider, TOptions> : ConfigurationChangeTokenSource<TOptions>
        where TProvider : FileLoggerProvider
        where TOptions : FileLoggerOptions
    {
        public FileLoggerOptionsChangeTokenSource(string optionsName, ILoggerProviderConfiguration<TProvider> providerConfiguration)
            : base(optionsName, providerConfiguration.Configuration) { }
    }

    internal sealed class FileLoggerOptionsSetup<TProvider, TOptions> : NamedConfigureFromConfigurationOptions<TOptions>
        where TProvider : FileLoggerProvider
        where TOptions : FileLoggerOptions
    {
        public FileLoggerOptionsSetup(string optionsName, ILoggerProviderConfiguration<TProvider> providerConfiguration)
            : base(optionsName, providerConfiguration.Configuration) { }
    }

    public static partial class FileLoggerFactoryExtensions
    {
        private static ILoggingBuilder AddFile<TProvider, TOptions>(this ILoggingBuilder builder, string optionsName, Func<IServiceProvider, TProvider> providerFactory)
            where TProvider : FileLoggerProvider
            where TOptions : FileLoggerOptions
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            builder.AddConfiguration();

            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, TProvider>(providerFactory));

            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IOptionsChangeTokenSource<TOptions>, FileLoggerOptionsChangeTokenSource<TProvider, TOptions>>(sp =>
                new FileLoggerOptionsChangeTokenSource<TProvider, TOptions>(optionsName, sp.GetRequiredService<ILoggerProviderConfiguration<TProvider>>())));

            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<TOptions>, FileLoggerOptionsSetup<TProvider, TOptions>>(sp =>
                new FileLoggerOptionsSetup<TProvider, TOptions>(optionsName, sp.GetRequiredService<ILoggerProviderConfiguration<TProvider>>())));

            return builder;
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder)
        {
            return builder.AddFile<FileLoggerProvider, FileLoggerOptions>(Options.Options.DefaultName, sp => new FileLoggerProvider(sp.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>()));
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, FileLoggerContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            return builder.AddFile<FileLoggerProvider, FileLoggerOptions>(Options.Options.DefaultName, sp => new FileLoggerProvider(context, sp.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>()));
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            builder.AddFile();
            builder.Services.Configure(configure);
            return builder;
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, FileLoggerContext context, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            builder.AddFile(context);
            builder.Services.Configure(configure);
            return builder;
        }

        public static ILoggingBuilder AddFile<TProvider>(this ILoggingBuilder builder, FileLoggerContext context = null, Action<FileLoggerOptions> configure = null, string optionsName = null)
            where TProvider : FileLoggerProvider
        {
            return builder.AddFile<TProvider, FileLoggerOptions>(context, configure, optionsName);
        }

        public static ILoggingBuilder AddFile<TProvider, TOptions>(this ILoggingBuilder builder, FileLoggerContext context = null, Action<TOptions> configure = null, string optionsName = null)
            where TProvider : FileLoggerProvider
            where TOptions : FileLoggerOptions
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            if (optionsName == null)
                optionsName = typeof(TProvider).ToString();

            if (context == null)
                context = FileLoggerContext.Default;

            builder.AddFile<TProvider, TOptions>(optionsName, sp => ActivatorUtilities.CreateInstance<TProvider>(sp, context, optionsName));

            if (configure != null)
                builder.Services.Configure(optionsName, configure);

            return builder;
        }
    }
}
