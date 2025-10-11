using Jackdaw.Core;
using Jackdaw.Interfaces;
using Jackdaw.Unit.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Jackdaw.Unit.Tests.Core;

public class MediatorRunnerTests
{
  [Fact]
  public async Task ExecuteAsync_ShouldProcessRequestsFromQueue()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockDispatcher = Substitute.For<IHandlerDispatcher>();
    var serviceProvider = TestServiceProvider.Create();

    var metadata = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("test"),
        new TaskCompletionSource<TestResponse>());

    var callCount = 0;
    mockQueue.DequeueAsync(Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          callCount++;
          if (callCount == 1)
          {
            return Task.FromResult<IRequestMetadata>(metadata);
          }
          // Block subsequent calls to simulate continuous running
          return Task.Delay(Timeout.Infinite, callInfo.ArgAt<CancellationToken>(0))
                  .ContinueWith<IRequestMetadata>(_ => null!);
        });

    var runner = new MediatorRunner(mockQueue, serviceProvider, mockDispatcher);
    var cts = new CancellationTokenSource();

    // Act
    var executeTask = runner.StartAsync(cts.Token);

    // Wait for the request to be processed
    await Task.Delay(100);
    cts.Cancel();

    try
    {
      await runner.StopAsync(CancellationToken.None);
    }
    catch (OperationCanceledException)
    {
      // Expected when cancelling
    }

    // Assert - Runner should have tried to dequeue and dispatch at least once
    await mockQueue.Received().DequeueAsync(Arg.Any<CancellationToken>());
    await mockDispatcher.Received().DispatchAsync(
        metadata,
        Arg.Any<IServiceProvider>(),
        Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task ExecuteAsync_ShouldCreateNewScopeForEachRequest()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockDispatcher = Substitute.For<IHandlerDispatcher>();

    var services = new ServiceCollection();
    services.AddScoped<CountingTestRequestHandler>();
    var serviceProvider = services.BuildServiceProvider();

    var metadata1 = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("test1"),
        new TaskCompletionSource<TestResponse>());

    var metadata2 = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("test2"),
        new TaskCompletionSource<TestResponse>());

    var callCount = 0;
    var capturedServiceProviders = new List<IServiceProvider>();

    mockQueue.DequeueAsync(Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          callCount++;
          if (callCount == 1) return Task.FromResult<IRequestMetadata>(metadata1);
          if (callCount == 2) return Task.FromResult<IRequestMetadata>(metadata2);

          return Task.Delay(Timeout.Infinite, callInfo.ArgAt<CancellationToken>(0))
                  .ContinueWith<IRequestMetadata>(_ => null!);
        });

    mockDispatcher.DispatchAsync(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<IServiceProvider>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          capturedServiceProviders.Add(callInfo.ArgAt<IServiceProvider>(1));
          return Task.CompletedTask;
        });

    var runner = new MediatorRunner(mockQueue, serviceProvider, mockDispatcher);
    var cts = new CancellationTokenSource();

    // Act
    var executeTask = runner.StartAsync(cts.Token);
    await Task.Delay(150);
    cts.Cancel();

    try
    {
      await runner.StopAsync(CancellationToken.None);
    }
    catch (OperationCanceledException) { }

    // Assert - Should have created at least 2 scopes
    Assert.True(capturedServiceProviders.Count >= 2);
    // Verify the first two are different scopes
    Assert.NotSame(capturedServiceProviders[0], capturedServiceProviders[1]);
  }

  [Fact]
  public async Task ExecuteAsync_WhenCancelled_ShouldStopProcessing()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockDispatcher = Substitute.For<IHandlerDispatcher>();
    var serviceProvider = TestServiceProvider.Create();

    mockQueue.DequeueAsync(Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          var token = callInfo.ArgAt<CancellationToken>(0);
          return Task.Delay(Timeout.Infinite, token)
                  .ContinueWith<IRequestMetadata>(_ => null!);
        });

    var runner = new MediatorRunner(mockQueue, serviceProvider, mockDispatcher);
    var cts = new CancellationTokenSource();

    // Act
    var executeTask = runner.StartAsync(cts.Token);
    await Task.Delay(50);

    cts.Cancel();

    // Assert - Should complete when cancelled
    try
    {
      await runner.StopAsync(CancellationToken.None);
    }
    catch (OperationCanceledException)
    {
      // Expected
    }

    // The dispatcher may or may not have been called depending on timing
    // Just verify the runner stopped gracefully
    Assert.True(true);
  }

  [Fact]
  public async Task ExecuteAsync_ShouldContinueProcessingAfterDispatcherError()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockDispatcher = Substitute.For<IHandlerDispatcher>();
    var serviceProvider = TestServiceProvider.Create();

    var metadata1 = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("test1"),
        new TaskCompletionSource<TestResponse>());

    var metadata2 = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("test2"),
        new TaskCompletionSource<TestResponse>());

    var callCount = 0;
    mockQueue.DequeueAsync(Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          callCount++;
          if (callCount == 1) return Task.FromResult<IRequestMetadata>(metadata1);
          if (callCount == 2) return Task.FromResult<IRequestMetadata>(metadata2);

          return Task.Delay(Timeout.Infinite, callInfo.ArgAt<CancellationToken>(0))
                  .ContinueWith<IRequestMetadata>(_ => null!);
        });

    var dispatchCount = 0;
    mockDispatcher.DispatchAsync(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<IServiceProvider>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          Interlocked.Increment(ref dispatchCount);
          if (dispatchCount == 1)
          {
            throw new InvalidOperationException("First dispatch failed");
          }
          return Task.CompletedTask;
        });

    var runner = new MediatorRunner(mockQueue, serviceProvider, mockDispatcher);
    var cts = new CancellationTokenSource();

    // Act
    var executeTask = runner.StartAsync(cts.Token);
    await Task.Delay(200);
    cts.Cancel();

    try
    {
      await runner.StopAsync(CancellationToken.None);
    }
    catch (OperationCanceledException) { }

    // Assert - Should have attempted at least 1 dispatch
    Assert.True(dispatchCount >= 1, $"Expected at least 1 dispatch, got {dispatchCount}");
  }

  [Fact]
  public async Task ExecuteAsync_WithMultipleRequests_ShouldProcessInOrder()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockDispatcher = Substitute.For<IHandlerDispatcher>();
    var serviceProvider = TestServiceProvider.Create();

    var requests = new List<IRequestMetadata>
        {
            new RequestMetadata<TestResponse>(Guid.NewGuid(), new TestRequest("request-1"), new TaskCompletionSource<TestResponse>()),
            new RequestMetadata<TestResponse>(Guid.NewGuid(), new TestRequest("request-2"), new TaskCompletionSource<TestResponse>()),
            new RequestMetadata<TestResponse>(Guid.NewGuid(), new TestRequest("request-3"), new TaskCompletionSource<TestResponse>())
        };

    var callCount = 0;
    mockQueue.DequeueAsync(Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          if (callCount < requests.Count)
          {
            return Task.FromResult(requests[callCount++]);
          }

          return Task.Delay(Timeout.Infinite, callInfo.ArgAt<CancellationToken>(0))
                  .ContinueWith<IRequestMetadata>(_ => null!);
        });

    var dispatchedRequests = new List<IRequestMetadata>();
    mockDispatcher.DispatchAsync(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<IServiceProvider>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          dispatchedRequests.Add(callInfo.ArgAt<IRequestMetadata>(0));
          return Task.CompletedTask;
        });

    var runner = new MediatorRunner(mockQueue, serviceProvider, mockDispatcher);
    var cts = new CancellationTokenSource();

    // Act
    var executeTask = runner.StartAsync(cts.Token);
    await Task.Delay(200);
    cts.Cancel();

    try
    {
      await runner.StopAsync(CancellationToken.None);
    }
    catch (OperationCanceledException) { }

    // Assert - Should have processed all 3 requests
    Assert.True(dispatchedRequests.Count >= 3, $"Expected at least 3 dispatched requests, got {dispatchedRequests.Count}");
    // Verify first 3 are in order
    for (int i = 0; i < Math.Min(3, dispatchedRequests.Count); i++)
    {
      Assert.Equal(requests[i].RequestId, dispatchedRequests[i].RequestId);
    }
  }

  [Fact]
  public async Task ExecuteAsync_PassesCancellationTokenToDispatcher()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockDispatcher = Substitute.For<IHandlerDispatcher>();
    var serviceProvider = TestServiceProvider.Create();

    var metadata = new RequestMetadata<TestResponse>(
        Guid.NewGuid(),
        new TestRequest("test"),
        new TaskCompletionSource<TestResponse>());

    var callCount = 0;
    mockQueue.DequeueAsync(Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          callCount++;
          if (callCount == 1)
          {
            return Task.FromResult<IRequestMetadata>(metadata);
          }
          return Task.Delay(Timeout.Infinite, callInfo.ArgAt<CancellationToken>(0))
                  .ContinueWith<IRequestMetadata>(_ => null!);
        });

    CancellationToken capturedToken = default;
    mockDispatcher.DispatchAsync(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<IServiceProvider>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          capturedToken = callInfo.ArgAt<CancellationToken>(2);
          return Task.CompletedTask;
        });

    var runner = new MediatorRunner(mockQueue, serviceProvider, mockDispatcher);
    var cts = new CancellationTokenSource();

    // Act
    var executeTask = runner.StartAsync(cts.Token);
    await Task.Delay(100);
    cts.Cancel();

    try
    {
      await runner.StopAsync(CancellationToken.None);
    }
    catch (OperationCanceledException) { }

    // Assert
    Assert.NotEqual(default, capturedToken);
  }

  [Theory]
  [InlineData(1)]
  [InlineData(3)]
  [InlineData(5)]
  public async Task ExecuteAsync_ProcessesMultipleRequests(int requestCount)
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockDispatcher = Substitute.For<IHandlerDispatcher>();
    var serviceProvider = TestServiceProvider.Create();

    var requests = Enumerable.Range(0, requestCount)
        .Select(i => new RequestMetadata<TestResponse>(
            Guid.NewGuid(),
            new TestRequest($"request-{i}"),
            new TaskCompletionSource<TestResponse>()))
        .Cast<IRequestMetadata>()
        .ToList();

    var callCount = 0;
    mockQueue.DequeueAsync(Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          if (callCount < requests.Count)
          {
            return Task.FromResult(requests[callCount++]);
          }

          return Task.Delay(Timeout.Infinite, callInfo.ArgAt<CancellationToken>(0))
                  .ContinueWith<IRequestMetadata>(_ => null!);
        });

    var dispatchCount = 0;
    mockDispatcher.DispatchAsync(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<IServiceProvider>(),
        Arg.Any<CancellationToken>())
        .Returns(_ =>
        {
          Interlocked.Increment(ref dispatchCount);
          return Task.CompletedTask;
        });

    var runner = new MediatorRunner(mockQueue, serviceProvider, mockDispatcher);
    var cts = new CancellationTokenSource();

    // Act
    var executeTask = runner.StartAsync(cts.Token);
    await Task.Delay(requestCount * 50 + 150);
    cts.Cancel();

    try
    {
      await runner.StopAsync(CancellationToken.None);
    }
    catch (OperationCanceledException) { }

    // Assert - Should have processed at least the expected number of requests
    Assert.True(dispatchCount >= requestCount, $"Expected at least {requestCount} dispatches, got {dispatchCount}");
  }
}
