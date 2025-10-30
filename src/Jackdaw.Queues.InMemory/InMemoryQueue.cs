

using System.Threading.Channels;
using Jackdaw.Interfaces;

namespace Jackdaw.Queues.InMemory;

public class InMemoryQueue(int maxQueueSize) : IMessageQueue
{
  private readonly Channel<IRequestMetadata> _channel = Channel.CreateBounded<IRequestMetadata>(new BoundedChannelOptions(maxQueueSize)
  {
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = true,
    AllowSynchronousContinuations = false
  });

  public async Task<Guid> EnqueueAsync<TResponse>(IRequestMetadata request, CancellationToken cancellationToken) where TResponse : IResponse
  {
    await _channel.Writer.WriteAsync(request, cancellationToken);
    return request.RequestId;
  }

  public async Task<IRequestMetadata> DequeueAsync(CancellationToken cancellationToken)
  {
    var request = await _channel.Reader.ReadAsync(cancellationToken);
    return request;
  }
}
