using Jackdaw.Core;
using Jackdaw.Integration.Tests.TestHelpers;
using Jackdaw.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jackdaw.Integration.Tests;

/// <summary>
/// Integration tests that verify component interactions without requiring generated code
/// Full end-to-end tests would require the source generator output
/// </summary>
public class ServiceRegistrationTests
{
  [Fact]
  public void Builder_WithQueueAndHandlers_ShouldRegisterAllComponents()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder
        .UseInMemoryQueue(options => options = options with { MaxQueueSize = 100 })
        .AddHandler<TestRequestHandler, TestRequest, TestResponse>()
        .AddHandler<EmptyRequestHandler, EmptyRequest, EmptyResponse>();

    var serviceProvider = services.BuildServiceProvider();

    // Assert
    Assert.True(builder.Results.HasQueue);
    Assert.NotNull(serviceProvider.GetService<IMessageQueue>());
    Assert.NotNull(serviceProvider.GetService<IRequestHandler<TestRequest, TestResponse>>());
    Assert.NotNull(serviceProvider.GetService<IRequestHandler<EmptyRequest, EmptyResponse>>());
  }

  [Fact]
  public async Task HandlerExecution_DirectCall_ShouldProcessRequest()
  {
    // Arrange
    var handler = new TestRequestHandler();
    var request = new TestRequest("direct-test");

    // Act
    var response = await handler.Handle(request, CancellationToken.None);

    // Assert
    Assert.NotNull(response);
    Assert.Equal("Processed: direct-test", response.Value);
  }

  [Theory]
  [InlineData("data-1")]
  [InlineData("data-2")]
  [InlineData("special-characters-!@#")]
  public async Task HandlerExecution_WithVariousInputs_ShouldProcessCorrectly(string inputData)
  {
    // Arrange
    var handler = new TestRequestHandler();
    var request = new TestRequest(inputData);

    // Act
    var response = await handler.Handle(request, CancellationToken.None);

    // Assert
    Assert.Contains(inputData, response.Value);
  }

  [Fact]
  public async Task EmptyRequestHandler_ShouldReturnEmptyResponse()
  {
    // Arrange
    var handler = new EmptyRequestHandler();
    var request = new EmptyRequest();

    // Act
    var response = await handler.Handle(request, CancellationToken.None);

    // Assert
    Assert.NotNull(response);
  }

  [Fact]
  public async Task ErrorRequestHandler_ShouldThrowException()
  {
    // Arrange
    var handler = new ErrorRequestHandler();
    var request = new ErrorRequest();

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        async () => await handler.Handle(request, CancellationToken.None));

    Assert.Equal("Test error from handler", exception.Message);
  }

  [Fact]
  public async Task SlowRequestHandler_ShouldRespectDelay()
  {
    // Arrange
    var handler = new SlowRequestHandler();
    var delayMs = 100;
    var request = new SlowRequest(delayMs);
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    // Act
    var response = await handler.Handle(request, CancellationToken.None);
    stopwatch.Stop();

    // Assert
    Assert.Contains($"{delayMs}ms", response.Value);
    Assert.True(stopwatch.ElapsedMilliseconds >= delayMs - 10); // Allow small variance
  }

  [Fact]
  public async Task CountingHandler_ShouldTrackMultipleCalls()
  {
    // Arrange
    var handler = new CountingTestRequestHandler();
    var requests = new[]
    {
            new TestRequest("call-1"),
            new TestRequest("call-2"),
            new TestRequest("call-3")
        };

    // Act
    var responses = new List<TestResponse>();
    foreach (var request in requests)
    {
      responses.Add(await handler.Handle(request, CancellationToken.None));
    }

    // Assert
    Assert.Equal(3, handler.CallCount);
    Assert.Equal(3, handler.ReceivedRequests.Count);
    Assert.Equal(requests, handler.ReceivedRequests);
    Assert.Contains("Call #1", responses[0].Value);
    Assert.Contains("Call #2", responses[1].Value);
    Assert.Contains("Call #3", responses[2].Value);
  }

  [Fact]
  public async Task SlowRequestHandler_WithCancellation_ShouldThrow()
  {
    // Arrange
    var handler = new SlowRequestHandler();
    var request = new SlowRequest(5000); // 5 second delay
    var cts = new CancellationTokenSource();
    cts.CancelAfter(100); // Cancel after 100ms

    // Act & Assert
    await Assert.ThrowsAsync<TaskCanceledException>(
        async () => await handler.Handle(request, cts.Token));
  }

  [Fact]
  public void ServiceCollection_WithoutQueue_ShouldShowBuilderState()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder.AddHandler<TestRequestHandler, TestRequest, TestResponse>();
    // Not calling UseInMemoryQueue

    // Assert
    Assert.False(builder.Results.HasQueue);
  }

  [Fact]
  public void ServiceCollection_WithQueue_ShouldRegisterServices()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder.UseInMemoryQueue();
    builder.AddHandler<TestRequestHandler, TestRequest, TestResponse>();

    var serviceProvider = services.BuildServiceProvider();

    // Assert
    Assert.NotNull(serviceProvider.GetService<IMessageQueue>());
    Assert.NotNull(serviceProvider.GetService<IRequestHandler<TestRequest, TestResponse>>());
    Assert.True(builder.Results.HasQueue);
  }
}
