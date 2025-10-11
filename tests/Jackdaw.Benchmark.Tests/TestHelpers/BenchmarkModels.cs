using Jackdaw.Interfaces;

namespace Jackdaw.Benchmark.Tests.TestHelpers;

// Benchmark Test Models
public record BenchmarkRequest(string Data) : IRequest<BenchmarkResponse>;
public record BenchmarkResponse(string Result) : IResponse;

public class BenchmarkRequestHandler : IRequestHandler<BenchmarkRequest, BenchmarkResponse>
{
  public Task<BenchmarkResponse> Handle(BenchmarkRequest request, CancellationToken cancellationToken)
  {
    // Minimal processing to measure framework overhead
    return Task.FromResult(new BenchmarkResponse($"Processed: {request.Data}"));
  }
}
