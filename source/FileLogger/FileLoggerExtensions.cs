using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder)
        {
            builder.Services.AddSingleton<ILoggerProvider>(sp => new FileLoggerProvider(sp.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>()));
            return builder;
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, IFileLoggerContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            builder.Services.AddSingleton<ILoggerProvider>(sp => new FileLoggerProvider(context, sp.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>()));
            return builder;
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            builder.Services.Configure(configure);
            return builder.AddFile();
        }

        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, IFileLoggerContext context, Action<FileLoggerOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            builder.Services.Configure(configure);
            return builder.AddFile(context);
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

            var factory = Expression.Lambda<Func<IServiceProvider, ILoggerProvider>>(newExpr, param).Compile();

            builder.Services.Configure(optionsName, configure);
            builder.Services.AddSingleton(factory);
            return builder;
        }
    }
}
