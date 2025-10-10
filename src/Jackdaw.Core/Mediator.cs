using Jackdaw.Interfaces;

namespace Jackdaw.Core;

public interface IMediator
{
  Task<TResponse> Send<TResponse>(
    IRequest<TResponse> request,
    CancellationToken cancellationToken = default) where TResponse : IResponse;
}

public class Mediator(IMessageQueue messageQueue) : IMediator
{
  public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) where TResponse : IResponse
  {
    var requestId = Guid.NewGuid();
    var completionSource = new TaskCompletionSource<TResponse>();

    await messageQueue.EnqueueAsync<TResponse>(new RequestMetadata<TResponse>(requestId, request, completionSource), cancellationToken);

    return await completionSource.Task;
  }
}
