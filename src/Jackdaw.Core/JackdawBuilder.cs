
using Jackdaw.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Jackdaw.Core;

public class JackdawBuilder(
    IServiceCollection Services)
{
  private readonly HashSet<QueueBuilder> _queueBuilders = new();
  private readonly HashSet<Type> _globalMiddlewares = new();
  public QueueBuilder AddQueue(
      string name)
  {
    var queueBuilder = new QueueBuilder(name, Services);
    _queueBuilders.Add(queueBuilder);
    return queueBuilder;
  }
  public JackdawBuilder UseMiddleware<TMiddleware>()
      where TMiddleware : class, IPipelineBehavior
  {
    // Register the middleware globally
    Services.AddScoped<IPipelineBehavior, TMiddleware>();
    _globalMiddlewares.Add(typeof(TMiddleware));
    return this;
  }

  public bool Initialize()
  {
    foreach (var builder in _queueBuilders)
    {
      builder.SetGlobalMiddlewares(_globalMiddlewares);
      if (!builder.Valid())
      {
        return false;
      }
    }
    return true;
  }
}

// public static class ServiceExtensions
// {
//   public static JackdawBuilder AddJackdaw(
//       this IServiceCollection services,
//       Action<JackdawBuilder> configure)
//   {
//     var builder = new JackdawBuilder(services);
//     configure(builder);
//     return builder;
//   }
// }