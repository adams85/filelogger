using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

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

        static ILoggingBuilder AddFile<TProvider>(this ILoggingBuilder builder, string optionsName, Func<IServiceProvider, TProvider> providerFactory)
            where TProvider : FileLoggerProvider
        {
            builder.AddConfiguration();

            builder.Services.AddSingleton<ILoggerProvider, TProvider>(providerFactory);

            builder.Services.AddSingleton<IOptionsChangeTokenSource<FileLoggerOptions>>(sp => 
                new ConfigurationChangeTokenSource<FileLoggerOptions>(optionsName, sp.GetRequiredService<ILoggerProviderConfiguration<TProvider>>().Configuration));
            builder.Services.AddSingleton<IConfigureOptions<FileLoggerOptions>>(sp =>
                new NamedConfigureFromConfigurationOptions<FileLoggerOptions>(optionsName, sp.GetRequiredService<ILoggerProviderConfiguration<TProvider>>().Configuration));

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
