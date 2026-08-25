using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text;
using System.Threading.Tasks;

namespace System.Web.UJMW {

  /// <summary>
  /// Hosts the dynamic MCP JSON-RPC endpoint as an ASP.NET Core MVC action.
  /// </summary>
  [ApiController]
  [Route("_mcp")]
  [ApiExplorerSettings(GroupName = "MCP")]
  [Tags("MCP")]
  public sealed class DynamicMcpController : ControllerBase {

    private readonly DynamicMcpEndpoint _Endpoint;
    private readonly DynamicMcpToolCatalog _ToolCatalog;

    /// <summary>
    /// Creates a new dynamic MCP controller.
    /// </summary>
    /// <param name="endpoint">The JSON-RPC endpoint handler.</param>
    /// <param name="toolCatalog">The MCP tool catalog.</param>
    public DynamicMcpController(DynamicMcpEndpoint endpoint, DynamicMcpToolCatalog toolCatalog) {
      _Endpoint = endpoint;
      _ToolCatalog = toolCatalog;
    }

    /// <summary>
    /// Dumps all currently exposed MCP tools in a human-readable format.
    /// </summary>
    /// <returns>The current MCP tool dump.</returns>
    [HttpGet]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ContentResult Get() {
      DynamicMcpToolDescriptor[] tools = _ToolCatalog.GetTools();
      StringBuilder builder = new StringBuilder();
      builder.AppendLine("UJMW Dynamic MCP Server");
      builder.AppendLine("Tools: " + tools.Length.ToString());
      builder.AppendLine();

      foreach (DynamicMcpToolDescriptor tool in tools) {
        builder.AppendLine(tool.Name);
        builder.AppendLine("  Description: " + tool.Description);
        builder.AppendLine("  Service: " + tool.ServiceType.FullName);
        builder.AppendLine("  Target: " + tool.HttpMethod + " /" + tool.RelativePath);
        builder.AppendLine("  InputSchema:");
        builder.AppendLine(tool.InputSchema.ToString(Formatting.Indented));
        builder.AppendLine();
      }

      return this.Content(builder.ToString(), "text/plain", Encoding.UTF8);
    }

    /// <summary>
    /// Handles an MCP JSON-RPC request.
    /// </summary>
    /// <returns>The completed task.</returns>
    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task Post([FromBody] DynamicMcpJsonRpcRequest request) {
      return _Endpoint.Handle(this.HttpContext, request);
    }

  }

}
