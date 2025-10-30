namespace Jackdaw.Interfaces;

public interface IMessageQueue : IDefaultMessageQueue
{
  Task<Guid> EnqueueAsync<TResponse>(IRequestMetadata request, CancellationToken cancellationToken) where TResponse : IResponse;
  Task<IRequestMetadata> DequeueAsync(CancellationToken cancellationToken);
}

public interface IDefaultMessageQueue { }
