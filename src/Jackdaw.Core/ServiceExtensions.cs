
using Jackdaw.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Jackdaw.Core;

public class JackdawBuilder(
    IServiceCollection Services)
{
  private HashSet<QueueBuilder> _queueBuilders = new();
  public QueueBuilder AddQueue(
      string name)
  {
    var queueBuilder = new QueueBuilder(name, Services);
    _queueBuilders.Add(queueBuilder);
    return queueBuilder;
  }

  public bool Valid()
  {
    foreach (var builder in _queueBuilders)
    {
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