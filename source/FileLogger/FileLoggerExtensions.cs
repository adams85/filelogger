using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

namespace Karambolo.Extensions.Logging.File
{
    class FileLoggerOptionsChangeTokenSource<TProvider> : ConfigurationChangeTokenSource<FileLoggerOptions>
        where TProvider : FileLoggerProvider
    {
        public FileLoggerOptionsChangeTokenSource(string optionsName, ILoggerProviderConfiguration<TProvider> providerConfiguration) 
            : base(optionsName, providerConfiguration.Configuration) { }
    }

    class FileLoggerOptionsSetup<TProvider> : NamedConfigureFromConfigurationOptions<FileLoggerOptions>
        where TProvider : FileLoggerProvider
    {
        public FileLoggerOptionsSetup(string optionsName, ILoggerProviderConfiguration<TProvider> providerConfiguration)
            : base(optionsName, providerConfiguration.Configuration) { }
    }

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

        static ILoggingBuilder AddFile<TProvider>(this ILoggingBuilder builder, string optionsName, Func<IServiceProvider, TProvider> providerFactory)
            where TProvider : FileLoggerProvider
        {
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
            return builder.AddFile(Options.DefaultName, sp => new FileLoggerProvider(sp.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>()));
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, IFileLoggerContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            return builder.AddFile(Options.DefaultName, sp => new FileLoggerProvider(context, sp.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>()));
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            builder.AddFile();
            builder.Services.Configure(configure);
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

        static Lazy<MethodInfo> getRequiredServiceMethod = new Lazy<MethodInfo>(() =>
        {
            Expression<Action> callExpr = () => ServiceProviderServiceExtensions.GetRequiredService<object>(null);
            return ((MethodCallExpression)callExpr.Body).Method.GetGenericMethodDefinition();
        });

        public static ILoggingBuilder AddFile<TProvider>(this ILoggingBuilder builder, string optionsName, IFileLoggerContext context = null, Action<FileLoggerOptions> configure = null)
            where TProvider : FileLoggerProvider
        {
            if (optionsName == null)
                throw new ArgumentNullException(nameof(optionsName));

            var constructorArgTypes = new[] { typeof(IFileLoggerContext), typeof(IOptionsMonitor<FileLoggerOptions>), typeof(string) };

            var constructor = typeof(TProvider).GetConstructor(constructorArgTypes);
            if (constructor == null)
                throw new ArgumentException($"The provider type must have a public constructor accepting {string.Join(",", constructorArgTypes.Select(t => t.Name))} (in this order).", nameof(TProvider));

            var param = Expression.Parameter(typeof(IServiceProvider));
            var newExpr = Expression.New(constructor,
                Expression.Constant(context, typeof(IFileLoggerContext)),
                Expression.Call(getRequiredServiceMethod.Value.MakeGenericMethod(typeof(IOptionsMonitor<FileLoggerOptions>)), param),
                Expression.Constant(optionsName, typeof(string)));

            var factory = Expression.Lambda<Func<IServiceProvider, TProvider>>(newExpr, param).Compile();

            builder.AddFile(optionsName, factory);
            builder.Services.Configure(optionsName, configure);
            return builder;
        }
    }
}
