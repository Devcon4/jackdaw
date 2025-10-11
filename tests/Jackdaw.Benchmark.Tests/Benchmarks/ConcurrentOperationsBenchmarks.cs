using BenchmarkDotNet.Attributes;
using Jackdaw.Benchmark.Tests.TestHelpers;
using Jackdaw.Interfaces;
using Jackdaw.Queues.InMemory;

namespace Jackdaw.Benchmark.Tests.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class ConcurrentOperationsBenchmarks
{
  private InMemoryQueue? _queue;
  private List<IRequestMetadata>? _requests;

  [Params(10, 50, 100)]
  public int ConcurrentTasks { get; set; }

  [Params(100, 500)]
  public int MessagesPerTask { get; set; }

  [GlobalSetup]
  public void Setup()
  {
    var totalMessages = ConcurrentTasks * MessagesPerTask;
    _queue = new InMemoryQueue(totalMessages + 1000);

    _requests = Enumerable.Range(0, totalMessages)
        .Select(i => new RequestMetadata<BenchmarkResponse>(
            Guid.NewGuid(),
            new BenchmarkRequest($"concurrent-{i}"),
            new TaskCompletionSource<BenchmarkResponse>()))
        .Cast<IRequestMetadata>()
        .ToList();
  }

  [Benchmark(Description = "Concurrent producers enqueuing")]
  public async Task ConcurrentProducers()
  {
    var tasks = Enumerable.Range(0, ConcurrentTasks)
        .Select(taskIndex => Task.Run(async () =>
        {
          var startIndex = taskIndex * MessagesPerTask;
          for (int i = 0; i < MessagesPerTask; i++)
          {
            await _queue!.EnqueueAsync<BenchmarkResponse>(
                _requests![startIndex + i],
                CancellationToken.None);
          }
        }))
        .ToList();

    await Task.WhenAll(tasks);
  }

  [Benchmark(Description = "Concurrent consumers dequeuing")]
  public async Task ConcurrentConsumers()
  {
    // Pre-populate queue
    var totalMessages = ConcurrentTasks * MessagesPerTask;
    for (int i = 0; i < totalMessages; i++)
    {
      await _queue!.EnqueueAsync<BenchmarkResponse>(_requests![i], CancellationToken.None);
    }

    // Concurrent consumers
    var tasks = Enumerable.Range(0, ConcurrentTasks)
        .Select(_ => Task.Run(async () =>
        {
          for (int i = 0; i < MessagesPerTask; i++)
          {
            await _queue!.DequeueAsync(CancellationToken.None);
          }
        }))
        .ToList();

    await Task.WhenAll(tasks);
  }

  [Benchmark(Description = "Mixed concurrent producers and consumers")]
  public async Task MixedConcurrentOperations()
  {
    var halfTasks = ConcurrentTasks / 2;

    // Start producers
    var producerTasks = Enumerable.Range(0, halfTasks)
        .Select(taskIndex => Task.Run(async () =>
        {
          var startIndex = taskIndex * MessagesPerTask;
          for (int i = 0; i < MessagesPerTask; i++)
          {
            await _queue!.EnqueueAsync<BenchmarkResponse>(
                _requests![startIndex + i],
                CancellationToken.None);
          }
        }))
        .ToList();

    // Start consumers
    var consumerTasks = Enumerable.Range(0, halfTasks)
        .Select(_ => Task.Run(async () =>
        {
          for (int i = 0; i < MessagesPerTask; i++)
          {
            await _queue!.DequeueAsync(CancellationToken.None);
          }
        }))
        .ToList();

    await Task.WhenAll(producerTasks.Concat(consumerTasks));
  }

  [Benchmark(Description = "High contention scenario")]
  public async Task HighContentionScenario()
  {
    // Many threads competing for queue access
    var tasks = Enumerable.Range(0, ConcurrentTasks)
        .Select(taskIndex => Task.Run(async () =>
        {
          var startIndex = taskIndex * (MessagesPerTask / 2);

          // Each task both produces and consumes
          for (int i = 0; i < MessagesPerTask / 2; i++)
          {
            await _queue!.EnqueueAsync<BenchmarkResponse>(
                _requests![startIndex + i],
                CancellationToken.None);

            if (i % 2 == 0) // Dequeue every other operation
            {
              try
              {
                await _queue!.DequeueAsync(CancellationToken.None);
              }
              catch
              {
                // Queue might be empty
              }
            }
          }
        }))
        .ToList();

    await Task.WhenAll(tasks);
  }

  [Benchmark(Description = "Burst traffic pattern")]
  public async Task BurstTrafficPattern()
  {
    // Simulate burst: rapid enqueue, then rapid dequeue
    var burstSize = Math.Min(MessagesPerTask, 100);

    for (int burst = 0; burst < 5; burst++)
    {
      // Burst enqueue
      var enqueueTasks = Enumerable.Range(0, ConcurrentTasks)
          .Select(taskIndex => Task.Run(async () =>
          {
            var startIndex = (burst * ConcurrentTasks * burstSize) + (taskIndex * burstSize);
            for (int i = 0; i < burstSize && startIndex + i < _requests!.Count; i++)
            {
              await _queue!.EnqueueAsync<BenchmarkResponse>(
                  _requests[startIndex + i],
                  CancellationToken.None);
            }
          }));

      await Task.WhenAll(enqueueTasks);

      // Burst dequeue
      var dequeueTasks = Enumerable.Range(0, ConcurrentTasks)
          .Select(_ => Task.Run(async () =>
          {
            for (int i = 0; i < burstSize; i++)
            {
              try
              {
                await _queue!.DequeueAsync(CancellationToken.None);
              }
              catch
              {
                // Queue might be empty
                break;
              }
            }
          }));

      await Task.WhenAll(dequeueTasks);
    }
  }
}
