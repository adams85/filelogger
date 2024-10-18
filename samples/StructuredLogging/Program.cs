﻿using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace StructuredLogging
{
    // This sample demonstrates how to produce structured logs in JSON format.
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));

                // register the JSON file logger
                builder.AddJsonFile(o =>
                {
                    o.RootPath = AppContext.BaseDirectory;
                });
            });

            await using (ServiceProvider sp = services.BuildServiceProvider())
            {
                // create logger factory
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

                // generate a larger amount of messages
                Parallel.For(0, 1000, i =>
                {
                    var logger = loggerFactory.CreateLogger("Thread" + Thread.CurrentThread.ManagedThreadId);
                    var logLevel = (LogLevel)(i % (int)(LogLevel.Critical + 1));
                    logger.Log(logLevel, 0, "Msg" + i, null, (s, _) => s);
                });
            }
        }
    }
 }
