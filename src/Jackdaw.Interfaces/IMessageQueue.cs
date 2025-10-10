namespace Jackdaw.Interfaces;

public interface IMessageQueue
{
  Task<Guid> EnqueueAsync<TResponse>(IRequestMetadata request, CancellationToken cancellationToken) where TResponse : IResponse;
  Task<IRequestMetadata> DequeueAsync(CancellationToken cancellationToken);
}
