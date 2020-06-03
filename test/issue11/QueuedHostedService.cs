using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace issue11
{
    public class QueuedHostedService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly ILogger<QueuedHostedService> _logger;

        public QueuedHostedService(IServiceProvider services, IBackgroundTaskQueue taskQueue, ILogger<QueuedHostedService> logger)
        {
            _services = services;
            _taskQueue = taskQueue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            this._logger.LogInformation("Queued Hosted Service is starting.");
            // ^ this is logged properly in the integra-<counter> file from the category "Itg.Services.Tasks" ^
            await this.BackgroundProceessing(ct);
        }

        private async Task BackgroundProceessing(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var workOrder = await this._taskQueue.DequeueAsync(ct);

                using var scope = this._services.CreateScope();
                var workerType = workOrder
                  .GetType()
                  .GetInterfaces()
                  .First(t => t.IsConstructedGenericType && t.GetGenericTypeDefinition() == typeof(IBackgroundWorkOrder<,>))
                  .GetGenericArguments()
                  .Last();

                // Getting a specific worker based on the generic type defined for the received Order.
                var worker = scope.ServiceProvider
                  .GetRequiredService(workerType);

                var task = (Task)workerType
                  .GetMethod("DoWork")
                  .Invoke(worker, new object[] { workOrder, ct });
                await task;
            }
        }
    }
}
