using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Jackdaw.Core;
using Jackdaw.Interfaces;
using Jackdaw.Queues.InMemory;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
  options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddSingleton<TodoStore>();

builder.Services.AddJackdaw(jackdaw =>
{
  jackdaw.UseMiddleware<TimingMiddleware>();

  jackdaw.AddQueue("TodosQueue")
      .UseMiddleware<LoggingMiddleware>()
      .UseInMemory()
      .AsDefault();

  jackdaw.AddQueue("DomainEvents").UseInMemory();
});

var app = builder.Build();

var todosApi = app.MapGroup("/todos");
todosApi.MapGet("/", async ([FromServices] IMediator mediator) =>
{
  var response = await mediator.Send(new GetTodosQuery());
  return Results.Ok(response.Todos);
});
todosApi.MapGet("/{id}", async (int id, [FromServices] IMediator mediator, [FromServices] ILogger<Program> logger) =>
{
  var res = await mediator.Send(new GetTodoQuery(id));
  return res.Todo is not null ? Results.Ok(res.Todo) : Results.NotFound();
});
todosApi.MapPost("/", async ([FromBody] CreateTodoCommand command, [FromServices] IMediator mediator) =>
{
  var response = await mediator.Send(command);
  return Results.Created($"/todos/{response.CreatedTodo.Id}", response.CreatedTodo);
});

app.Run();

public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

[JsonSerializable(typeof(IEnumerable<Todo>))]
[JsonSerializable(typeof(GetTodoResponse))]
[JsonSerializable(typeof(CreateTodoResponse))]
[JsonSerializable(typeof(CreateTodoCommand))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}

public class TodoStore
{
  private readonly ConcurrentBag<Todo> _todoBag = new()
  {
    new Todo(1, "Walk the dog"),
    new Todo(2, "Do the dishes", DateOnly.FromDateTime(DateTime.Now)),
    new Todo(3, "Do the laundry", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
    new Todo(4, "Clean the bathroom"),
    new Todo(5, "Clean the car", DateOnly.FromDateTime(DateTime.Now.AddDays(2)))
  };


  public IEnumerable<Todo> Query() => _todoBag;
  private int NextId => _todoBag.Max(t => t.Id) + 1;
  public int Add(Todo todo)
  {
    var newTodo = todo with { Id = NextId };

    _todoBag.Add(newTodo);
    return newTodo.Id;
  }
}

public record CreateTodoCommand(string Title, DateOnly? DueBy = null) : IRequest<CreateTodoResponse>;
public record CreateTodoResponse(Todo CreatedTodo) : IResponse;
public class CreateTodoHandler(TodoStore todoStore) : IHandler<CreateTodoCommand, CreateTodoResponse>
{
  public Task<CreateTodoResponse> Handle(CreateTodoCommand request, CancellationToken cancellationToken)
  {
    var todo = new Todo(0, request.Title, request.DueBy);
    var createdId = todoStore.Add(todo);
    var created = todoStore.Query().First(t => t.Id == createdId);
    return Task.FromResult(new CreateTodoResponse(created));
  }
}


public record GetTodosQuery() : IRequest<GetTodosResponse>;
public record GetTodosResponse(IEnumerable<Todo> Todos) : IResponse;
public class GetTodosHandler(TodoStore todoStore) : IHandler<GetTodosQuery, GetTodosResponse>
{

  public Task<GetTodosResponse> Handle(GetTodosQuery request, CancellationToken cancellationToken)
  {
    return Task.FromResult(new GetTodosResponse(todoStore.Query()));
  }
}

public record GetTodoQuery(int Id) : IRequest<GetTodoResponse>;

public record GetTodoResponse(Todo? Todo) : IResponse;

public class GetTodoHandler(IMediator mediator, TodoStore todoStore) : IHandler<GetTodoQuery, GetTodoResponse>
{
  public async Task<GetTodoResponse> Handle(GetTodoQuery request, CancellationToken cancellationToken)
  {
    await mediator.Send(new FetchEvent());
    var todo = todoStore.Query().FirstOrDefault(t => t.Id == request.Id);
    return new GetTodoResponse(todo);
  }
}

public record FetchEvent() : IRequest<FetchEventResponse>;
public record FetchEventResponse(string Message) : IResponse;

[JackdawQueue("DomainEvents")]
public class FetchEventHandler : IHandler<FetchEvent, FetchEventResponse>
{
  public Task<FetchEventResponse> Handle(FetchEvent request, CancellationToken cancellationToken)
  {
    return Task.FromResult(new FetchEventResponse("Event fetched successfully."));
  }
}

public class LoggingMiddleware() : IPipelineBehavior
{
  public Task<TResponse> Handle<TRequest, TResponse>(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    where TRequest : IRequest<TResponse>
    where TResponse : IResponse
  {
    var response = next();
    return response;
  }
}

public class TimingMiddleware(ILogger<TimingMiddleware> logger) : IPipelineBehavior
{
  public async Task<TResponse> Handle<TRequest, TResponse>(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    where TRequest : IRequest<TResponse>
    where TResponse : IResponse
  {
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var response = await next();
    sw.Stop();
    logger.LogInformation("Finished {RequestType} in {Elapsed} nanoseconds", typeof(TRequest).Name, sw.Elapsed.TotalNanoseconds);
    return response;
  }
}

public class AuthenticationMiddleware(ILogger<AuthenticationMiddleware> logger) : IPipelineBehavior
{
  public Task<TResponse> Handle<TRequest, TResponse>(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    where TRequest : IRequest<TResponse>
    where TResponse : IResponse
  {
    logger.LogInformation("Authenticating request of type {RequestType}", typeof(TRequest).Name);
    throw new UnauthorizedAccessException("Authentication failed.");
  }
}