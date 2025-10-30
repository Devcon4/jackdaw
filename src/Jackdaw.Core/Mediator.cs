// Mediator.cs
using Jackdaw.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Jackdaw.Core;

public interface IMediator
{
  Task<TResponse> Send<TResponse>(
    IRequest<TResponse> request,
    CancellationToken cancellationToken = default) where TResponse : IResponse;
}

public class Mediator(IQueueRouter router) : IMediator
{
  private readonly IQueueRouter _router = router;

  public async Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken cancellationToken = default)
      where TResponse : IResponse
  {
    var requestId = Guid.NewGuid();
    var completionSource = new TaskCompletionSource<TResponse>();

    var messageQueue = _router.GetQueue<IRequest<TResponse>, TResponse>(request);

    await messageQueue.EnqueueAsync<TResponse>(
        new RequestMetadata<TResponse>(requestId, request, completionSource),
        cancellationToken);

    return await completionSource.Task;
  }
}

// Router interface (generated code will implement this)
public interface IQueueRouter
{
  IMessageQueue GetQueue<TRequest, TResponse>(TRequest request) where TResponse : IResponse where TRequest : IRequest<TResponse>;
}