using BenchmarkDotNet.Attributes;
using Jackdaw.Benchmark.Tests.TestHelpers;
using Jackdaw.Interfaces;
using Jackdaw.Queues.InMemory;

namespace Jackdaw.Benchmark.Tests.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class QueueBenchmarks
{
  private InMemoryQueue? _smallQueue;
  private InMemoryQueue? _mediumQueue;
  private InMemoryQueue? _largeQueue;
  private List<IRequestMetadata>? _requests;

  [Params(10, 100, 1000)]
  public int MessageCount { get; set; }

  [GlobalSetup]
  public void Setup()
  {
    _smallQueue = new InMemoryQueue(100);
    _mediumQueue = new InMemoryQueue(1000);
    _largeQueue = new InMemoryQueue(10000);

    // Pre-create requests to avoid allocation overhead in benchmarks
    _requests = Enumerable.Range(0, MessageCount)
        .Select(i => new RequestMetadata<BenchmarkResponse>(
            Guid.NewGuid(),
            new BenchmarkRequest($"data-{i}"),
            new TaskCompletionSource<BenchmarkResponse>()))
        .Cast<IRequestMetadata>()
        .ToList();
  }

  [Benchmark(Description = "Enqueue messages to queue")]
  public async Task EnqueueMessages()
  {
    var queue = new InMemoryQueue(MessageCount + 100);

    for (int i = 0; i < MessageCount; i++)
    {
      await queue.EnqueueAsync<BenchmarkResponse>(_requests![i], CancellationToken.None);
    }
  }

  [Benchmark(Description = "Enqueue and Dequeue messages (FIFO)")]
  public async Task EnqueueDequeueMessages()
  {
    var queue = new InMemoryQueue(MessageCount + 100);

    // Enqueue all
    for (int i = 0; i < MessageCount; i++)
    {
      await queue.EnqueueAsync<BenchmarkResponse>(_requests![i], CancellationToken.None);
    }

    // Dequeue all
    for (int i = 0; i < MessageCount; i++)
    {
      await queue.DequeueAsync(CancellationToken.None);
    }
  }

  [Benchmark(Description = "Concurrent Enqueue/Dequeue")]
  public async Task ConcurrentEnqueueDequeue()
  {
    var queue = new InMemoryQueue(MessageCount + 100);

    // Start producer and consumer tasks
    var enqueueTask = Task.Run(async () =>
    {
      for (int i = 0; i < MessageCount; i++)
      {
        await queue.EnqueueAsync<BenchmarkResponse>(_requests![i], CancellationToken.None);
      }
    });

    var dequeueTask = Task.Run(async () =>
    {
      for (int i = 0; i < MessageCount; i++)
      {
        await queue.DequeueAsync(CancellationToken.None);
      }
    });

    await Task.WhenAll(enqueueTask, dequeueTask);
  }

  [Benchmark(Description = "Small Queue (100 capacity) throughput")]
  public async Task SmallQueueThroughput()
  {
    for (int i = 0; i < Math.Min(MessageCount, 50); i++)
    {
      await _smallQueue!.EnqueueAsync<BenchmarkResponse>(_requests![i], CancellationToken.None);
    }

    for (int i = 0; i < Math.Min(MessageCount, 50); i++)
    {
      await _smallQueue!.DequeueAsync(CancellationToken.None);
    }
  }

  [Benchmark(Description = "Medium Queue (1000 capacity) throughput")]
  public async Task MediumQueueThroughput()
  {
    var count = Math.Min(MessageCount, 500);
    for (int i = 0; i < count; i++)
    {
      await _mediumQueue!.EnqueueAsync<BenchmarkResponse>(_requests![i], CancellationToken.None);
    }

    for (int i = 0; i < count; i++)
    {
      await _mediumQueue!.DequeueAsync(CancellationToken.None);
    }
  }

  [Benchmark(Description = "Large Queue (10000 capacity) throughput")]
  public async Task LargeQueueThroughput()
  {
    var count = Math.Min(MessageCount, 5000);
    for (int i = 0; i < count; i++)
    {
      await _largeQueue!.EnqueueAsync<BenchmarkResponse>(_requests![i % _requests.Count], CancellationToken.None);
    }

    for (int i = 0; i < count; i++)
    {
      await _largeQueue!.DequeueAsync(CancellationToken.None);
    }
  }
}
