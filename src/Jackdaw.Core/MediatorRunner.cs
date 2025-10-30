using Jackdaw.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jackdaw.Core;

public class MediatorRunner : BackgroundService
{
  private readonly string _queueName;
  private readonly IServiceProvider _serviceProvider;
  private readonly IHandlerDispatcher _dispatcher;

  public MediatorRunner(
      string queueName,
      IServiceProvider serviceProvider,
      IHandlerDispatcher dispatcher)
  {
    _queueName = queueName;
    _serviceProvider = serviceProvider;
    _dispatcher = dispatcher;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var messageQueue = _serviceProvider.GetRequiredKeyedService<IMessageQueue>(_queueName);

    while (!stoppingToken.IsCancellationRequested)
    {
      var request = await messageQueue.DequeueAsync(stoppingToken);

      using var scope = _serviceProvider.CreateScope();

      await _dispatcher.DispatchAsync(request, scope.ServiceProvider, stoppingToken);
    }
  }
}

public interface IHandlerDispatcher
{
  Task DispatchAsync(IRequestMetadata metadata, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}
