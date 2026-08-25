using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace System.Web.UJMW {

  /// <summary>
  /// Provides ASP.NET Core setup extensions for the dynamic MCP endpoint.
  /// </summary>
  public static class SetupExtensions {

    /// <summary>
    /// Adds a dynamic MCP endpoint exposing registered API Explorer operations as MCP tools.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configMethod">The MCP controller configuration method.</param>
    public static void AddDynamicMcpController(this IServiceCollection services, Action<DynamicMcpControllerRegistrar> configMethod) {
      DynamicMcpControllerRegistrar registrar = new DynamicMcpControllerRegistrar();
      if (configMethod != null) {
        configMethod.Invoke(registrar);
      }

      services.AddSingleton(registrar);
      services.AddSingleton(registrar.Options);
      services.AddEndpointsApiExplorer();
      services.AddSingleton<DynamicMcpXmlDocumentationProvider>();
      services.AddSingleton<DynamicMcpJsonSchemaBuilder>();
      services.AddSingleton<DynamicMcpToolCatalog>();
      services.AddTransient<DynamicMcpEndpoint>();
    }

    /// <summary>
    /// Maps the dynamic MCP endpoint explicitly.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint convention builder.</returns>
    public static IEndpointConventionBuilder MapDynamicMcpController(this IEndpointRouteBuilder endpoints) {
      DynamicMcpControllerOptions options = endpoints.ServiceProvider.GetRequiredService<DynamicMcpControllerOptions>();
      string route = options.Route;
      if (string.IsNullOrWhiteSpace(route)) {
        route = "_mcp";
      }
      string apiGroupName = options.ApiGroupName;
      if (string.IsNullOrWhiteSpace(apiGroupName)) {
        apiGroupName = "MCP";
      }

      return endpoints.MapPost(
        route,
        (HttpContext context) => {
          DynamicMcpEndpoint endpoint = context.RequestServices.GetRequiredService<DynamicMcpEndpoint>();
          return endpoint.Handle(context);
        }
      )
      .WithDisplayName("UJMW Dynamic MCP Server")
      .WithName("UJMW.DynamicMcpController")
      .WithGroupName(apiGroupName)
      .WithTags(apiGroupName);
    }

  }

}
