using Microsoft.Extensions.DependencyInjection;

namespace Jackdaw.Integration.Tests.TestHelpers;

/// <summary>
/// Helper class to create test service providers with configured services
/// </summary>
public static class TestServiceProvider
{
  public static IServiceProvider Create(Action<IServiceCollection>? configure = null)
  {
    var services = new ServiceCollection();
    configure?.Invoke(services);
    return services.BuildServiceProvider();
  }

  public static IServiceScope CreateScope(Action<IServiceCollection>? configure = null)
  {
    var serviceProvider = Create(configure);
    return serviceProvider.CreateScope();
  }
}
