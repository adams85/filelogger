using System;
using System.Diagnostics.CodeAnalysis;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging;

internal sealed class FileLoggerOptionsChangeTokenSource<TProvider, TOptions> : ConfigurationChangeTokenSource<TOptions>
    where TProvider : FileLoggerProvider
    where TOptions : FileLoggerOptions
{
    public FileLoggerOptionsChangeTokenSource(string optionsName, ILoggerProviderConfiguration<TProvider> providerConfiguration)
        : base(optionsName, providerConfiguration.Configuration) { }
}

internal sealed class FileLoggerOptionsSetup<TProvider, TOptions> : ConfigureNamedOptions<TOptions>
    where TProvider : FileLoggerProvider
    where TOptions : FileLoggerOptions
{
    public FileLoggerOptionsSetup(string name, Action<TOptions> action)
        : base(name, action) { }
}

public static partial class FileLoggerFactoryExtensions
{
#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
    private const string TrimmingRequiresUnreferencedCodeMessage = $"{nameof(FileLoggerOptions)}'s dependent types may have their members trimmed. Ensure all required members are preserved.";
#endif

    private static ILoggingBuilder AddFile<TProvider, TOptions>(this ILoggingBuilder builder, string optionsName, Func<IServiceProvider, TProvider> providerFactory,
        Func<IServiceProvider, FileLoggerOptionsSetup<TProvider, TOptions>> optionsSetupFactory)
        where TProvider : FileLoggerProvider
        where TOptions : FileLoggerOptions
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));

        builder.AddConfiguration();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, TProvider>(providerFactory));

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IOptionsChangeTokenSource<TOptions>, FileLoggerOptionsChangeTokenSource<TProvider, TOptions>>(sp =>
            new FileLoggerOptionsChangeTokenSource<TProvider, TOptions>(optionsName, sp.GetRequiredService<ILoggerProviderConfiguration<TProvider>>())));

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<TOptions>, FileLoggerOptionsSetup<TProvider, TOptions>>(optionsSetupFactory));

        return builder;
    }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
    [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
    private static ILoggingBuilder AddFile<TProvider>(this ILoggingBuilder builder, string optionsName, Func<IServiceProvider, TProvider> providerFactory)
        where TProvider : FileLoggerProvider
    {
        return builder.AddFile<TProvider, FileLoggerOptions>(optionsName,
            providerFactory,
            optionsSetupFactory: sp =>
            {
                IConfiguration config = sp.GetRequiredService<ILoggerProviderConfiguration<TProvider>>().Configuration;

#if NET8_0_OR_GREATER
#pragma warning disable IL2026, IL3050 // suppress false positives
                // In .NET 8+ builds configuration binding is source generated (see also csproj).
                return new FileLoggerOptionsSetup<TProvider, FileLoggerOptions>(optionsName, options => config.Bind(new FileLoggerOptions.BindingWrapper(options)));
#pragma warning restore IL2026, IL3050
#else
                return new FileLoggerOptionsSetup<TProvider, FileLoggerOptions>(optionsName, options => config.Bind(options));
#endif
            });
    }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
    [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder)
    {
        return builder.AddFile<FileLoggerProvider>(Options.Options.DefaultName, sp => new FileLoggerProvider(sp.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>()));
    }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
    [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, FileLoggerContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        return builder.AddFile<FileLoggerProvider>(Options.Options.DefaultName, sp => new FileLoggerProvider(context, sp.GetRequiredService<IOptionsMonitor<FileLoggerOptions>>()));
    }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
    [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, Action<FileLoggerOptions> configure)
    {
        if (configure is null)
            throw new ArgumentNullException(nameof(configure));

        builder.AddFile();
        builder.Services.Configure(configure);
        return builder;
    }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
    [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, FileLoggerContext context, Action<FileLoggerOptions> configure)
    {
        if (configure is null)
            throw new ArgumentNullException(nameof(configure));

        builder.AddFile(context);
        builder.Services.Configure(configure);
        return builder;
    }

#if NET5_0_OR_GREATER && !NET8_0_OR_GREATER
    [RequiresUnreferencedCode(TrimmingRequiresUnreferencedCodeMessage)]
#endif
    public static ILoggingBuilder AddFile<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider
#else
        TProvider
#endif
    >(this ILoggingBuilder builder, FileLoggerContext context = null, Action<FileLoggerOptions> configure = null, string optionsName = null)
        where TProvider : FileLoggerProvider
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));

        optionsName ??= typeof(TProvider).ToString();

        context ??= FileLoggerContext.Default;

        builder.AddFile<TProvider>(optionsName, sp => ActivatorUtilities.CreateInstance<TProvider>(sp, context, optionsName));

        if (configure is not null)
            builder.Services.Configure(optionsName, configure);

        return builder;
    }

#if NET5_0_OR_GREATER
    [RequiresUnreferencedCode($"{nameof(TOptions)}'s dependent types may have their members trimmed. Ensure all required members are preserved.")]
#if NET7_0_OR_GREATER
    [RequiresDynamicCode($"Binding {nameof(TOptions)} to configuration values may require generating dynamic code at runtime.")]
#endif
#endif
    public static ILoggingBuilder AddFile<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider, TOptions
#else
        TProvider, TOptions
#endif
    >(this ILoggingBuilder builder, FileLoggerContext context = null, Action<TOptions> configure = null, string optionsName = null)
        where TProvider : FileLoggerProvider
        where TOptions : FileLoggerOptions
    {
        // SYSLIB1104 is expected here, however found no way to suppress it for the next line only.
        return builder.AddFile<TProvider, TOptions>((options, config) => config.Bind(options), context, configure, optionsName);
    }

    public static ILoggingBuilder AddFile<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider, TOptions
#else
        TProvider, TOptions
#endif
    >(this ILoggingBuilder builder, Action<TOptions, IConfiguration> bindOptions, FileLoggerContext context = null, Action<TOptions> configure = null, string optionsName = null)
        where TProvider : FileLoggerProvider
        where TOptions : FileLoggerOptions
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));

        if (bindOptions is null)
            throw new ArgumentNullException(nameof(bindOptions));

        optionsName ??= typeof(TProvider).ToString();

        context ??= FileLoggerContext.Default;

        builder.AddFile<TProvider, TOptions>(optionsName,
            providerFactory: sp => ActivatorUtilities.CreateInstance<TProvider>(sp, context, optionsName),
            optionsSetupFactory: sp =>
            {
                IConfiguration config = sp.GetRequiredService<ILoggerProviderConfiguration<TProvider>>().Configuration;
                return new FileLoggerOptionsSetup<TProvider, TOptions>(optionsName, options => bindOptions(options, config));
            });

        if (configure is not null)
            builder.Services.Configure(optionsName, configure);

        return builder;
    }
}
