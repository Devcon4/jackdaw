
using Jackdaw.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Jackdaw.Core;

public class JackdawBuilder(
    IServiceCollection Services)
{
  private readonly HashSet<QueueBuilder> _queueBuilders = new();
  private readonly HashSet<Type> _globalMiddlewares = new();
  private Func<JackdawBuilder, (bool, Exception?)>? _validations;
  public QueueBuilder AddQueue(
      string name)
  {
    var queueBuilder = new QueueBuilder(name, Services);
    _queueBuilders.Add(queueBuilder);

    AddValidation(b => (b._queueBuilders.Count(q => q.QueueName == name) <= 1, new InvalidOperationException($"A queue with the name '{name}' has already been registered. Queue names must be unique.")));
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

  public JackdawBuilder AddValidation(Func<JackdawBuilder, (bool, Exception?)> validation)
  {
    _validations += validation;
    return this;
  }

  public HashSet<string> GetQueueNames() => [.. _queueBuilders.Select(b => b.QueueName)];

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

  public bool Valid()
  {
    if (_validations is null)
    {
      return true;
    }

    var (isValid, exception) = _validations(this);
    exception ??= new InvalidOperationException("Unknown validation error.");
    if (!isValid)
    {
      throw exception;
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