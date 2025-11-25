| :mega: Important notices |
|--------------------------|
| If upgrading from v3 to v4, there are some minor breaking changes you may need to be aware of. See the [release notes](https://github.com/adams85/filelogger/releases/tag/v4.0.0) for the details. Documentation of the previous major version (v3) is available [here](https://github.com/adams85/filelogger/tree/3.6). |

# Karambolo.Extensions.Logging.File

This class library contains a lightweight implementation of the [Microsoft.Extensions.Logging.ILoggerProvider](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.iloggerprovider) interface for file logging. Runs on all .NET platforms which implement .NET Standard 2.0+, including .NET Core 2+ (ASP.NET Core 2.1+) and .NET 5+.

[![Build status](https://github.com/adams85/filelogger/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/adams85/filelogger/actions/workflows/ci.yml)
[![NuGet Release](https://img.shields.io/nuget/v/Karambolo.Extensions.Logging.File.svg)](https://www.nuget.org/packages/Karambolo.Extensions.Logging.File/)
[![Donate](https://img.shields.io/badge/-buy_me_a%C2%A0coffee-gray?logo=buy-me-a-coffee)](https://www.buymeacoffee.com/adams85)

The code is based on [ConsoleLogger](https://github.com/dotnet/runtime/tree/master/src/libraries/Microsoft.Extensions.Logging.Console), whose **full feature set is implemented** (including log scopes and configuration reloading). The library has **no 3rd party dependencies**.

To prevent I/O blocking, **log messages are processed asynchronously in the background**, with a guarantee that [pending messages are written to file on graceful shutdown](https://stackoverflow.com/questions/40073743/how-to-log-to-a-file-without-using-third-party-logger-in-net-core#comment129425711_60515392).

File system access is implemented on top of the [Microsoft.Extensions.FileProviders.IFileProvider](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.fileproviders.ifileprovider) abstraction, so it's even possible to use a custom backing storage.

**JSON structured logging** (following the format established by [the JSON formatter of the built-in console logger](https://learn.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter#register-formatter)) is also available.

The **self-contained trimmed** and **Native AOT** deployments models are also supported (in applications running on .NET 8 or newer).

### Additional features:
 - Flexible configuration:
   - Hierarchical (two-level) log file settings.
   - Fine-grained control over log message filtering.
 - File path templates for including log entry date (or other user-defined tokens) in log file paths/names.
 - Size-limited log files with customizable counter format. (Log rotation is [achievable through customization](https://github.com/adams85/filelogger/tree/master/samples/LogRotation).)
 - Fully customizable log text formatting.
 - Designed with extensibility/customizability in mind.
 - Support for registering multiple providers with different settings.

## Installation

Add the _Karambolo.Extensions.Logging.File_ NuGet package to your project:

    dotnet add package Karambolo.Extensions.Logging.File

or, if you want structured logging, add _Karambolo.Extensions.Logging.File.Json_ NuGet package instead:

    dotnet add package Karambolo.Extensions.Logging.File.Json

If you have a .NET Core/.NET 5+ project other than an ASP.NET Core web application (e.g. a console application), you should also consider adding explicit references to the following NuGet packages _with the version matching your .NET runtime_. For example, if your project targets .NET 9:

    dotnet add package Microsoft.Extensions.FileProviders.Physical -v 9.0.11
    dotnet add package Microsoft.Extensions.Logging.Configuration -v 9.0.11
    dotnet add package Microsoft.Extensions.Options.ConfigurationExtensions -v 9.0.11

<details>
  <summary>Explanation why this is recommended</summary>

  The _Karambolo.Extensions.Logging.File_ package depends on some framework libraries and references the lowest possible versions of these dependencies (e.g. the build targeting .NET 9 references _Microsoft.Extensions.Logging.Configuration_ v9.0.0). **These versions may not (mostly will not) align with the version of your application's target platform** since that may be a newer patch, minor or even major version. Thus, referencing _Karambolo.Extensions.Logging.File_ in itself may result in referencing outdated framework libraries on that particular platform (sticking to the previous example, _Microsoft.Extensions.Logging.Configuration_ v9.0.0 instead of v9.0.11).

  Luckily, **in the case of ASP.NET Core this is resolved automatically** as ASP.NET Core projects already reference the correct (newer) versions of the framework libraries in question (by means of the _Microsoft.AspNetCore.App_ metapackage).

  However, **in other cases (like a plain .NET Core/.NET 5+ console application) you may end up with outdated dependencies**, which is usually undesired (even can lead to issues like [this](https://github.com/adams85/filelogger/issues/19)), so you want to resolve this situation by adding the explicit package references listed above.

  For more details, see [NuGet package dependency resolution](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution).
</details>

## Configuration

It's entirely possible to configure the file logger provider by code. However, you usually want to do that using an `appsettings.json` file. A minimal configuration looks like this:

``` json5
{
  "Logging": {
    "File": {
      "LogLevel": {
        "Default": "Information"
      },
      "Files": [
        {
          "Path": "app.log"
        }
      ]
    }
  }
}
```

Please note that you need to specify at least one log file to get anything logged. For a full reference of available settings, see the [Settings section](#settings).

If you've chosen structured logging, replace calls to `AddFile(...)` with `AddJsonFile(...)` in the following code examples.

<details>
  <summary>A sidenote regarding structured logging</summary>

  `AddJsonFile` is just a convenience method, which sets the `TextBuilder` setting to the default JSON formatter (`JsonFileLogEntryTextBuilder`) for the logger provider. You can achieve the same effect by using `AddFile` and setting `TextBuilder` (or `TextBuilderType`) to the aforementioned formatter manually. For details, see the [Settings section](#settings).

  It also follows from the above that you can still override this setting in your configuration (`appsettings.json` or configurer callback) and use other formatters regardless the defaults set by `AddJsonFile`.
</details>

### ASP.NET Core

#### Minimal hosting model

``` csharp
var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFile(o => o.RootPath = builder.Environment.ContentRootPath);

var app = builder.Build();

// ...

app.Run();
```

#### Legacy hosting model

``` csharp
public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .ConfigureLogging((ctx, builder) =>
                    {
                        // The following line might be necessary when using an older version of ASP.NET Core.
                        // builder.AddConfiguration(ctx.Configuration.GetSection("Logging"));

                        builder.AddFile(o => o.RootPath = ctx.HostingEnvironment.ContentRootPath);
                    })
                    .UseStartup<Startup>();
            });
}
```

### .NET Generic Host

#### Minimal hosting model

``` csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddFile(o => o.RootPath = builder.Environment.ContentRootPath);

var host = builder.Build();

// ...

host.Run();
```

#### Legacy hosting model

``` csharp
public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureLogging((ctx, builder) =>
            {
                // The following line might be necessary when using an older version of Microsoft.Extensions.Hosting.
                // builder.AddConfiguration(ctx.Configuration.GetSection("Logging"));

                builder.AddFile(o => o.RootPath = ctx.HostingEnvironment.ContentRootPath);
            });
}
```

### Custom setup using DI

``` csharp
// Build configuration
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

// Configure DI
var services = new ServiceCollection();

services.AddLogging(builder =>
{
    builder.AddConfiguration(configuration.GetSection("Logging"));
    builder.AddFile(o => o.RootPath = AppContext.BaseDirectory);
});

// Build the DI container and start logging
await using (var sp = services.BuildServiceProvider())
{
    var logger = sp.GetRequiredService<ILogger<Program>>();

    // ...
}
```

### Advanced use cases

#### Using multiple providers with different settings

First of all, you need a little bit of boilerplate code:

``` csharp
[ProviderAlias("File2")]
class AltFileLoggerProvider : FileLoggerProvider
{
    public AltFileLoggerProvider(FileLoggerContext context, IOptionsMonitor<FileLoggerOptions> options, string optionsName) : base(context, options, optionsName) { }
}
```

And a setup like this:

``` csharp
services.AddLogging(builder =>
{
    builder.AddConfiguration(config.GetSection("Logging"));
    builder.AddFile(o => o.RootPath = AppContext.BaseDirectory);
    builder.AddFile<AltFileLoggerProvider>(configure: o => o.RootPath = AppContext.BaseDirectory);
});
```

Now, you have two independent file logger providers. One of them picks up its configuration from the standard configuration section `File` while the other one from section `File2`, as specified by the `ProviderAlias` attribute.

You may also check out [this demo application](https://github.com/adams85/filelogger/tree/master/samples/SplitByLogLevel), which shows a complete example of this advanced setup.

#### Customizing/extending the logging logic

The implementation of the file logger provides many extension points (mostly, in the form of overridable virtual methods), so you can customize its behavior and/or implement features that are not available out of the box.

For example, see [this sample application](https://github.com/adams85/filelogger/tree/master/samples/LogRotation), which extends the file logger with the ability to rotate log files.

## Settings

### Provider settings

There are some settings which can be configured at provider level only (`FileLoggerOptions`):

|  | **Description** | **Default value** | **Notes** |
|---|---|---|---|
| **FileAppender** | Specifies the object responsible for appending log messages. | `PhysicalFileAppender` instance with root path set to `Environment.CurrentDirectory` | The `RootPath` shortcut property is also available for setting a `PhysicalFileAppender` with a custom root path. (This path must point to an existing directory.) |
| **BasePath** | Path to the base directory of log files. | `""` (none) | Base path is relative to (but cannot point outside of) the root path of the underlying file provider (`FileAppender.FileProvider`). (If this path does not exist, it will be created automatically.) |
| **Files** | An array of `LogFileOptions`, which define the settings for the individual log files. | | **You need to explicitly define at least one log file**, otherwise the provider won't log anything. |

### Log file settings

These settings can be configured at log file level only (`LogFileOptions`):

|  | **Description** | **Default value** | **Notes** |
|---|---|---|---|
| **Path** | Path of the log file relative to `FileLoggerOptions.BasePath`. | | Can be a simple path or a path template.<br/>Templates specify placeholders for date and counter strings like `"<date>/app-<counter>.log"`.<br/>The file definition is ignored if `Path` is `null` or empty.<br/>`Path` should be unique, **multiple file definitions with the same `Path` value may lead to erroneous behavior**. |
| **MinLevel** | Defines log level switches for the individual file. | | Works similarly to standard [LogLevel](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/#log-level) switches but operates as an additional filter. It can only further restrict the log messages that have already passed through the standard `LogLevel` filter. |

### Globally configurable log file settings

The log file settings below can be specified globally (at provider level) and individually (at log file level) as well. **File-level settings always override provider-level settings.** (In practice, this means if a setting property is `null` for a given file, the value of the same property of the provider-level settings applies. If that is `null` too, a default value is used.)

|  | **Description** | **Default value** | **Notes** |
|---|---|---|---|
| **FileAccessMode** | Strategy for accessing log files. | `LogFileAccessMode.` `KeepOpenAndAutoFlush` | <ul><li>**KeepOpenAndAutoFlush**: Keeps open the log file until completion and flushes each entry into the file immediately.</li><li>**KeepOpen**: Keeps open the log file until completion but entries are flushed only when internal buffer gets full. (Provides the best performance but entries won't appear instantly in the log file.)</li><li>**OpenTemporarily**: Opens the log file only when an entry needs to be written and then closes it immediately. (Provides the worst performance but log files won't be locked by the process.)</li></ul> |
| **FileEncoding** | Character encoding to use. | `Encoding.UTF8` | The `FileEncodingName` shortcut property is also available for setting this option using an encoding name. |
| **DateFormat** | Specifies the default date format to use in log file path templates. | `"yyyyMMdd"` | It must be a standard .NET format string which can be passed to [DateTimeOffset.ToString](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.tostring#System_DateTimeOffset_ToString_System_String_).<br/>Date format can even be specified inline in the path template: e.g. `"<date:yyyy>/app.log"` |
| **CounterFormat** | Specifies the default counter format to use in log file path templates. | default integer to string conversion | It must be a standard .NET format string which can be passed to [Int32.ToString](https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring#System_Int32_ToString_System_String_).<br/>Counter format can even be specified inline in the path template: e.g. `"app-<counter:000>.log"` |
| **MaxFileSize** | If set, new files will be created when file size limit is reached. | | `Path` must be a template containing a counter placeholder, otherwise the file size limit is not enforced. |
| **TextBuilder** | Specifies a custom log text formatter. | `FileLogEntryTextBuilder.` `Instance` | For best performance, use a single formatter instance for all files that need the same formatting if possible.<br/>The `TextBuilderType` shortcut property is also available for setting this option using a type name. (However, when [trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained) is enabled, you need to ensure the referenced type is preserved.)<br/>For an example of usage, see [this sample application](https://github.com/adams85/filelogger/tree/master/samples/CustomLogFormat). |
| **IncludeScopes** | Enables log scopes to be included in the output. | `false` | Works the same way as `ConsoleLogger`. |
| **MaxQueueSize** | Defines the maximum capacity of the log processor queue (per file). | `0` (unbounded) | If set to a value greater than 0, log entries will be discarded when the queue is full, that is, when the specified limit is exceeded. |
| **PathPlaceholderResolver** | Provides a way to hook into path template resolution. | | This is a callback that can be used to customize or extend the resolution of path template placeholders. Enables special formatting, custom placeholders, etc.<br/>For an example of usage, see [this sample application](https://github.com/adams85/filelogger/tree/master/samples/CustomPathPlaceholder). |

### Sample JSON configuration
``` json5
{
  "Logging": {
    // global filter settings
    "LogLevel": {
      "Default": "Information"
    },
    // provider-level settings
    "File": {
      "BasePath": "Logs",
      "FileAccessMode": "KeepOpenAndAutoFlush",
      "FileEncodingName": "utf-8",
      "DateFormat": "yyyyMMdd",
      "CounterFormat": "000",
      "MaxFileSize": 10485760,
      "TextBuilderType": "MyApp.CustomLogEntryTextBuilder, MyApp",
      // provider-level filters
      "LogLevel": {
        "MyApp": "Information",
        "Default": "Debug" // provider-level filters can loosen the levels specified by the global filters
      },
      "IncludeScopes": true,
      "MaxQueueSize": 100,
      "Files": [
        // a simple log file definition, which inherits all settings from the provider (will produce files like "default-000.log")
        {
          "Path": "default-<counter>.log"
        },
        // another log file definition, which defines extra filters and overrides the Counter property (will produce files like "2019/08/other-00.log")
        {
          "Path": "<date:yyyy>/<date:MM>/other-<counter>.log",
          // file-level filters
          "MinLevel": {
            "MyApp.SomeClass": "Warning",
            "Default": "Trace" // this will have no effect as file-level filters can only be more restrictive than provider-level filters!
          },
          "CounterFormat": "00"
        }
      ]
    }
  }
}
```

## Troubleshooting

If you have [added the right NuGet package](#installation) and [configured logging as described above](#configuration) but your application outputs no log files, check the following:

- Have you defined at least one log file in the `Files` collection? If so, have you specified the `Path` property of that file? (See also [this issue](https://github.com/adams85/filelogger/issues/12).)
- Are the `Path` properties of the defined log files valid paths on the operating system you use? If you use path templates (that is, paths containing placeholders like `<date>` or `<counter>`), are they resolved to valid paths?
- Do the combined paths of the defined log files point inside `RootPath` (or more precisely, inside the root path of `FileAppender.FileProvider`)? (See also [this issue](https://github.com/adams85/filelogger/issues/1).)
- Does the application's process have the sufficient file system permissions to create and write files in the `"{RootPath}/{BasePath}"` directory? (See also [this issue](https://github.com/adams85/filelogger/issues/8#issuecomment-611755013).)

If none of these helps, you can track down the problem by observing the file logger's diagnostic events:

```csharp
// This subscription should happen before anything is logged, so place it
// in your code early enough (preferably, before configuration of logging).
FileLoggerContext.Default.DiagnosticEvent += e =>
{
    // Examine the diagnostic event (print it to the debug window,
    // set a breakpoint and inspect internal state on break, etc.)
    Debug.WriteLine(e);
};
```
