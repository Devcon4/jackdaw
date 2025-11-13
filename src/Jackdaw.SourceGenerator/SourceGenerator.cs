using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Jackdaw.SourceGenerator;

[Generator]
public class JackdawGenerator : IIncrementalGenerator
{
  public void Initialize(IncrementalGeneratorInitializationContext context)
  {
    // Find all classes that directly implement IHandler<TRequest, TResponse>
    var handlerClasses = context.SyntaxProvider
        .CreateSyntaxProvider(
            predicate: static (s, _) => s is ClassDeclarationSyntax c && c.BaseList != null,
            transform: static (ctx, _) => GetHandlerInfo(ctx))
        .Where(static m => m is not null);

    // Find interfaces marked with [JackdawQueue] attribute in current project
    var markedInterfaces = context.SyntaxProvider
        .CreateSyntaxProvider(
            predicate: static (s, _) => s is InterfaceDeclarationSyntax i && i.AttributeLists.Count > 0,
            transform: static (ctx, _) => GetMarkedInterfaceInfo(ctx))
        .Where(static m => m is not null);

    // Find classes that implement interfaces (for matching with marked interfaces)
    var interfaceImplementors = context.SyntaxProvider
        .CreateSyntaxProvider(
            predicate: static (s, _) => s is ClassDeclarationSyntax c && c.BaseList != null,
            transform: static (ctx, _) => GetInterfaceImplementorInfo(ctx))
        .Where(static m => m is not null);

    // Get compilation to scan referenced assemblies
    var compilationProvider = context.CompilationProvider;

    // Combine everything including compilation for cross-assembly analysis
    var combined = markedInterfaces.Collect()
        .Combine(interfaceImplementors.Collect())
        .Combine(compilationProvider);

    // Combine all handler sources
    var allHandlers = handlerClasses.Collect()
        .Combine(combined)
        .Select(static (data, _) =>
        {
          var (directHandlers, ((interfaces, implementors), compilation)) = data;

          // Scan referenced assemblies for marked interfaces
          var referencedMarkedInterfaces = ScanReferencedAssembliesForMarkedInterfaces(compilation);

          return new CombinedHandlerData(
              directHandlers.ToList(),
              interfaces.ToList(),
              implementors.ToList(),
              referencedMarkedInterfaces,
              compilation);
        });

    context.RegisterSourceOutput(allHandlers, GenerateDispatcher);
  }
  private static HandlerInfo? GetHandlerInfo(GeneratorSyntaxContext context)
  {
    var classDeclaration = (ClassDeclarationSyntax)context.Node;
    var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

    if (symbol is not INamedTypeSymbol classSymbol)
      return null;

    var handlerInterface = classSymbol.AllInterfaces
        .FirstOrDefault(i =>
            i.IsGenericType &&
            i.ConstructedFrom.ToDisplayString() == "Jackdaw.Interfaces.IHandler<TRequest, TResponse>");

    if (handlerInterface is null)
      return null;

    var requestType = handlerInterface.TypeArguments[0];
    var responseType = handlerInterface.TypeArguments[1];

    // Look for [JackdawQueue("QueueName")] attribute
    var queueAttribute = classSymbol.GetAttributes()
        .FirstOrDefault(a => a.AttributeClass?.Name == "JackdawQueueAttribute");

    var queueName = queueAttribute?.ConstructorArguments.FirstOrDefault().Value?.ToString()
                    ?? "Default"; // Use "Default" if no attribute

    return new HandlerInfo(
        classSymbol.ToDisplayString(),
        requestType.ToDisplayString(),
        responseType.ToDisplayString(),
        queueName);
  }

  private static MarkedInterfaceInfo? GetMarkedInterfaceInfo(GeneratorSyntaxContext context)
  {
    var interfaceDeclaration = (InterfaceDeclarationSyntax)context.Node;
    var symbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration);

    if (symbol is not INamedTypeSymbol interfaceSymbol)
      return null;

