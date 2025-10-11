using BenchmarkDotNet.Attributes;
using Jackdaw.Benchmark.Tests.TestHelpers;
using Jackdaw.Core;
using Jackdaw.Interfaces;
using NSubstitute;

namespace Jackdaw.Benchmark.Tests.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class MediatorBenchmarks
{
  private IMediator? _mediator;
  private IMessageQueue? _mockQueue;
  private List<BenchmarkRequest>? _requests;

  [Params(10, 100, 1000)]
  public int RequestCount { get; set; }

  [GlobalSetup]
  public void Setup()
  {
    _mockQueue = Substitute.For<IMessageQueue>();
    _mediator = new Mediator(_mockQueue);

    // Configure mock to immediately complete requests
    _mockQueue.EnqueueAsync<BenchmarkResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          var metadata = callInfo.ArgAt<RequestMetadata<BenchmarkResponse>>(0);
          // Simulate instant completion
          metadata.CompletionSource.SetResult(new BenchmarkResponse("Completed"));
          return Task.FromResult(metadata.RequestId);
        });

    // Pre-create requests
    _requests = Enumerable.Range(0, RequestCount)
        .Select(i => new BenchmarkRequest($"request-{i}"))
        .ToList();
  }

  [Benchmark(Description = "Send single request through mediator")]
  public async Task<BenchmarkResponse> SendSingleRequest()
  {
    return await _mediator!.Send<BenchmarkResponse>(_requests![0], CancellationToken.None);
  }

  [Benchmark(Description = "Send multiple sequential requests")]
  public async Task SendSequentialRequests()
  {
    for (int i = 0; i < RequestCount; i++)
    {
      await _mediator!.Send<BenchmarkResponse>(_requests![i], CancellationToken.None);
    }
  }

  [Benchmark(Description = "Send multiple concurrent requests")]
  public async Task SendConcurrentRequests()
  {
    var tasks = _requests!.Select(r => _mediator!.Send<BenchmarkResponse>(r, CancellationToken.None));
    await Task.WhenAll(tasks);
  }

  [Benchmark(Description = "Request/Response roundtrip latency")]
  public async Task<BenchmarkResponse> RequestResponseLatency()
  {
    var request = new BenchmarkRequest("latency-test");
    return await _mediator!.Send<BenchmarkResponse>(request, CancellationToken.None);
  }

  [Benchmark(Description = "Mediator overhead (request creation and queueing)")]
  public async Task MediatorOverhead()
  {
    // Measure just the mediator's overhead without handler execution
    for (int i = 0; i < Math.Min(RequestCount, 100); i++)
    {
      await _mediator!.Send<BenchmarkResponse>(_requests![i], CancellationToken.None);
    }
  }
}
