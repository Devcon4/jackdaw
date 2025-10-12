
using Jackdaw.Interfaces;
using Jackdaw.Queues.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace Jackdaw.Core;

public partial class JackdawBuilder(IServiceCollection services)
{
  public record struct BuildResults(bool HasQueue);
  public record InMemoryQueueOptions(int MaxQueueSize = 1024);

  private BuildResults _results = new(false);

  public JackdawBuilder UseInMemoryQueue(Action<InMemoryQueueOptions>? configure = null)
  {
    var options = new InMemoryQueueOptions();
    configure?.Invoke(options);
    services.AddSingleton<IMessageQueue>(new InMemoryQueue(options.MaxQueueSize));
    _results = _results with { HasQueue = true };
    return this;
  }

  public BuildResults Results => _results;

}
