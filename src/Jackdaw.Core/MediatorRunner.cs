using Jackdaw.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jackdaw.Core;

public class MediatorRunner(IMessageQueue messageQueue, IServiceProvider serviceProvider, IHandlerDispatcher dispatcher) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      var request = await messageQueue.DequeueAsync(stoppingToken);

      using var scope = serviceProvider.CreateScope();

      await dispatcher.DispatchAsync(request, scope.ServiceProvider, stoppingToken);
    }
  }
}

public interface IHandlerDispatcher
{
  Task DispatchAsync(IRequestMetadata metadata, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}
