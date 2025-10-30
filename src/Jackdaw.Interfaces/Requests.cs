namespace Jackdaw.Interfaces;

public interface IResponse { }
public interface IRequest<TResponse> where TResponse : IResponse { }
public interface IRequestHandlerBase { }
public interface IHandler<TRequest, TResponse> : IRequestHandlerBase
    where TRequest : IRequest<TResponse> where TResponse : IResponse
{
  Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}



public interface IRequestMetadata { Guid RequestId { get; } }
public record struct RequestMetadata<TResponse>(Guid RequestId, IRequest<TResponse> Request, TaskCompletionSource<TResponse> CompletionSource) : IRequestMetadata where TResponse : IResponse;
