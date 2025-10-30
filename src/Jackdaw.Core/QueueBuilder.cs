using Jackdaw.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Jackdaw.Core;


// QueueRegistration.cs (internal record)
internal record QueueRegistration(
    string Name,
    Func<IServiceProvider, IMessageQueue> Factory,
    bool IsDefault,
    bool AllowMultipleHandlers
);

public class QueueBuilder
{
  private readonly string _queueName;
  private readonly IServiceCollection _services;
  private bool _queueRegistered = false;
  private Func<QueueBuilder, (bool, Exception)>? _validations;

  internal QueueBuilder(string queueName, IServiceCollection services)
  {
    _queueName = queueName;
    _services = services;

    AddValidation(b => (_queueName is "Default", new InvalidOperationException("The 'Default' queue is reserved and cannot be used as a queue name.")));
    AddValidation(b => (b._queueRegistered, new InvalidCastException($"Queue '{_queueName}' does not specify a queue implementation. Add UseInMemory or another queue implementation.")));
  }

  // Core configuration methods
  public QueueBuilder AsDefault()
  {

    _services.AddKeyedSingleton("Default", (sp, key) => sp.GetRequiredKeyedService<IMessageQueue>(_queueName));

    return this;
  }

  public QueueBuilder AddValidation(Func<QueueBuilder, (bool, Exception)> validation)
  {
    _validations += validation;
    return this;
  }

  public bool Valid()
  {
    if (_validations is null)
    {
      return true;
    }

    var (isValid, exception) = _validations(this);
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

  internal IServiceCollection Services => _services;
  internal string QueueName => _queueName;
}
