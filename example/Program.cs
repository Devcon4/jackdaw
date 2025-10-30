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

builder.Services.AddJackdaw(jackdaw =>
{
  jackdaw.AddQueue("TodosQueue")
      .UseInMemory()
      .AsDefault();

  jackdaw.AddQueue("DomainEvents").UseInMemory();
});

var app = builder.Build();

var sampleTodos = new Todo[] {
    new(1, "Walk the dog"),
    new(2, "Do the dishes", DateOnly.FromDateTime(DateTime.Now)),
    new(3, "Do the laundry", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
    new(4, "Clean the bathroom"),
    new(5, "Clean the car", DateOnly.FromDateTime(DateTime.Now.AddDays(2)))
};

var todosApi = app.MapGroup("/todos");
todosApi.MapGet("/", () => sampleTodos);
todosApi.MapGet("/{id}", async (int id, [FromServices] IMediator mediator, [FromServices] ILogger<Program> logger) =>
{
  var sw = System.Diagnostics.Stopwatch.StartNew();
  logger.LogInformation("Received request for Todo with ID {Id}", id);
  var res = await mediator.Send(new GetTodoQuery(id));
  sw.Stop();
  logger.LogInformation("Handled request for Todo with ID {Id} in {ElapsedMilliseconds}", id, sw.ElapsedTicks);
  return res.Todo is not null ? Results.Ok(res.Todo) : Results.NotFound();
});

app.Run();

public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

[JsonSerializable(typeof(Todo[]))]
[JsonSerializable(typeof(GetTodoResponse))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}

// In Program.cs or a separate file in example project

public record GetTodoQuery(int Id) : IRequest<GetTodoResponse>;

public record GetTodoResponse(Todo? Todo) : IResponse;

public class GetTodoHandler : IHandler<GetTodoQuery, GetTodoResponse>
{
  private readonly IMediator _mediator;
  private readonly Todo[] _todos;

  public GetTodoHandler(IMediator mediator)
  {
    _mediator = mediator;
    _todos = new[] {
      new Todo(1, "Walk the dog"),
      new Todo(2, "Do the dishes", DateOnly.FromDateTime(DateTime.Now)),
      new Todo(3, "Do the laundry", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
      new Todo(4, "Clean the bathroom"),
      new Todo(5, "Clean the car", DateOnly.FromDateTime(DateTime.Now.AddDays(2)))
    };
  }

  public async Task<GetTodoResponse> Handle(GetTodoQuery request, CancellationToken cancellationToken)
  {
    await _mediator.Send(new FetchEvent());
    var todo = _todos.FirstOrDefault(t => t.Id == request.Id);
    return new GetTodoResponse(todo);
  }
}

public record FetchEvent() : IRequest<FetchEventResponse>;
public record FetchEventResponse(string Message) : IResponse;

[JackdawQueue("DomainEvents")]
public class FetchEventHandler(ILogger<FetchEventHandler> logger) : IHandler<FetchEvent, FetchEventResponse>
{
  public Task<FetchEventResponse> Handle(FetchEvent request, CancellationToken cancellationToken)
  {
    logger.LogInformation("Handling FetchEvent");
    return Task.FromResult(new FetchEventResponse("Event fetched successfully."));
  }
}