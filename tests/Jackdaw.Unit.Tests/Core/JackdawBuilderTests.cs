using Jackdaw.Core;
using Jackdaw.Interfaces;
using Jackdaw.Queues.InMemory;
using Jackdaw.Unit.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jackdaw.Unit.Tests.Core;

public class QueueBuilderTests
{
  [Fact]
  public void AddJackdawQueue_WithInMemory_ShouldRegisterQueueInServices()
  {
    // Arrange
    var services = new ServiceCollection();
    var builder = new JackdawBuilder(services);

    // Act
    builder.AddQueue("TestQueue").UseInMemory().AsDefault();
    var serviceProvider = services.BuildServiceProvider();
    var queue = serviceProvider.GetKeyedService<IMessageQueue>("TestQueue");

    // Assert
    Assert.NotNull(queue);
  }

  [Fact]
  public void AddJackdawQueue_WithDefaultMaxSize_ShouldRegisterSuccessfully()
  {
    // Arrange
    var services = new ServiceCollection();

    // Act
    var builder = new JackdawBuilder(services);
    builder.AddQueue("DefaultQueue").UseInMemory();
    var serviceProvider = services.BuildServiceProvider();
    var queue = serviceProvider.GetKeyedService<IMessageQueue>("DefaultQueue");

    // Assert
    Assert.NotNull(queue);
    Assert.IsAssignableFrom<IMessageQueue>(queue);
  }

  [Theory]
  [InlineData(10)]
  [InlineData(100)]
  [InlineData(500)]
  [InlineData(2048)]
  public void AddJackdawQueue_WithCustomMaxQueueSize_ShouldConfigureCorrectly(int maxQueueSize)
  {
    // Arrange
    var services = new ServiceCollection();

    // Act
    var builder = new JackdawBuilder(services);
    builder.AddQueue("CustomQueue").UseInMemory(new(maxQueueSize));
    var serviceProvider = services.BuildServiceProvider();
    var queue = serviceProvider.GetKeyedService<IMessageQueue>("CustomQueue");

    // Assert
    Assert.NotNull(queue);
  }

  [Fact]
  public void AddJackdawQueue_MultipleQueues_ShouldRegisterAll()
  {
    // Arrange
    var services = new ServiceCollection();

    // Act
    var builder = new JackdawBuilder(services);
    builder.AddQueue("Queue1").UseInMemory();
    builder.AddQueue("Queue2").UseInMemory();
    var serviceProvider = services.BuildServiceProvider();

    // Assert
    var queue1 = serviceProvider.GetKeyedService<IMessageQueue>("Queue1");
    var queue2 = serviceProvider.GetKeyedService<IMessageQueue>("Queue2");
    Assert.NotNull(queue1);
    Assert.NotNull(queue2);
  }

  [Fact]
  public void AddJackdawQueue_WithAsDefault_ShouldRegisterDefaultQueue()
  {
    // Arrange
    var services = new ServiceCollection();

    // Act
    var builder = new JackdawBuilder(services);
    builder.AddQueue("DefaultQueue").UseInMemory().AsDefault();
    var serviceProvider = services.BuildServiceProvider();

    // Assert
    var queue = serviceProvider.GetKeyedService<IMessageQueue>("DefaultQueue");
    var defaultQueue = serviceProvider.GetService<IDefaultMessageQueue>();

    Assert.NotNull(queue);
    Assert.NotNull(defaultQueue);
  }

  [Fact]
  public void QueueBuilder_CanChainMultipleConfigurations()
  {
    // Arrange
    var services = new ServiceCollection();

    // Act
    var builder = new JackdawBuilder(services);
    builder.AddQueue("ChainedQueue").UseInMemory()
        .AsDefault();

    var serviceProvider = services.BuildServiceProvider();

    // Assert
    var queue = serviceProvider.GetKeyedService<IMessageQueue>("ChainedQueue");
    var defaultQueue = serviceProvider.GetService<IDefaultMessageQueue>();

    Assert.NotNull(queue);
    Assert.NotNull(defaultQueue);
  }

  [Fact]
  public void InMemoryQueueOptions_DefaultValues_ShouldBeSet()
  {
    // Arrange & Act
    var options = new InMemoryQueueOptions();

    // Assert
    Assert.True(options.MaxQueueSize > 0);

  }
}