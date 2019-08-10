using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging
{
    internal class FileLoggerOptionsChangeTokenSource<TProvider> : ConfigurationChangeTokenSource<FileLoggerOptions>
        where TProvider : FileLoggerProvider
    {
        public FileLoggerOptionsChangeTokenSource(string optionsName, ILoggerProviderConfiguration<TProvider> providerConfiguration)
            : base(optionsName, providerConfiguration.Configuration) { }
    }

    internal class FileLoggerOptionsSetup<TProvider> : NamedConfigureFromConfigurationOptions<FileLoggerOptions>
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

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, IFileLoggerContext context)
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

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, IFileLoggerContext context, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            builder.AddFile(context);
            builder.Services.Configure(configure);
            return builder;
        }

        private static readonly Lazy<MethodInfo> s_getRequiredServiceMethod = new Lazy<MethodInfo>(() =>
        {
            Expression<Action> callExpr = () => ServiceProviderServiceExtensions.GetRequiredService<object>(null);
            return ((MethodCallExpression)callExpr.Body).Method.GetGenericMethodDefinition();
        });

        private static Func<IServiceProvider, TProvider> CreateProviderFactory<TProvider>(IFileLoggerContext context, string optionsName)
            where TProvider : FileLoggerProvider
        {
            Type[] constructorArgTypes = new[] { typeof(IFileLoggerContext), typeof(IOptionsMonitor<FileLoggerOptions>), typeof(string) };

            ConstructorInfo constructor = typeof(TProvider).GetConstructor(constructorArgTypes);
            if (constructor == null)
                throw new ArgumentException($"The provider type must have a public constructor accepting {string.Join(",", constructorArgTypes.Select(t => t.Name))} (in this order).", nameof(TProvider));

            ParameterExpression param = Expression.Parameter(typeof(IServiceProvider));
            NewExpression newExpr = Expression.New(constructor,
                Expression.Constant(context, typeof(IFileLoggerContext)),
                Expression.Call(s_getRequiredServiceMethod.Value.MakeGenericMethod(typeof(IOptionsMonitor<FileLoggerOptions>)), param),
                Expression.Constant(optionsName, typeof(string)));

            Func<IServiceProvider, TProvider> factory = Expression.Lambda<Func<IServiceProvider, TProvider>>(newExpr, param).Compile();
            return factory;
        }

        public static ILoggingBuilder AddFile<TProvider>(this ILoggingBuilder builder, IFileLoggerContext context = null, Action<FileLoggerOptions> configure = null, string optionsName = null)
            where TProvider : FileLoggerProvider
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            if (optionsName == null)
                optionsName = typeof(TProvider).FullName;

            builder.AddFile(optionsName, CreateProviderFactory<TProvider>(context, optionsName));
            builder.Services.Configure(optionsName, configure);
            return builder;
        }
    }
}