    // Look for [JackdawQueue("QueueName")] attribute on the interface
    var queueAttribute = interfaceSymbol.GetAttributes()
        .FirstOrDefault(a => a.AttributeClass?.Name == "JackdawQueueAttribute");

    if (queueAttribute is null)
      return null;

    var queueName = queueAttribute.ConstructorArguments.FirstOrDefault().Value?.ToString();
    if (string.IsNullOrEmpty(queueName))
      return null;

    return new MarkedInterfaceInfo(
        interfaceSymbol.ToDisplayString(),
        queueName!);  // We just checked for null/empty above
  }

  private static InterfaceImplementorInfo? GetInterfaceImplementorInfo(GeneratorSyntaxContext context)
  {
    var classDeclaration = (ClassDeclarationSyntax)context.Node;
    var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

    if (symbol is not INamedTypeSymbol classSymbol)
      return null;

    // Skip abstract classes
    if (classSymbol.IsAbstract)
      return null;

    // Get all interfaces this class implements
    var implementedInterfaces = classSymbol.AllInterfaces
        .Select(i => i.ToDisplayString())
        .ToList();

    if (implementedInterfaces.Count == 0)
      return null;

    return new InterfaceImplementorInfo(
        classSymbol.ToDisplayString(),
        implementedInterfaces);
  }

  private record HandlerInfo(
      string HandlerType,
      string RequestType,
      string ResponseType,
      string QueueName);

  private record MarkedInterfaceInfo(
      string InterfaceType,
      string QueueName);

  private record InterfaceImplementorInfo(
      string ClassType,
      List<string> ImplementedInterfaces);

  private record CombinedHandlerData(
      List<HandlerInfo?> DirectHandlers,
      List<MarkedInterfaceInfo?> MarkedInterfaces,
      List<InterfaceImplementorInfo?> InterfaceImplementors,
      List<MarkedInterfaceInfo> ReferencedMarkedInterfaces,
      Compilation Compilation);

  private record InterfaceBasedHandlerInfo(
      string HandlerType,
      string InterfaceType,
      string QueueName);

  private static List<MarkedInterfaceInfo> ScanReferencedAssembliesForMarkedInterfaces(Compilation compilation)
  {
    var result = new List<MarkedInterfaceInfo>();

    foreach (var reference in compilation.References)
    {
      var assembly = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
      if (assembly != null)
      {
        // Look for interfaces marked with [JackdawQueue] in referenced assemblies
        ScanNamespaceForMarkedInterfaces(assembly.GlobalNamespace, result);
      }
    }

    return result;
  }

  private static void ScanNamespaceForMarkedInterfaces(INamespaceSymbol namespaceSymbol, List<MarkedInterfaceInfo> result)
  {
    // Check all types in this namespace
    foreach (var member in namespaceSymbol.GetMembers())
    {
      if (member is INamedTypeSymbol typeSymbol && typeSymbol.TypeKind == TypeKind.Interface)
      {
        // Check if this interface has [JackdawQueue] attribute
        var queueAttribute = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "JackdawQueueAttribute");

        if (queueAttribute != null)
        {
          var arg = queueAttribute.ConstructorArguments.FirstOrDefault();
          // The value should be a compile-time constant, get the actual value
          var queueName = arg.Value as string;
          if (!string.IsNullOrEmpty(queueName))
          {
            result.Add(new MarkedInterfaceInfo(
                typeSymbol.ToDisplayString(),
                queueName!));
          }
        }
      }
      else if (member is INamespaceSymbol nestedNamespace)
      {
        // Recursively scan nested namespaces
        ScanNamespaceForMarkedInterfaces(nestedNamespace, result);
      }
    }
  }

  private void GenerateDispatcher(SourceProductionContext context, CombinedHandlerData data)
  {
    var validHandlers = data.DirectHandlers.Where(h => h is not null).Cast<HandlerInfo>().ToList();

    // Combine marked interfaces from current project and referenced assemblies
    var validMarkedInterfaces = data.MarkedInterfaces.Where(m => m is not null).Cast<MarkedInterfaceInfo>().ToList();
    var allMarkedInterfaces = validMarkedInterfaces.Concat(data.ReferencedMarkedInterfaces).ToList();

    var validImplementors = data.InterfaceImplementors.Where(i => i is not null).Cast<InterfaceImplementorInfo>().ToList();

    var interfaceBasedHandlers = new List<InterfaceBasedHandlerInfo>();
    var additionalHandlerInfo = new List<HandlerInfo>();

    foreach (var markedInterface in allMarkedInterfaces)
    {
      foreach (var implementor in validImplementors)
      {
        // Check if this class implements the marked interface
        // For generic interfaces, we need to match the base definition
        // Extract just the type name without generic parameters for comparison
        var markedInterfaceBaseName = markedInterface.InterfaceType.Split('<')[0];

        var matchesInterface = implementor.ImplementedInterfaces.Any(impl =>
        {
          // Exact match for non-generic interfaces
          if (impl == markedInterface.InterfaceType)
            return true;

          // For generic interfaces, check if the base name matches
          var implBaseName = impl.Split('<')[0];
          return implBaseName == markedInterfaceBaseName;
        });

        if (matchesInterface)
        {
          var interfaceHandler = new InterfaceBasedHandlerInfo(
              implementor.ClassType,
              markedInterface.InterfaceType,
              markedInterface.QueueName);
          interfaceBasedHandlers.Add(interfaceHandler);

          // Also extract concrete request/response types from the implementor's interfaces
          // and add them as HandlerInfo for queue routing - use the queue from the interfaceHandler
          foreach (var impl in implementor.ImplementedInterfaces)
          {
            // Match patterns like "IHandler<SimpleRequirement, AuthorizationResult>"
            var match = System.Text.RegularExpressions.Regex.Match(impl, @"IHandler<([^,]+),\s*(.+)>");
            if (match.Success)
            {
              var requestType = match.Groups[1].Value.Trim();
              var responseType = match.Groups[2].Value.Trim();

              // Use the queue name from the matched interface handler
              // This ensures we use the correct queue for interface-based handlers
              additionalHandlerInfo.Add(new HandlerInfo(
                  implementor.ClassType,
                  requestType,
                  responseType,
                  interfaceHandler.QueueName));
            }
          }
        }
      }
    }

    // Combine direct handlers with extracted interface-based handler info
    // Prefer non-Default queues when there are duplicates
    var allHandlersForRouting = validHandlers.Concat(additionalHandlerInfo)
        .GroupBy(h => new { h.RequestType, h.ResponseType })
        .Select(g => g.OrderBy(h => h.QueueName == "Default" ? 1 : 0).First())
        .ToList();

    var handlersByQueue = validHandlers.GroupBy(h => h.QueueName).ToList();
    var interfaceHandlersByQueue = interfaceBasedHandlers.GroupBy(h => h.QueueName).ToList();

    // Merge the queue groups for runner registration
    var allQueueNames = handlersByQueue.Select(g => g.Key)
        .Concat(interfaceHandlersByQueue.Select(g => g.Key))
        .Distinct()
        .ToList();

    // remove interface based handlers from handlerComments
    var handlerComments = string.Join("\n", validHandlers.Where(h => !interfaceBasedHandlers.Any(i => i.HandlerType == h.HandlerType)).Select(h =>
      $"// Handler: {h.HandlerType} -> Queue: {h.QueueName}"));

    var interfaceComments = string.Join("\n", interfaceBasedHandlers.Select(h =>
      $"// Interface Handler: {h.HandlerType} implements {h.InterfaceType} -> Queue: {h.QueueName}"));

    var allComments = string.Join("\n", new[] { handlerComments, interfaceComments }.Where(s => !string.IsNullOrEmpty(s)));

    // Check if Jackdaw.Authorization is referenced and generate IRequirementRouter if needed
    var hasAuthorizationReference = data.Compilation.References
        .Select(r => data.Compilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol)
        .Any(a => a?.Name == "Jackdaw.Authorization");

    var authorizers = hasAuthorizationReference
        ? DiscoverAuthorizers(data.Compilation)
        : new List<(string AuthorizerType, string RequestType)>();

    var authorizerCode = hasAuthorizationReference
        ? GenerateRequirementRouter(authorizers)
        : string.Empty;

    var code = $$"""
      #nullable enable
      // <auto-generated/>
      // Found {{validHandlers.Count}} direct handler(s) and {{interfaceBasedHandlers.Count}} interface-based handler(s) across {{allQueueNames.Count}} queue(s)
      {{allComments}}

      using System;
      using System.Threading;
      using System.Threading.Tasks;
      using Microsoft.Extensions.DependencyInjection;
      using Microsoft.Extensions.Hosting;
      using Jackdaw.Interfaces;

      namespace Jackdaw.Core;

      {{GenerateDispatcherClass(allHandlersForRouting)}}

      {{GenerateRouterClass(allHandlersForRouting, interfaceBasedHandlers)}}

      {{GenerateServiceExtensions(validHandlers, interfaceBasedHandlers, allQueueNames, hasAuthorizationReference, authorizers)}}

      {{authorizerCode}}
      """;

    context.AddSource("HandlerDispatcher.g.cs", code);
  }
  private string GenerateDispatcherClass(List<HandlerInfo> handlers)
  {
    if (handlers.Count == 0)
    {
      return """
            file sealed class GeneratedHandlerDispatcher : IHandlerDispatcher
            {
                public async Task DispatchAsync(IRequestMetadata metadata, IServiceProvider serviceProvider, CancellationToken cancellationToken)
                {
                    throw new InvalidOperationException("No handlers registered.");
                }
            }
            """;
    }

    var getQueueName = (HandlerInfo handler) =>
    $$"""
    var queueName = {{(handler.QueueName is "Default" ? "serviceProvider.GetRequiredService<DefaultQueueName>().ActualQueueName" : $"\"{handler.QueueName}\"")}};
    """;


    var handlerCases = string.Join("\n", handlers.Select(handler =>
    {
      var hash = $"m{System.Math.Abs(handler.RequestType.GetHashCode())}";
      return $$"""
                    case RequestMetadata<{{handler.ResponseType}}> {{hash}} when {{hash}}.Request is {{handler.RequestType}}:
                    {
                        {{getQueueName(handler)}}
                        var middlewares = serviceProvider.GetKeyedServices<IPipelineBehavior>(queueName);
                        var handlers = serviceProvider.GetServices<IHandler<{{handler.RequestType}}, {{handler.ResponseType}}>>();
                        {{handler.ResponseType}}? response = default;
                        
                        foreach (var handler in handlers)
                        {
                            Func<Task<{{handler.ResponseType}}>> pipeline = async () => 
                                await handler.Handle(({{handler.RequestType}}){{hash}}.Request, cancellationToken);
                            
                            foreach (var middleware in middlewares.Reverse())
                            {
                                var currentPipeline = pipeline;
                                pipeline = async () => await middleware.Handle<{{handler.RequestType}}, {{handler.ResponseType}}>(
                                    ({{handler.RequestType}}){{hash}}.Request,
                                    currentPipeline,
                                    cancellationToken);
                            }
                            
                            try
                            {
                                response = await pipeline();
                            }
                            catch (Exception ex)
                            {
                                {{hash}}.CompletionSource.SetException(ex);
                                return;
                            }
                        }
                        
                        if (response != null)
                        {
                            {{hash}}.CompletionSource.SetResult(response);
                        }
                        break;
                    }
        """;
    }));

    return $$"""
        file sealed class GeneratedHandlerDispatcher : IHandlerDispatcher
        {
            public async Task DispatchAsync(IRequestMetadata metadata, IServiceProvider serviceProvider, CancellationToken cancellationToken)
            {
                switch (metadata)
                {
        {{handlerCases}}
                    default:
                        throw new InvalidOperationException($"No handler registered for {metadata.GetType().Name}");
                }
            }
        }
        """;
  }
  private string GenerateRouterClass(List<HandlerInfo> handlers, List<InterfaceBasedHandlerInfo> interfaceBasedHandlers)
  {
    if (handlers.Count == 0 && interfaceBasedHandlers.Count == 0)
    {
      return """
        file sealed class GeneratedQueueRouter : IQueueRouter
        {
            private readonly IServiceProvider _serviceProvider;

            public GeneratedQueueRouter(IServiceProvider serviceProvider)
            {
                _serviceProvider = serviceProvider;
            }

            public IMessageQueue GetQueue<TRequest, TResponse>(TRequest request) where TResponse : IResponse where TRequest : IRequest<TResponse>
            {
                throw new InvalidOperationException("No handlers registered.");
            }
        }
        """;
    }

    var requestToQueue = handlers
      .GroupBy(h => h.RequestType)
      .ToDictionary(g => g.Key, g => g.First().QueueName);

    var switchCases = string.Join("\n", requestToQueue.Select(kvp =>
      $$"""            {{kvp.Key}} _ => _serviceProvider.GetRequiredKeyedService<IMessageQueue>("{{kvp.Value}}"),"""));

    return $$"""
      file sealed class GeneratedQueueRouter : IQueueRouter
      {
          private readonly IServiceProvider _serviceProvider;

          public GeneratedQueueRouter(IServiceProvider serviceProvider)
          {
              _serviceProvider = serviceProvider;
          }

          public IMessageQueue GetQueue<TRequest, TResponse>(TRequest request) where TResponse : IResponse where TRequest : IRequest<TResponse>
          {
              return request switch
              {
      {{switchCases}}
                  _ => throw new InvalidOperationException(
                      $"No queue mapping found for request type {request.GetType().Name}. " +
                      $"Ensure the handler for this request is decorated with [JackdawQueue] attribute or register a default queue.")
              };
          }
      }
      """;
  }
  private string GenerateServiceExtensions(
      List<HandlerInfo> handlers,
      List<InterfaceBasedHandlerInfo> interfaceBasedHandlers,
      List<string> allQueueNames,
      bool hasAuthorizationReference,
      List<(string AuthorizerType, string RequestType)> authorizers)
  {
    var handlerRegistrations = string.Join("\n", handlers.Select(handler =>
      $$"""        services.AddScoped<IHandler<{{handler.RequestType}}, {{handler.ResponseType}}>, {{handler.HandlerType}}>();"""));

    // Register authorizers as IJackdawAuthorizer<TRequest>
    var authorizerRegistrations = authorizers.Count > 0
        ? string.Join("\n", authorizers.Select(a =>
            $$"""        services.AddScoped<Jackdaw.Authorization.IJackdawAuthorizer<{{a.RequestType}}>, {{a.AuthorizerType}}>();"""))
        : string.Empty;

    // Add IRequirementRouter registration if authorization is referenced
    var authorizationRegistration = hasAuthorizationReference
        ? "\n        services.AddSingleton<Jackdaw.Authorization.IRequirementRouter, GeneratedRequirementRouter>();"
        : string.Empty;

    var allRegistrations = string.Join("\n", new[] { handlerRegistrations, authorizerRegistrations, authorizationRegistration }
        .Where(s => !string.IsNullOrEmpty(s)));

    var runnerRegistrations = string.Join("\n\n", allQueueNames.Select(queueName =>
    {
      return $$"""
              // MediatorRunner for '{{queueName}}' queue
              services.AddSingleton<IHostedService>(sp => 
                  new MediatorRunner(
                      "{{queueName}}",
                      sp,
                      sp.GetRequiredService<IHandlerDispatcher>()));
      """;
    }));

    return $$"""
      public static partial class ServiceCollectionExtensions
      {
          public static IServiceCollection AddJackdaw(this IServiceCollection services, Action<JackdawBuilder>? configure = null)
          {
              var builder = new JackdawBuilder(services);
              configure?.Invoke(builder);
              var isValid = builder.Initialize();
              if (!isValid)
              {
                  throw new InvalidOperationException("Invalid Jackdaw configuration.");
              }

      {{allRegistrations}}
              services.AddSingleton<IHandlerDispatcher, GeneratedHandlerDispatcher>();
              services.AddSingleton<IQueueRouter, GeneratedQueueRouter>();
              services.AddSingleton<IMediator, Mediator>();

      {{runnerRegistrations}}
              return services;
          }
      }
      """;
  }

  private List<(string AuthorizerType, string RequestType)> DiscoverAuthorizers(Compilation compilation)
  {
    var authorizers = new List<(string AuthorizerType, string RequestType)>();

    foreach (var syntaxTree in compilation.SyntaxTrees)
    {
      var semanticModel = compilation.GetSemanticModel(syntaxTree);
      var root = syntaxTree.GetRoot();

      foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
      {
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
        if (classSymbol == null || classSymbol.IsAbstract)
          continue;

        // Check if this class inherits from JackdawAuthorizer<T>
        var baseType = classSymbol.BaseType;
        while (baseType != null)
        {
          if (baseType.IsGenericType &&
              baseType.ConstructedFrom.ToDisplayString() == "Jackdaw.Authorization.JackdawAuthorizer<TRequest>")
          {
            var requestType = baseType.TypeArguments[0].ToDisplayString();
            authorizers.Add((classSymbol.ToDisplayString(), requestType));
            break;
          }
          baseType = baseType.BaseType;
        }
      }
    }

    return authorizers;
  }

  private string GenerateRequirementRouter(List<(string AuthorizerType, string RequestType)> authorizers)
  {
    if (authorizers.Count == 0)
    {
      // Return a default empty implementation - no namespace since it's in the same file
      return """

file sealed class GeneratedRequirementRouter : Jackdaw.Authorization.IRequirementRouter
{
    public System.Collections.Generic.IEnumerable<Jackdaw.Authorization.IAuthorizerRequirement> GetRequirements<TRequest, TResponse>(TRequest request) 
        where TResponse : Jackdaw.Interfaces.IResponse 
        where TRequest : Jackdaw.Interfaces.IRequest<TResponse>
    {
        return System.Array.Empty<Jackdaw.Authorization.IAuthorizerRequirement>();
    }
}
""";
    }

    var switchCases = string.Join("\n", authorizers.Select(a =>
    {
      var varName = $"r{System.Math.Abs(a.RequestType.GetHashCode())}";
      return $$"""            {{a.RequestType}} {{varName}} => GetAuthorizerRequirements({{varName}}),""";
    }));

    return $$"""

file sealed class GeneratedRequirementRouter : Jackdaw.Authorization.IRequirementRouter
{
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;

    public GeneratedRequirementRouter(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public System.Collections.Generic.IEnumerable<Jackdaw.Authorization.IAuthorizerRequirement> GetRequirements<TRequest, TResponse>(TRequest request) 
        where TResponse : Jackdaw.Interfaces.IResponse 
        where TRequest : Jackdaw.Interfaces.IRequest<TResponse>
    {
        return request switch
        {
{{switchCases}}
            _ => System.Array.Empty<Jackdaw.Authorization.IAuthorizerRequirement>()
        };
    }

    private System.Collections.Generic.IEnumerable<Jackdaw.Authorization.IAuthorizerRequirement> GetAuthorizerRequirements<TConcreteRequest>(TConcreteRequest request)
        where TConcreteRequest : notnull
    {
        using var scope = _scopeFactory.CreateScope();
        var authorizer = scope.ServiceProvider.GetService<Jackdaw.Authorization.IJackdawAuthorizer<TConcreteRequest>>();
        if (authorizer == null)
            return System.Array.Empty<Jackdaw.Authorization.IAuthorizerRequirement>();

        // Build the policy - authorizer can inspect the request instance
        authorizer.BuildPolicy(request);
        return authorizer.Requirements;
    }
}
""";
  }
}