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
todosApi.MapGet("/{id}", async (int id, [FromServices] IMediator mediator) => await mediator.Send(new GetTodoQuery(id)));

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

public class GetTodoHandler : IRequestHandler<GetTodoQuery, GetTodoResponse>
{
  private readonly Todo[] _todos;

  public GetTodoHandler()
  {
    _todos = new[] {
      new Todo(1, "Walk the dog"),
      new Todo(2, "Do the dishes", DateOnly.FromDateTime(DateTime.Now)),
      new Todo(3, "Do the laundry", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
      new Todo(4, "Clean the bathroom"),
      new Todo(5, "Clean the car", DateOnly.FromDateTime(DateTime.Now.AddDays(2)))
    };
  }

  public Task<GetTodoResponse> Handle(GetTodoQuery request, CancellationToken cancellationToken)
  {
    var todo = _todos.FirstOrDefault(t => t.Id == request.Id);
    return Task.FromResult(new GetTodoResponse(todo));
  }
}

public record FetchEvent() : IRequest<FetchEventResponse>;
public record FetchEventResponse(string Message) : IResponse;

[JackdawQueue("DomainEvents")]
public class FetchEventHandler : IRequestHandler<FetchEvent, FetchEventResponse>
{
  public Task<FetchEventResponse> Handle(FetchEvent request, CancellationToken cancellationToken)
  {
    return Task.FromResult(new FetchEventResponse("Event fetched successfully."));
  }
}