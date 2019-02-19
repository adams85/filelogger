# Karambolo.Extensions.Logging.File

This class library contains a lightweight implementation of the [Microsoft.Extensions.Logging.ILoggerProvider](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.iloggerprovider) interface for file logging. Supports .NET Core 1.1 (.NET Standard 1.3), .NET Core 2.0 and .NET Core 2.1 (.NET Standard 2.0).

[![NuGet Release](https://img.shields.io/nuget/v/Karambolo.Extensions.Logging.File.svg)](https://www.nuget.org/packages/Karambolo.Extensions.Logging.File/)

The code is based on [ConsoleLogger](https://github.com/aspnet/Logging/tree/master/src/Microsoft.Extensions.Logging.Console) whose **full feature set is implemented** (including log scopes and configuration reloading). The library has **no 3rd party dependencies**. No I/O blocking occurs as **processing of log messages is done in the background**. File system access is implemented on top of the [Microsoft.Extensions.FileProviders.IFileProvider](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.fileproviders.ifileprovider) abstraction so it's even possible to use a custom backing storage.

### Additional features:
 - Routing log messages based on category name to different files.
 - Rolling log files with customizable counter format.
 - Seperate log files based on log entry date.
 - Customizable log text formatting.
 - Extensibility through inheritance.
 - Multiple providers with different settings (as of version 2.1).

### Important

#### Upgrading to version 2.1

This version comes with several breaking changes. These changes mostly affect the internal interfaces. After upgrading, usually you just need to rebuild your project and you're good to go.

However, if you implemented a custom *IFileLogEntryTextBuilder*, you will need a small change to your code. The signature of *BuildEntryText* method was changed slightly because *FileLogScope* was eliminated as MS introduced the *IExternalScopeProvider* abstraction for unified log scope handling in .NET Core 2.1.

Furthermore, it's worth noting that you don't need *FileLoggerContext* any more when configuring your logger provider. Root path and fallback file name should be configured through *FileLoggerOptions*. To avoid a complete breaking change, this interface remained unchanged for the moment but using the related *FileLoggerContext* constructors is obsolete now.

Also keep in mind that in .NET Core 2.1 *ILoggingBuilder.AddConfiguration(IConfiguration)* automatically configures the options of your logging providers (as long as configuration sections are named properly) so manual configuration is unnecessary and redundant.

### Configuration samples

#### .NET Core 2.1

* ASP.NET Core application

```
public class Program
{
    public static void Main(string[] args)
    {
        CreateWebHostBuilder(args).Build().Run();
    }

    public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
        WebHost.CreateDefaultBuilder(args)
            .ConfigureLogging((ctx, builder) =>
            {
                builder.AddConfiguration(ctx.Configuration.GetSection("Logging"));
                builder.AddFile(o => o.RootPath = AppContext.BaseDirectory);
            })
            .UseStartup<Startup>();
}
```

* Console application

```
// build configuration
// var configuration = ...;

// configure DI
var services = new ServiceCollection();

services.AddLogging(builder =>
{
    builder.AddConfiguration(configuration.GetSection("Logging"));
    builder.AddFile(o => o.RootPath = AppContext.BaseDirectory);
});

// create logger factory
using (var sp = services.BuildServiceProvider())
{
    var loggerFactory = sp.GetService<ILoggerFactory>();
    // ...
}
```

#### .NET Core 2.0

* ASP.NET Core application

```
public class Program
{
    public static void Main(string[] args)
    {
        BuildWebHost(args).Run();
    }

    public static IWebHost BuildWebHost(string[] args) =>
        WebHost.CreateDefaultBuilder(args)
            .UseStartup<Startup>()
            .ConfigureLogging((ctx, builder) =>
            {
                builder.AddConfiguration(ctx.Configuration.GetSection("Logging"));
                builder.AddFile(new FileLoggerContext(AppContext.BaseDirectory, "default.log"));
                builder.Services.Configure<FileLoggerOptions>(ctx.Configuration.GetSection("Logging:File"));
            })
            .Build();
}
```

* Console application

```
// build configuration
// var configuration = ...;

// configure DI
var services = new ServiceCollection();

services.AddLogging(builder =>
{
    builder.AddConfiguration(configuration.GetSection("Logging"));
    builder.AddFile(new FileLoggerContext(AppContext.BaseDirectory, "default.log"));
    builder.Services.Configure<FileLoggerOptions>(configuration.GetSection("Logging:File"));
});

// create logger factory
using (var sp = services.BuildServiceProvider())
{
    var loggerFactory = sp.GetService<ILoggerFactory>();
    // ...
}
```

#### .NET Core 1.1

* ASP.NET Core application

```
public class Startup
{
    // class members omitted for brevity...

    public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
    {
        loggerFactory.AddFile(new FileLoggerContext(AppContext.BaseDirectory, "default.log"), Configuration.GetSection("Logging:File"));

        // ...
    }
}
```

* Console application

```
// build configuration
// var configuration = ...;

// create logger factory
using (var loggerFactory = new LoggerFactory())
{
    loggerFactory.AddFile(new FileLoggerContext(AppContext.BaseDirectory, "default.log"), configuration.GetSection("Logging:File"));

    // ...
}
```

#### Advanced use cases

* Using multiple providers with different settings

This feature is available as of version 2.1.

First of all, you need a little bit of boilerplate code:

```
[ProviderAlias("File2")]
class AltFileLoggerProvider : FileLoggerProvider
{
    public AltFileLoggerProvider(IFileLoggerContext context, IOptionsMonitor<FileLoggerOptions> options, string optionsName) : base(context, options, optionsName) { }
}
```

And a setup like this:

```
services.AddLogging(builder =>
{
    builder.AddConfiguration(config.GetSection("Logging"));
    builder.AddFile(o => o.RootPath = AppContext.BaseDirectory);
    builder.AddFile<AltFileLoggerProvider>(configure: o => o.RootPath = AppContext.BaseDirectory);
});
```

Now, you have two independent file logger providers. One of them picks up its configuration from the standard configuration section "File" while the other one from section "File2" as specified by the *ProviderAlias* attribute.

### Settings

#### Reference

|  | **Description** | **Default value** | **Notes** |
|---|---|---|---|
| **FileAppender** | Specifies the object responsible for appending log messages. | *PhysicalFileAppender* instance with root path set to *Environment.CurrentDirectory* | Available as of version 2.1. *FileLoggerOptions* provides the *RootPath* shortcut property for setting a *PhysicalFileAppender* with a custom root path. |
| **BasePath** | Path to the base directory of log files. | "" (none) | Path is relative to (but cannot access outside of) the root path of the underlying file provider (*FileAppender.FileProvider*). |
| **EnsureBasePath** | Tries to create base directory if it does not exist. | false | |
| **FileEncoding** | Character encoding to use. | UTF-8 | *FileLoggerOptions* provides the *FileEncodingName* shortcut property for setting this option using encoding name. |
| **FallbackFileName** | Name of the file in which log entries with unmapped category names are sent. | "default.log" | Available as of version 2.1. |
| **FileNameMappings** | Defines log category name to file name mapping by (prefix, file name) pairs (similarly to log level switches). | | |
| **DateFormat** | If set, separate files will be created based on date using the specified format. | unset (no date appended) | |
| **CounterFormat** | Specifies the format of the counter if any. | unset | |
| **MaxFileSize** | If greater than 0, new files will be created using a counter when file size limit is reached. | 0 (no counter appended) | |
| **TextBuilder** | Specifies a custom log text formatter type. | | *FileLoggerOptions* provides the *TextBuilderType* shortcut property for setting this option using type name. |
| **LogLevel** | Defines log level switches. | | Works exactly as in the case of *ConsoleLogger*. |
| **IncludeScopes** | Enables including log scopes in the output. | false | Works exactly as in the case of *ConsoleLogger*. |
| **MaxQueueSize** | Defines the maximum capacity of the log processor queue (per file). | -1 (unbounded) | If set to a value greater than 0, log entries will be discarded when the queue is full, that is the specified limit is exceeded. |

#### Sample JSON configuration
```
{
  "Logging": {
    "File": {
      "BasePath": "Logs",
      "EnsureBasePath": true,
      "FileEncoding": "utf-8",
      "FileNameMappings": {
        "MyApp.SomeClass": "someclass.log",
        "Default": "default.log"
      },
      "DateFormat": "yyyyMMdd",
      "CounterFormat": "000",
      "MaxFileSize": 10485760,
      "TextBuilderType": "MyApp.CustomLogEntryTextBuilder, MyApp",
      "LogLevel": {
        "MyApp": "Information",
        "Default": "Warning"
      },
      "IncludeScopes": true,
      "MaxQueueSize": 100
    }
  },
  // global filter settings
  "LogLevel": {
    "Default": "Information"
  }
}
```
