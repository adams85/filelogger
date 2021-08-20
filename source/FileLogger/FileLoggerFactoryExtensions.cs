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
    internal sealed class FileLoggerOptionsChangeTokenSource<TProvider> : ConfigurationChangeTokenSource<FileLoggerOptions>
        where TProvider : FileLoggerProvider
    {
        public FileLoggerOptionsChangeTokenSource(string optionsName, ILoggerProviderConfiguration<TProvider> providerConfiguration)
            : base(optionsName, providerConfiguration.Configuration) { }
    }

    internal sealed class FileLoggerOptionsSetup<TProvider> : NamedConfigureFromConfigurationOptions<FileLoggerOptions>
        where TProvider : FileLoggerProvider
    {
        public FileLoggerOptionsSetup(string optionsName, ILoggerProviderConfiguration<TProvider> providerConfiguration)
            : base(optionsName, providerConfiguration.Configuration) { }
    }

    public static partial class FileLoggerFactoryExtensions
    {
        private static ILoggingBuilder AddFile<TProvider>(this ILoggingBuilder builder, string optionsName, Func<IServiceProvider, TProvider> providerFactory)
            where TProvider : FileLoggerProvider
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            builder.AddConfiguration();

            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, TProvider>(providerFactory));

            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IOptionsChangeTokenSource<FileLoggerOptions>, FileLoggerOptionsChangeTokenSource<TProvider>>(sp =>
                new FileLoggerOptionsChangeTokenSource<TProvider>(optionsName, sp.GetRequiredService<ILoggerProviderConfiguration<TProvider>>())));

            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<FileLoggerOptions>, FileLoggerOptionsSetup<TProvider>>(sp =>
                new FileLoggerOptionsSetup<TProvider>(optionsName, sp.GetRequiredService<ILoggerProviderConfiguration<TProvider>>())));

            return builder;
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder)
        {
            return builder.AddFile(Options.Options.DefaultName, sp => new FileLoggerProvider(sp.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>()));
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, FileLoggerContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            return builder.AddFile(Options.Options.DefaultName, sp => new FileLoggerProvider(context, sp.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>()));
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
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            if (optionsName == null)
                optionsName = typeof(TProvider).ToString();

            if (context == null)
                context = FileLoggerContext.Default;

            builder.AddFile(optionsName, sp => ActivatorUtilities.CreateInstance<TProvider>(sp, context, optionsName));

            if (configure != null)
                builder.Services.Configure(optionsName, configure);

            return builder;
        }
    }
}
