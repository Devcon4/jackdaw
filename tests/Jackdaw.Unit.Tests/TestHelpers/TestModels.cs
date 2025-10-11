using Jackdaw.Interfaces;

namespace Jackdaw.Unit.Tests.TestHelpers;

// Test Response Types
public record TestResponse(string Value) : IResponse;
public record EmptyResponse : IResponse;
public record ErrorResponse(string ErrorMessage) : IResponse;

// Test Request Types
public record TestRequest(string Data) : IRequest<TestResponse>;
public record EmptyRequest : IRequest<EmptyResponse>;
public record ErrorRequest : IRequest<ErrorResponse>;
public record SlowRequest(int DelayMs) : IRequest<TestResponse>;

// Test Handlers
public class TestRequestHandler : IRequestHandler<TestRequest, TestResponse>
{
  public Task<TestResponse> Handle(TestRequest request, CancellationToken cancellationToken)
  {
    return Task.FromResult(new TestResponse($"Processed: {request.Data}"));
  }
}

public class EmptyRequestHandler : IRequestHandler<EmptyRequest, EmptyResponse>
{
  public Task<EmptyResponse> Handle(EmptyRequest request, CancellationToken cancellationToken)
  {
    return Task.FromResult(new EmptyResponse());
  }
}

public class ErrorRequestHandler : IRequestHandler<ErrorRequest, ErrorResponse>
{
  public Task<ErrorResponse> Handle(ErrorRequest request, CancellationToken cancellationToken)
  {
    throw new InvalidOperationException("Test error from handler");
  }
}

public class SlowRequestHandler : IRequestHandler<SlowRequest, TestResponse>
{
  public async Task<TestResponse> Handle(SlowRequest request, CancellationToken cancellationToken)
  {
    await Task.Delay(request.DelayMs, cancellationToken);
    return new TestResponse($"Completed after {request.DelayMs}ms");
  }
}

// Counting handler for verification
public class CountingTestRequestHandler : IRequestHandler<TestRequest, TestResponse>
{
  public int CallCount { get; private set; }
  public List<TestRequest> ReceivedRequests { get; } = new();

  public Task<TestResponse> Handle(TestRequest request, CancellationToken cancellationToken)
  {
    CallCount++;
    ReceivedRequests.Add(request);
    return Task.FromResult(new TestResponse($"Call #{CallCount}: {request.Data}"));
  }
}
