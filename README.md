# Karambolo.Extensions.Logging.File

This class library contains an implementation of the [Microsoft.Extensions.Logging.ILoggerProvider](https://docs.microsoft.com/en-us/aspnet/core/api/microsoft.extensions.logging.iloggerprovider) interface for file logging. Supports .NET Core 1.1 (.NET Standard 1.3) and .NET Core 2.0 (.NET Standard 2.0).

[![NuGet Release](https://img.shields.io/nuget/v/Karambolo.Extensions.Logging.File.svg)](https://www.nuget.org/packages/Karambolo.Extensions.Logging.File/)

The code is based on ConsoleLogger and its **full feature set is implemented** (including log scopes and configuration reloading). The library has **no 3rd party dependencies**. No I/O blocking occurs as **processing log messages is done in the background**. File system access is implemented on top of the [Microsoft.Extensions.FileProviders.IFileProvider](https://docs.microsoft.com/en-us/aspnet/core/api/microsoft.extensions.fileproviders.ifileprovider) abstraction, so backing storage can be replaced.

### Additional features:
 - Routing log messages based on category name to different files.
 - Rolling log files with customizable counter format.
 - Seperate log files based on log entry date.
 - Customizable log text formatting.
 - Extensibility through inheritance.

### Usage

#### .NET Core 1.x
```
// build configuration...

var settings = new ConfigurationFileLoggerSettings(config);
var context = new FileLoggerContext(AppContext.BaseDirectory, "fallback.log");

// create logger factory...

loggerFactory.AddFile(context, settings);
```
#### .NET Core 2.x
```
// build configuration...

var services = new ServiceCollection();

services.AddOptions();

var context = new FileLoggerContext(AppContext.BaseDirectory, "fallback.log");
services.AddLogging(b => b.AddFile(context));

services.Configure<FileLoggerOptions>(config);

// inject or resolve ILogger<T> or ILoggerFactory from the service provider
```

### Settings

 - **BasePath**: path to the base directory of log files. Path is relative to (but cannot access outside of) the file provider root path.
 - **EnsureBasePath**: tries to create base directory if it does not exist.
 - **FileEncoding**: character encoding to use. Default value: UTF-8.
 - **FileNameMappings**: defines log category name to file name mapping by (prefix, file name) pairs (similarly to log level switches).
 - **DateFormat**: if set, separate files will be created based on date. 
 - **CounterFormat**: specifies the format of the counter if any.
 - **MaxFileSize**: if greater than 0, new files will be created when file size limit is reached.
 - **TextBuilder**: specifies a custom log text formatter type.
 - **LogLevel**: defines log level switches (exactly as in the case of ConsoleLogger).
 - **IncludeScopes**: enables including log scopes in the output (exactly as in the case of ConsoleLogger).
 - **MaxQueueSize**: defines the maximum capacity of the (per file) log processor queue. If queue is full, log entries will be discarded. Default value: 64.

#### Sample JSON configuration
```
{
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
```
