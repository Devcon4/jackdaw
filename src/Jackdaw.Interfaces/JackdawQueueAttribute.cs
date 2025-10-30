namespace Jackdaw.Interfaces;

/// <summary>
/// Specifies which named queue this handler should be registered to.
/// If not specified, the handler will be registered to the default queue (if any).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class JackdawQueueAttribute : Attribute
{
  /// <summary>
  /// Gets the name of the queue this handler belongs to.
  /// </summary>
  public string QueueName { get; }

  /// <summary>
  /// Registers this handler to a specific named queue.
  /// </summary>
  /// <param name="queueName">The name of the queue (must match a queue registered via AddJackdawQueue)</param>
  public JackdawQueueAttribute(string queueName)
  {
    if (string.IsNullOrWhiteSpace(queueName))
      throw new ArgumentException("Queue name cannot be null or whitespace.", nameof(queueName));

    QueueName = queueName;
  }
}