using System.Diagnostics.CodeAnalysis;
using Jackdaw.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jackdaw.Core;


// QueueRegistration.cs (internal record)
internal record QueueRegistration(
    string Name,
    Func<IServiceProvider, IMessageQueue> Factory,
    bool IsDefault,
    bool AllowMultipleHandlers
);

public record DefaultQueueName(string ActualQueueName);

public class QueueBuilder
{
  private readonly string _queueName;
  private readonly IServiceCollection _services;
  private bool _queueRegistered = false;
  private Func<QueueBuilder, (bool, Exception?)>? _validations;
  private readonly List<Type> _registeredMiddlewares = new();

  internal QueueBuilder(string queueName, IServiceCollection services)
  {
    _queueName = queueName;
    _services = services;

    AddValidation(b => (_queueName is "Default", new InvalidOperationException("The 'Default' queue is reserved and cannot be used as a queue name.")));
    AddValidation(b => (b._queueRegistered, new InvalidCastException($"Queue '{_queueName}' does not specify a queue implementation. Add UseInMemory or another queue implementation.")));
    AddValidation(b =>
    {
      var duplicates = b._registeredMiddlewares.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
      if (duplicates.Count != 0)
      {
        return (false, new InvalidOperationException($"Queue '{_queueName}' has duplicate middleware registrations: {string.Join(", ", duplicates.Select(t => t.Name))}. Each middleware can only be registered once per queue."));
      }
      return (true, null);
    });
  }

  // Core configuration methods
  public QueueBuilder AsDefault()
  {

    _services.AddKeyedSingleton("Default", (sp, key) => sp.GetRequiredKeyedService<IMessageQueue>(_queueName));
    _services.AddSingleton(new DefaultQueueName(_queueName));

    return this;
  }

  internal QueueBuilder AddValidation(Func<QueueBuilder, (bool, Exception?)> validation)
  {
    _validations += validation;
    return this;
  }

  internal bool Valid()
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

  public QueueBuilder UseQueue(Func<IServiceProvider, IMessageQueue> factory)
  {
    if (_queueRegistered)
    {
      AddValidation(b => (false, new InvalidOperationException($"Queue '{_queueName}' is already registered.")));
    }

    _queueRegistered = true;
    _services.AddKeyedSingleton(_queueName, (sp, key) => factory(sp));
    return this;
  }

  public QueueBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>() where TMiddleware : class, IPipelineBehavior
  {
    _services.AddKeyedScoped<IPipelineBehavior, TMiddleware>(_queueName);
    _registeredMiddlewares.Add(typeof(TMiddleware));
    return this;
  }

  internal void SetGlobalMiddlewares(HashSet<Type> globalMiddlewares)
  {
    AddValidation(b =>
    {
      var duplicates = globalMiddlewares.Intersect(b._registeredMiddlewares).ToList();
      if (duplicates.Count != 0)
      {
        return (false, new InvalidOperationException($"Queue '{_queueName}' has the same middleware registrations as global middlewares: {string.Join(", ", duplicates.Select(t => t.Name))}. Remove these middlewares from either the global configuration or the queue configuration."));
      }
      return (true, null);
    });

    foreach (var middleware in globalMiddlewares)
    {
      _services.AddKeyedScoped(typeof(IPipelineBehavior), _queueName, middleware);
    }
  }

  internal string QueueName => _queueName;
}
