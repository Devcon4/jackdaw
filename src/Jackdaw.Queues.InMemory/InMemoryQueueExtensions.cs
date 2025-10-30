using Jackdaw.Core;

namespace Jackdaw.Queues.InMemory;

public static class InMemoryQueueExtensions
{
  public static QueueBuilder UseInMemory(
      this QueueBuilder builder,
      InMemoryQueueOptions? options = null)
  {
    options ??= new InMemoryQueueOptions();

    return builder.UseQueue(sp => new InMemoryQueue(options.MaxQueueSize));
  }
}

public record InMemoryQueueOptions(int MaxQueueSize = 1024);