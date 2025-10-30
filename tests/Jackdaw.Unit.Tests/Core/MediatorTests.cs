using Jackdaw.Core;
using Jackdaw.Interfaces;
using Jackdaw.Unit.Tests.TestHelpers;
using NSubstitute;
using Xunit;

namespace Jackdaw.Unit.Tests.Core;

public class MediatorTests
{
  [Fact]
  public async Task Send_ShouldEnqueueRequestAndReturnResponse()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockRouter = Substitute.For<IQueueRouter>();
    mockRouter.GetQueue(Arg.Any<TestRequest>()).Returns(mockQueue);
    var mediator = new Mediator(mockRouter);

    var request = new TestRequest("test-data");

    // Configure mock to capture the metadata and complete it
    mockQueue.EnqueueAsync<TestResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          var metadata = callInfo.ArgAt<RequestMetadata<TestResponse>>(0);
          // Simulate the handler completing the request
          Task.Run(() => metadata.CompletionSource.SetResult(new TestResponse("Success")));
          return Task.FromResult(metadata.RequestId);
        });

    // Act
    var response = await mediator.Send<TestResponse>(request, CancellationToken.None);

    // Assert
    Assert.NotNull(response);
    Assert.Equal("Success", response.Value);
    await mockQueue.Received(1).EnqueueAsync<TestResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task Send_ShouldGenerateUniqueRequestIds()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockRouter = Substitute.For<IQueueRouter>();
    mockRouter.GetQueue(Arg.Any<TestRequest>()).Returns(mockQueue);
    var mediator = new Mediator(mockRouter);
    var capturedIds = new List<Guid>();

    mockQueue.EnqueueAsync<TestResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          var metadata = callInfo.ArgAt<RequestMetadata<TestResponse>>(0);
          capturedIds.Add(metadata.RequestId);
          Task.Run(() => metadata.CompletionSource.SetResult(new TestResponse("Done")));
          return Task.FromResult(metadata.RequestId);
        });

    // Act
    var requests = new[]
    {
            new TestRequest("request-1"),
            new TestRequest("request-2"),
            new TestRequest("request-3")
        };

    var tasks = requests.Select(r => mediator.Send<TestResponse>(r, CancellationToken.None));
    await Task.WhenAll(tasks);

    // Assert
    Assert.Equal(3, capturedIds.Count);
    Assert.Equal(capturedIds.Count, capturedIds.Distinct().Count()); // All IDs are unique
  }

  [Fact]
  public async Task Send_ShouldPassCorrectRequestToQueue()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockRouter = Substitute.For<IQueueRouter>();
    mockRouter.GetQueue(Arg.Any<TestRequest>()).Returns(mockQueue);
    var mediator = new Mediator(mockRouter);
    var request = new TestRequest("specific-data");
    RequestMetadata<TestResponse>? capturedMetadata = null;

    mockQueue.EnqueueAsync<TestResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          capturedMetadata = callInfo.ArgAt<RequestMetadata<TestResponse>>(0);
          Task.Run(() => capturedMetadata.Value.CompletionSource.SetResult(new TestResponse("Done")));
          return Task.FromResult(capturedMetadata.Value.RequestId);
        });

    // Act
    await mediator.Send<TestResponse>(request, CancellationToken.None);

    // Assert
    Assert.NotNull(capturedMetadata);
    Assert.Equal(request, capturedMetadata.Value.Request);
    Assert.NotEqual(Guid.Empty, capturedMetadata.Value.RequestId);
    Assert.NotNull(capturedMetadata.Value.CompletionSource);
  }

  [Fact]
  public async Task Send_WithCancellationToken_ShouldPassTokenToQueue()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockRouter = Substitute.For<IQueueRouter>();
    mockRouter.GetQueue(Arg.Any<TestRequest>()).Returns(mockQueue);
    var mediator = new Mediator(mockRouter);
    var request = new TestRequest("test");
    var cts = new CancellationTokenSource();

    mockQueue.EnqueueAsync<TestResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          var metadata = callInfo.ArgAt<RequestMetadata<TestResponse>>(0);
          var token = callInfo.ArgAt<CancellationToken>(1);
          Assert.Equal(cts.Token, token);
          Task.Run(() => metadata.CompletionSource.SetResult(new TestResponse("Done")));
          return Task.FromResult(metadata.RequestId);
        });

    // Act
    await mediator.Send<TestResponse>(request, cts.Token);

    // Assert
    await mockQueue.Received(1).EnqueueAsync<TestResponse>(
        Arg.Any<IRequestMetadata>(),
        cts.Token);
  }

  [Fact]
  public async Task Send_WhenQueueThrowsException_ShouldPropagateException()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockRouter = Substitute.For<IQueueRouter>();
    mockRouter.GetQueue(Arg.Any<TestRequest>()).Returns(mockQueue);
    var mediator = new Mediator(mockRouter);
    var request = new TestRequest("test");

    mockQueue.EnqueueAsync<TestResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>())
        .Returns<Task<Guid>>(_ => throw new InvalidOperationException("Queue is full"));

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        async () => await mediator.Send<TestResponse>(request, CancellationToken.None));

    Assert.Equal("Queue is full", exception.Message);
  }

  [Fact]
  public async Task Send_WhenCompletionSourceSetException_ShouldThrowException()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockRouter = Substitute.For<IQueueRouter>();
    mockRouter.GetQueue(Arg.Any<TestRequest>()).Returns(mockQueue);
    var mediator = new Mediator(mockRouter);
    var request = new TestRequest("test");

    mockQueue.EnqueueAsync<TestResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          var metadata = callInfo.ArgAt<RequestMetadata<TestResponse>>(0);
          Task.Run(() => metadata.CompletionSource.SetException(
                  new InvalidOperationException("Handler failed")));
          return Task.FromResult(metadata.RequestId);
        });

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        async () => await mediator.Send<TestResponse>(request, CancellationToken.None));

    Assert.Equal("Handler failed", exception.Message);
  }

  [Theory]
  [InlineData(1)]
  [InlineData(5)]
  [InlineData(10)]
  public async Task Send_MultipleConcurrentRequests_ShouldHandleAll(int requestCount)
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockRouter = Substitute.For<IQueueRouter>();
    mockRouter.GetQueue(Arg.Any<TestRequest>()).Returns(mockQueue);
    var mediator = new Mediator(mockRouter);
    var completedCount = 0;

    mockQueue.EnqueueAsync<TestResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          var metadata = callInfo.ArgAt<RequestMetadata<TestResponse>>(0);
          Task.Run(async () =>
              {
                await Task.Delay(10); // Simulate some processing time
                metadata.CompletionSource.SetResult(new TestResponse("Done"));
                Interlocked.Increment(ref completedCount);
              });
          return Task.FromResult(metadata.RequestId);
        });

    // Act
    var tasks = Enumerable.Range(0, requestCount)
        .Select(i => mediator.Send<TestResponse>(new TestRequest($"request-{i}"), CancellationToken.None))
        .ToList();

    var responses = await Task.WhenAll(tasks);

    // Assert
    Assert.Equal(requestCount, responses.Length);
    Assert.All(responses, r => Assert.Equal("Done", r.Value));

    // Wait a bit for all completions to be counted
    await Task.Delay(100);
    Assert.Equal(requestCount, completedCount);
  }

  [Fact]
  public async Task Send_WithDifferentResponseTypes_ShouldHandleCorrectly()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockRouter = Substitute.For<IQueueRouter>();
    mockRouter.GetQueue(Arg.Any<TestRequest>()).Returns(mockQueue);
    var mediator = new Mediator(mockRouter);

    mockQueue.EnqueueAsync<TestResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          var metadata = callInfo.ArgAt<RequestMetadata<TestResponse>>(0);
          Task.Run(() => metadata.CompletionSource.SetResult(new TestResponse("Test Response")));
          return Task.FromResult(metadata.RequestId);
        });

    mockQueue.EnqueueAsync<EmptyResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>())
        .Returns(callInfo =>
        {
          var metadata = callInfo.ArgAt<RequestMetadata<EmptyResponse>>(0);
          Task.Run(() => metadata.CompletionSource.SetResult(new EmptyResponse()));
          return Task.FromResult(metadata.RequestId);
        });

    // Act
    var testResponse = await mediator.Send<TestResponse>(new TestRequest("test"), CancellationToken.None);
    var emptyResponse = await mediator.Send<EmptyResponse>(new EmptyRequest(), CancellationToken.None);

    // Assert
    Assert.NotNull(testResponse);
    Assert.Equal("Test Response", testResponse.Value);
    Assert.NotNull(emptyResponse);
  }

  [Fact]
  public async Task Send_WhenCancelled_ShouldThrowOperationCanceledException()
  {
    // Arrange
    var mockQueue = Substitute.For<IMessageQueue>();
    var mockRouter = Substitute.For<IQueueRouter>();
    mockRouter.GetQueue(Arg.Any<TestRequest>()).Returns(mockQueue);
    var mediator = new Mediator(mockRouter);
    var request = new TestRequest("test");
    var cts = new CancellationTokenSource();

    mockQueue.EnqueueAsync<TestResponse>(
        Arg.Any<IRequestMetadata>(),
        Arg.Any<CancellationToken>())
        .Returns<Task<Guid>>(_ => throw new OperationCanceledException());

    cts.Cancel();

    // Act & Assert
    await Assert.ThrowsAsync<OperationCanceledException>(
        async () => await mediator.Send<TestResponse>(request, cts.Token));
  }
}
