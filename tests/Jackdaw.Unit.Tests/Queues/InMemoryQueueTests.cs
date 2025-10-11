using Jackdaw.Interfaces;
using Jackdaw.Queues.InMemory;
using Jackdaw.Unit.Tests.TestHelpers;
using Xunit;

namespace Jackdaw.Unit.Tests.Queues;

public class InMemoryQueueTests
{
  [Fact]
  public async Task EnqueueAsync_ShouldAddRequestToQueue()
  {
    // Arrange
    var queue = new InMemoryQueue(10);
    var request = new TestRequest("test-data");
    var metadata = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        request,
        new TaskCompletionSource<TestResponse>());

    // Act
    var requestId = await queue.EnqueueAsync<TestResponse>(metadata, CancellationToken.None);

    // Assert
    Assert.Equal(metadata.RequestId, requestId);
  }

  [Fact]
  public async Task DequeueAsync_ShouldReturnEnqueuedRequest()
  {
    // Arrange
    var queue = new InMemoryQueue(10);
    var request = new TestRequest("test-data");
    var metadata = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        request,
        new TaskCompletionSource<TestResponse>());

    await queue.EnqueueAsync<TestResponse>(metadata, CancellationToken.None);

    // Act
    var dequeuedMetadata = await queue.DequeueAsync(CancellationToken.None);

    // Assert
    Assert.NotNull(dequeuedMetadata);
    Assert.Equal(metadata.RequestId, dequeuedMetadata.RequestId);
    var typedMetadata = Assert.IsType<RequestMetadata<TestResponse>>(dequeuedMetadata);
    Assert.Equal(request, typedMetadata.Request);
  }

  [Fact]
  public async Task EnqueueDequeue_ShouldMaintainFifoOrder()
  {
    // Arrange
    var queue = new InMemoryQueue(10);
    var requests = new List<RequestMetadata<TestResponse>>();

    for (int i = 0; i < 5; i++)
    {
      var metadata = new RequestMetadata<TestResponse>(
          Guid.NewGuid(),
          new TestRequest($"request-{i}"),
          new TaskCompletionSource<TestResponse>());
      requests.Add(metadata);
      await queue.EnqueueAsync<TestResponse>(metadata, CancellationToken.None);
    }

    // Act & Assert
    for (int i = 0; i < 5; i++)
    {
      var dequeued = await queue.DequeueAsync(CancellationToken.None);
      Assert.Equal(requests[i].RequestId, dequeued.RequestId);
    }
  }

  [Theory]
  [InlineData(1)]
  [InlineData(5)]
  [InlineData(100)]
  [InlineData(1000)]
  public async Task Constructor_WithVariousQueueSizes_ShouldCreateQueue(int maxQueueSize)
  {
    // Arrange & Act
    var queue = new InMemoryQueue(maxQueueSize);
    var metadata = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("test"),
        new TaskCompletionSource<TestResponse>());

    // Assert - Should not throw
    await queue.EnqueueAsync<TestResponse>(metadata, CancellationToken.None);
    var dequeued = await queue.DequeueAsync(CancellationToken.None);
    Assert.Equal(metadata.RequestId, dequeued.RequestId);
  }

  [Fact]
  public async Task EnqueueAsync_WhenQueueFull_ShouldWaitForSpace()
  {
    // Arrange
    var queue = new InMemoryQueue(2);
    var metadata1 = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("request-1"),
        new TaskCompletionSource<TestResponse>());
    var metadata2 = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("request-2"),
        new TaskCompletionSource<TestResponse>());
    var metadata3 = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("request-3"),
        new TaskCompletionSource<TestResponse>());

    await queue.EnqueueAsync<TestResponse>(metadata1, CancellationToken.None);
    await queue.EnqueueAsync<TestResponse>(metadata2, CancellationToken.None);

    // Act - Start enqueue that should block
    var enqueueTask = Task.Run(async () =>
        await queue.EnqueueAsync<TestResponse>(metadata3, CancellationToken.None));

    // Give it a moment to block
    await Task.Delay(50);
    Assert.False(enqueueTask.IsCompleted);

    // Dequeue to make space
    await queue.DequeueAsync(CancellationToken.None);

    // Now the enqueue should complete
    await enqueueTask;
    Assert.True(enqueueTask.IsCompleted);
  }

  [Fact]
  public async Task EnqueueAsync_WhenCancelled_ShouldThrowOperationCanceledException()
  {
    // Arrange
    var queue = new InMemoryQueue(1);
    var metadata1 = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("request-1"),
        new TaskCompletionSource<TestResponse>());
    var metadata2 = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("request-2"),
        new TaskCompletionSource<TestResponse>());

    await queue.EnqueueAsync<TestResponse>(metadata1, CancellationToken.None);

    var cts = new CancellationTokenSource();
    cts.Cancel();

    // Act & Assert
    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        await queue.EnqueueAsync<TestResponse>(metadata2, cts.Token));
  }

  [Fact]
  public async Task DequeueAsync_WhenCancelled_ShouldThrowOperationCanceledException()
  {
    // Arrange
    var queue = new InMemoryQueue(10);
    var cts = new CancellationTokenSource();
    cts.Cancel();

    // Act & Assert
    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        await queue.DequeueAsync(cts.Token));
  }

  [Fact]
  public async Task DequeueAsync_WhenQueueEmpty_ShouldWaitForItem()
  {
    // Arrange
    var queue = new InMemoryQueue(10);

    // Act - Start dequeue on empty queue
    var dequeueTask = Task.Run(async () =>
        await queue.DequeueAsync(CancellationToken.None));

    // Give it a moment to start waiting
    await Task.Delay(50);
    Assert.False(dequeueTask.IsCompleted);

    // Add an item
    var metadata = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("test"),
        new TaskCompletionSource<TestResponse>());
    await queue.EnqueueAsync<TestResponse>(metadata, CancellationToken.None);

    // Now the dequeue should complete
    var result = await dequeueTask;
    Assert.Equal(metadata.RequestId, result.RequestId);
  }

  [Fact]
  public async Task ConcurrentEnqueueDequeue_ShouldHandleMultipleThreads()
  {
    // Arrange
    var queue = new InMemoryQueue(100);
    var requestCount = 50;
    var enqueuedIds = new List<Guid>();
    var dequeuedIds = new List<Guid>();
    var enqueueLock = new object();
    var dequeueLock = new object();

    // Act - Enqueue and dequeue concurrently
    var enqueueTasks = Enumerable.Range(0, requestCount).Select(async i =>
    {
      var metadata = new RequestMetadata<TestResponse>(
              Guid.NewGuid(),
              new TestRequest($"request-{i}"),
              new TaskCompletionSource<TestResponse>());

      lock (enqueueLock)
      {
        enqueuedIds.Add(metadata.RequestId);
      }

      await queue.EnqueueAsync<TestResponse>(metadata, CancellationToken.None);
    });

    var dequeueTasks = Enumerable.Range(0, requestCount).Select(async i =>
    {
      var metadata = await queue.DequeueAsync(CancellationToken.None);

      lock (dequeueLock)
      {
        dequeuedIds.Add(metadata.RequestId);
      }
    });

    await Task.WhenAll(enqueueTasks.Concat(dequeueTasks));

    // Assert
    Assert.Equal(requestCount, enqueuedIds.Count);
    Assert.Equal(requestCount, dequeuedIds.Count);
    Assert.All(enqueuedIds, id => Assert.Contains(id, dequeuedIds));
  }

  [Theory]
  [InlineData(5, 3)]
  [InlineData(10, 7)]
  [InlineData(20, 15)]
  public async Task MultipleRequestTypes_ShouldBeHandledCorrectly(int testRequests, int emptyRequests)
  {
    // Arrange
    var queue = new InMemoryQueue(100);
    var allRequestIds = new List<Guid>();

    // Enqueue different request types
    for (int i = 0; i < testRequests; i++)
    {
      var metadata = new RequestMetadata<TestResponse>(
          Guid.NewGuid(),
          new TestRequest($"test-{i}"),
          new TaskCompletionSource<TestResponse>());
      allRequestIds.Add(metadata.RequestId);
      await queue.EnqueueAsync<TestResponse>(metadata, CancellationToken.None);
    }

    for (int i = 0; i < emptyRequests; i++)
    {
      var metadata = new RequestMetadata<EmptyResponse>(
          Guid.NewGuid(),
          new EmptyRequest(),
          new TaskCompletionSource<EmptyResponse>());
      allRequestIds.Add(metadata.RequestId);
      await queue.EnqueueAsync<EmptyResponse>(metadata, CancellationToken.None);
    }

    // Act - Dequeue all
    var dequeuedIds = new List<Guid>();
    for (int i = 0; i < testRequests + emptyRequests; i++)
    {
      var metadata = await queue.DequeueAsync(CancellationToken.None);
      dequeuedIds.Add(metadata.RequestId);
    }

    // Assert
    Assert.Equal(allRequestIds.Count, dequeuedIds.Count);
    Assert.Equal(allRequestIds, dequeuedIds); // Should maintain FIFO order
  }
}
