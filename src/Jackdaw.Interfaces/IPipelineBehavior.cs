namespace Jackdaw.Interfaces;

public interface IPipelineBehavior
{
  Task<TResponse> Handle<TRequest, TResponse>(
      TRequest request,
      Func<Task<TResponse>> next,
      CancellationToken cancellationToken)
    where TRequest : IRequest<TResponse>
    where TResponse : IResponse;
}