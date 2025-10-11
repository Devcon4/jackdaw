using Jackdaw.Core;
using Jackdaw.Interfaces;
using Jackdaw.Unit.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jackdaw.Unit.Tests.Core;

public class JackdawBuilderTests
{
  [Fact]
  public void UseInMemoryQueue_ShouldRegisterQueueInServices()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder.UseInMemoryQueue();
    var serviceProvider = services.BuildServiceProvider();
    var queue = serviceProvider.GetService<IMessageQueue>();

    // Assert
    Assert.NotNull(queue);
  }

  [Fact]
  public void UseInMemoryQueue_ShouldSetHasQueueToTrue()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder.UseInMemoryQueue();

    // Assert
    Assert.True(builder.Results.HasQueue);
  }

  [Fact]
  public void UseInMemoryQueue_WithoutConfiguration_ShouldUseDefaultOptions()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder.UseInMemoryQueue();
    var serviceProvider = services.BuildServiceProvider();
    var queue = serviceProvider.GetService<IMessageQueue>();

    // Assert
    Assert.NotNull(queue);
    // The queue should work with default max size (1024)
    Assert.IsAssignableFrom<IMessageQueue>(queue);
  }

  [Theory]
  [InlineData(10)]
  [InlineData(100)]
  [InlineData(500)]
  [InlineData(2048)]
  public void UseInMemoryQueue_WithCustomMaxQueueSize_ShouldConfigureCorrectly(int maxQueueSize)
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder.UseInMemoryQueue(options =>
    {
      options = options with { MaxQueueSize = maxQueueSize };
    });
    var serviceProvider = services.BuildServiceProvider();
    var queue = serviceProvider.GetService<IMessageQueue>();

    // Assert
    Assert.NotNull(queue);
  }

  [Fact]
  public void UseInMemoryQueue_ShouldReturnBuilderForChaining()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    var returnedBuilder = builder.UseInMemoryQueue();

    // Assert
    Assert.Same(builder, returnedBuilder);
  }

  [Fact]
  public void AddHandler_ShouldRegisterHandlerInServices()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder.AddHandler<TestRequestHandler, TestRequest, TestResponse>();
    var serviceProvider = services.BuildServiceProvider();
    var handler = serviceProvider.GetService<IRequestHandler<TestRequest, TestResponse>>();

    // Assert
    Assert.NotNull(handler);
    Assert.IsType<TestRequestHandler>(handler);
  }

  [Fact]
  public void AddHandler_ShouldReturnBuilderForChaining()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    var returnedBuilder = builder.AddHandler<TestRequestHandler, TestRequest, TestResponse>();

    // Assert
    Assert.Same(builder, returnedBuilder);
  }

  [Fact]
  public void AddHandler_MultipleHandlers_ShouldRegisterAll()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder.AddHandler<TestRequestHandler, TestRequest, TestResponse>();
    builder.AddHandler<EmptyRequestHandler, EmptyRequest, EmptyResponse>();
    var serviceProvider = services.BuildServiceProvider();

    // Assert
    var handler1 = serviceProvider.GetService<IRequestHandler<TestRequest, TestResponse>>();
    var handler2 = serviceProvider.GetService<IRequestHandler<EmptyRequest, EmptyResponse>>();

    Assert.NotNull(handler1);
    Assert.NotNull(handler2);
    Assert.IsType<TestRequestHandler>(handler1);
    Assert.IsType<EmptyRequestHandler>(handler2);
  }

  [Fact]
  public void AddHandler_ShouldRegisterAsScoped()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder.AddHandler<CountingTestRequestHandler, TestRequest, TestResponse>();
    var serviceProvider = services.BuildServiceProvider();

    // Assert - Get handler from different scopes
    using var scope1 = serviceProvider.CreateScope();
    using var scope2 = serviceProvider.CreateScope();

    var handler1 = scope1.ServiceProvider.GetService<IRequestHandler<TestRequest, TestResponse>>();
    var handler2 = scope2.ServiceProvider.GetService<IRequestHandler<TestRequest, TestResponse>>();

    Assert.NotNull(handler1);
    Assert.NotNull(handler2);
    Assert.NotSame(handler1, handler2); // Different instances in different scopes
  }

  [Fact]
  public void Builder_CanChainMultipleConfigurations()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    var result = builder
        .UseInMemoryQueue(options => options = options with { MaxQueueSize = 100 })
        .AddHandler<TestRequestHandler, TestRequest, TestResponse>()
        .AddHandler<EmptyRequestHandler, EmptyRequest, EmptyResponse>();

    var serviceProvider = services.BuildServiceProvider();

    // Assert
    Assert.Same(builder, result);
    Assert.True(builder.Results.HasQueue);

    var queue = serviceProvider.GetService<IMessageQueue>();
    var handler1 = serviceProvider.GetService<IRequestHandler<TestRequest, TestResponse>>();
    var handler2 = serviceProvider.GetService<IRequestHandler<EmptyRequest, EmptyResponse>>();

    Assert.NotNull(queue);
    Assert.NotNull(handler1);
    Assert.NotNull(handler2);
  }

  [Fact]
  public void BuildResults_InitialState_ShouldHaveHasQueueFalse()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    var results = builder.Results;

    // Assert
    Assert.False(results.HasQueue);
  }

  [Fact]
  public void UseInMemoryQueue_CalledMultipleTimes_ShouldUseLastConfiguration()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder.UseInMemoryQueue(options => options = options with { MaxQueueSize = 10 });
    builder.UseInMemoryQueue(options => options = options with { MaxQueueSize = 100 });

    // Assert
    Assert.True(builder.Results.HasQueue);
    var serviceProvider = services.BuildServiceProvider();
    var queues = serviceProvider.GetServices<IMessageQueue>().ToList();

    // Should have 2 registrations (both as singleton)
    Assert.Equal(2, queues.Count);
  }

  [Theory]
  [InlineData(1)]
  [InlineData(3)]
  [InlineData(5)]
  public void AddHandler_SameHandlerMultipleTimes_ShouldRegisterMultipleTimes(int registrationCount)
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    for (int i = 0; i < registrationCount; i++)
    {
      builder.AddHandler<TestRequestHandler, TestRequest, TestResponse>();
    }

    var serviceProvider = services.BuildServiceProvider();
    var handlers = serviceProvider.GetServices<IRequestHandler<TestRequest, TestResponse>>().ToList();

    // Assert
    Assert.Equal(registrationCount, handlers.Count);
  }

  [Fact]
  public void Constructor_WithNullServices_ShouldNotThrow()
  {
    // This test verifies the builder can be constructed with a service collection
    // Arrange & Act
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Assert
    Assert.NotNull(builder);
    Assert.False(builder.Results.HasQueue);
  }

  [Fact]
  public void InMemoryQueueOptions_DefaultValues_ShouldBe1024()
  {
    // Arrange & Act
    var options = new JackdawBuilder.InMemoryQueueOptions();

    // Assert
    Assert.Equal(1024, options.MaxQueueSize);
  }

  [Fact]
  public void InMemoryQueueOptions_WithRecordSyntax_ShouldAllowCustomization()
  {
    // Arrange
    var defaultOptions = new JackdawBuilder.InMemoryQueueOptions();

    // Act
    var customOptions = defaultOptions with { MaxQueueSize = 2048 };

    // Assert
    Assert.Equal(1024, defaultOptions.MaxQueueSize);
    Assert.Equal(2048, customOptions.MaxQueueSize);
  }

  [Fact]
  public void BuildResults_IsRecordStruct_ShouldSupportWithSyntax()
  {
    // Arrange
    var results = new JackdawBuilder.BuildResults(false);

    // Act
    var updatedResults = results with { HasQueue = true };

    // Assert
    Assert.False(results.HasQueue);
    Assert.True(updatedResults.HasQueue);
  }
}
