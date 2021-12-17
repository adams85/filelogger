| :mega: Important notices |
|--------------|
|* If you're upgrading from 2.x to 3.x, there's several breaking changes to be aware of. See the [notes below](#user-content-important-notes-for-existing-consumers) for the details.<br /> * Documentation of the previous major version (2.x) is available [here](https://github.com/adams85/filelogger/tree/2.1).|

# Karambolo.Extensions.Logging.File

This class library contains a lightweight implementation of the [Microsoft.Extensions.Logging.ILoggerProvider](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.iloggerprovider) interface for file logging. Runs on all .NET platforms which implement .NET Standard 2.0+ including .NET Core 2 (ASP.NET Core 2.1+), .NET Core 3 and .NET 5+.

[![NuGet Release](https://img.shields.io/nuget/v/Karambolo.Extensions.Logging.File.svg)](https://www.nuget.org/packages/Karambolo.Extensions.Logging.File/)
[![Donate](https://img.shields.io/badge/-buy_me_a%C2%A0coffee-gray?logo=buy-me-a-coffee)](https://www.buymeacoffee.com/adams85)

The code is based on [ConsoleLogger](https://github.com/dotnet/runtime/tree/master/src/libraries/Microsoft.Extensions.Logging.Console) whose **full feature set is implemented** (including log scopes and configuration reloading). The library has **no 3rd party dependencies**. No I/O blocking occurs as **processing of log messages is done in the background**. File system access is implemented on top of the [Microsoft.Extensions.FileProviders.IFileProvider](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.fileproviders.ifileprovider) abstraction so it's even possible to use a custom backing storage.

### Additional features:
 - Flexible configuration:
   - Two-level log file settings.
   - Fine-grained control over log message filtering.
 - Rolling log files with customizable counter format.
 - Including log entry date in log file paths using templates.
 - Customizable log text formatting.
 - Extensibility by inheritance.
 - Multiple providers with different settings.

### Important notes for existing consumers

**Version 3.0 is a major revision with many improvements involving some breaking changes:**
* Regular users should adjust their logger configuration as the configuration system went through a substantial rework.
* Consumers using advanced features or customization should expect some more work to do because internals were changed extensively too.

Thus, **version 3.0 is not backward compatible with previous versions**. If you want to upgrade from older versions, please read up on the new configuration system to be able to make the necessary adjustments.

However, you may stay with version 2.1 as it continues to work on .NET Core 3 according to my tests (but please note that it isn't developed actively any more).

### Installation

Add the *Karambolo.Extensions.Logging.File* NuGet package to your application project:

    dotnet add package Karambolo.Extensions.Logging.File
    
If you have a .NET Core/.NET 5+ project other than an ASP.NET Core web application (e.g. a console application), also add explicit references to the following NuGet packages *with the version matching your .NET runtime*. For example if your project runs on .NET 6.0.0:

    dotnet add package Microsoft.Extensions.FileProviders.Physical -v 6.0.0
    dotnet add package Microsoft.Extensions.Logging.Configuration -v 6.0.0
    dotnet add package Microsoft.Extensions.Options.ConfigurationExtensions -v 6.0.0

<details>
  <summary>Explanation why this is necessary</summary>
  
  The *Karambolo.Extensions.Logging.File* package depends on some framework libraries and references the lowest possible versions of these depencencies (e.g. *Microsoft.Extensions.Logging.Configuration* 3.0.0 in the case of .NET Standard 2.1 target framework). **These versions may not (mostly do not) align with the version of your application's target platform** since that may be a newer patch, minor (or even major) version (e.g. .NET Core 6.0.0). Thus, referencing *Karambolo.Extensions.Logging.File* in itself usually results in referencing outdated framework libraries on that particular platform (sticking to the previous example, *Microsoft.Extensions.Logging.Configuration* 3.0.0 instead of 6.0.0).

  Luckily, **in the case of ASP.NET Core this is resolved automatically** as ASP.NET Core projects already reference the correct (newer) versions of the framework libraries in question (by means of the *Microsoft.AspNetCore.App* metapackage).

  However, **in other cases (like a plain .NET Core console application) you may end up with outdated dependencies**, which is usually undesired (even can lead to issues like [this](https://github.com/adams85/filelogger/issues/19)), so you want to resolve this situation by adding the explicit package references listed above.

  For more details see [NuGet package dependency resolution](https://docs.microsoft.com/en-us/nuget/concepts/dependency-resolution).
</details>

### Configuration

#### .NET Core 3, .NET 5+

* ASP.NET Core 3.0+, ASP.NET Core 5+ application

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
                        builder.AddConfiguration(ctx.Configuration.GetSection("Logging"));
                        builder.AddFile(o => o.RootPath = ctx.HostingEnvironment.ContentRootPath);
                    })
                    .UseStartup<Startup>();
            });
}
```

* Console application

``` csharp
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
await using (var sp = services.BuildServiceProvider())
{
    var loggerFactory = sp.GetService<ILoggerFactory>();
    // ...
}
```

#### .NET Core 2

* ASP.NET Core 2.1+ application

``` csharp
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
                builder.AddFile(o => o.RootPath = ctx.HostingEnvironment.ContentRootPath);
            })
            .UseStartup<Startup>();
}
```

* Console application

``` csharp
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

#### Advanced use cases

##### Using multiple providers with different settings

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

Now, you have two independent file logger providers. One of them picks up its configuration from the standard configuration section "File" while the other one from section "File2" as specified by the *ProviderAlias* attribute.

You may check out [this demo application](https://github.com/adams85/filelogger/tree/master/samples/SplitByLogLevel) which shows a complete example of this advanced setup.

### Settings

#### Provider settings

There are some settings which are configured on provider level only (*FileLoggerOptions*):

|  | **Description** | **Default value** | **Notes** |
|---|---|---|---|
| **FileAppender** | Specifies the object responsible for appending log messages. | *PhysicalFileAppender* instance with root path set to *Environment.CurrentDirectory* | The *RootPath* shortcut property is also available for setting a *PhysicalFileAppender* with a custom root path. (This path must point to an existing directory.) |
| **BasePath** | Path to the base directory of log files. | "" (none) | Base path is relative to (but cannot point outside of) the root path of the underlying file provider (*FileAppender.FileProvider*). (If this path does not exist, it will be created automatically.) |
| **Files** | An array of *LogFileOptions* which define the settings of the individual log files. | | There is an important change compared to older (2.x or earlier) versions: **you must explicitly define at least one log file**, otherwise the provider won't log anything. |

#### Log file settings

These settings can be configured on log file level only (*LogFileOptions*):

|  | **Description** | **Default value** | **Notes** |
|---|---|---|---|
| **Path** | Path of the log file relative to *FileLoggerOptions.BasePath*. | | Can be a simple path or a path template.<br/>Templates specify placeholders for date and counter strings like "&lt;date&gt;/app-&lt;counter&gt;.log".<br/>The file definition is ignored if *Path* is null or empty.<br/>*Path* should be unique, **multiple definitions with the same *Path* value may lead to erroneous behavior**. |
| **MinLevel** | Defines log level switches for the individual file. | | Works similarly to [LogLevel](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/#log-filtering) filter switches. However, this is a second-level filter which can only tighten the rules of the first-level *LogLevel* filters (as it can only filter messages that hit the logger provider). |

#### Globally configurable log file settings

The log file settings below can be specified globally (per provider) and individually (per log file) as well. **File-level settings always override provider-level settings.** (In practice this means if a setting property is *null* for a given file, the value of the same property of the provider-level settings applies. If that is *null* too, a default value is used.)

|  | **Description** | **Default value** | **Notes** |
|---|---|---|---|
| **FileAccessMode** | Strategy for accessing log files. | LogFileAccessMode. KeepOpenAndAutoFlush | <ul><li>**KeepOpenAndAutoFlush**: Keeps open the log file until completion and flushes each entry into the file immediately.</li><li>**KeepOpen**: Keeps open the log file until completion but entries are flushed only when internal buffer gets full. (Provides the best performance but entries don't appear instantly in the log file.)</li><li>**OpenTemporarily**: Opens the log file only when an entry needs to be written and then closes it immediately. (Provides the worst performance but log files won't be locked by the process.)</li></ul> |
| **FileEncoding** | Character encoding to use. | Encoding.UTF8 | The *FileEncodingName* shortcut property is also available for setting this option using an encoding name. |
| **DateFormat** | Specifies the default date format to use in log file path templates. | "yyyyMMdd" | Value must be a standard .NET format string which can be passed to [DateTimeOffset.ToString](https://docs.microsoft.com/en-us/dotnet/api/system.datetimeoffset.tostring#System_DateTimeOffset_ToString_System_String_).<br/>Date format can even be specified inline in the path template: e.g. "&lt;date:yyyy&gt;/app.log" |
| **CounterFormat** | Specifies the default counter format to use in log file path templates. | basic integer to string conversion | Value must be a standard .NET format string which can be passed to [Int32.ToString](https://docs.microsoft.com/en-us/dotnet/api/system.int32.tostring#System_Int32_ToString_System_String_).<br/>Counter format can even be specified inline in the path template: e.g. "app-&lt;counter:000&gt;.log" |
| **MaxFileSize** | If set, new files will be created when file size limit is reached. | | *Path* must be a template containing a counter placeholder, otherwise the file size limit is not enforced. |
| **TextBuilder** | Specifies a custom log text formatter. | FileLogEntryTextBuilder. Instance | For best performance, if you set this to a formatter of the same type for multiple files, use the same formatter instance if possible.<br/>The *TextBuilderType* shortcut property is also available for setting this option using a type name. |
| **IncludeScopes** | Enables including log scopes in the output. | false | Works exactly as in the case of *ConsoleLogger*. |
| **MaxQueueSize** | Defines the maximum capacity of the log processor queue (per file). | 0 (unbounded) | If set to a value greater than 0, log entries will be discarded when the queue is full, that is, when the specified limit is exceeded. |
| **PathPlaceholderResolver** | Provides a way to hook into path template resolution. | | Callback which can be used to customize or extend the resolution of path template placeholders. Enables special formatting, custom placeholders, etc.<br/>For usage, see the examples [here](https://github.com/adams85/filelogger/issues/6) and [here](https://github.com/adams85/filelogger/issues/7).<br/>(Available only since version 3.2.0) |

#### Sample JSON configuration
``` json5
{
  "Logging": {
    // global filter settings
    "LogLevel": {
        "Default": "Information"
    },
    // provider level settings
    "File": {
      "BasePath": "Logs",
      "FileAccessMode": "KeepOpenAndAutoFlush",
      "FileEncodingName": "utf-8",
      "DateFormat": "yyyyMMdd",
      "CounterFormat": "000",
      "MaxFileSize": 10485760,
      "TextBuilderType": "MyApp.CustomLogEntryTextBuilder, MyApp",
      // first-level filters
      "LogLevel": {
        "MyApp": "Information",
        "Default": "Debug" // first-level filters can loosen the levels specified by the global filters
      },
      "IncludeScopes": true,
      "MaxQueueSize": 100,
      "Files": [
        // a simple log file definition which inherits all settings from the provider (will produce files like "default-000.log")
        {
          "Path": "default-<counter>.log"
        },
        // another log file definition which defines extra filters and overrides the Counter property (will produce files like "2019/08/other-00.log")
        {
          "Path": "<date:yyyy>/<date:MM>/other-<counter>.log",
          // second-level filters
          "MinLevel": {
            "MyApp.SomeClass": "Warning",
            "Default": "Trace" // this has no effect as second-level filters can only be more restrictive than first-level filters!
          },
          "CounterFormat": "00"
        }
      ]
    }
  }
}
```

### Troubleshooting

If you have [added the right NuGet package](#user-content-installation) and [configured logging in your application by the above](#user-content-configuration) but the application outputs no log files, check the following points:

* Have you defined at least one log file in the `Files` collection? If so, have you specified the `Path` property of that file? (See also [this issue](https://github.com/adams85/filelogger/issues/12).)
* Are the `Path` properties of the defined log files valid paths on the operating system you use? If you use path templates (that is, paths containing placeholders like `<date>` or `<counter>`), are they resolved to valid paths?
* Do the combined paths of the defined log files point inside `RootPath` (or more precisely, inside the root path of `FileAppender.FileProvider`)? (See also [this issue](https://github.com/adams85/filelogger/issues/1).)
* Does the application's process have the sufficient file system permissions to create and write files in `RootPath`\\`BasePath`? (See also [this issue](https://github.com/adams85/filelogger/issues/8#issuecomment-611755013).)

If none of these helps, since version 3.2.0 you can track down the problem by observing the file logger's diagnostic events:

```csharp
// this subscription should happen before anything is logged,
// so place it in your code early enough (preferably, before configuration of logging)
FileLoggerContext.Default.DiagnosticEvent += e =>
{
    // examine the diagnostic event here:
    // print it to the debug window, set a breakpoint and inspect internal state on break, etc.
    Debug.WriteLine(e);
};
```
