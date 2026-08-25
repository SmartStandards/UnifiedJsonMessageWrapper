using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace System.Web.UJMW.Mcp {

  /// <summary>
  /// Handles the JSON-RPC based MCP endpoint.
  /// </summary>
  public sealed class DynamicMcpEndpoint {

    private static readonly HttpClient _HttpClient = new HttpClient();

    private readonly DynamicMcpControllerOptions _Options;
    private readonly DynamicMcpToolCatalog _ToolCatalog;

    /// <summary>
    /// Creates a new MCP endpoint handler.
    /// </summary>
    /// <param name="options">The global MCP endpoint options.</param>
    /// <param name="toolCatalog">The API Explorer tool catalog.</param>
    public DynamicMcpEndpoint(DynamicMcpControllerOptions options, DynamicMcpToolCatalog toolCatalog) {
      _Options = options;
      _ToolCatalog = toolCatalog;
    }

    /// <summary>
    /// Handles one incoming MCP HTTP request.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The completed task.</returns>
    public Task Handle(HttpContext context) {
      string requestText = this.ReadRequestText(context);
      JObject request;
      try {
        request = JObject.Parse(requestText);
      } catch (Newtonsoft.Json.JsonException ex) {
        return this.WriteJson(context, this.CreateError(JValue.CreateNull(), -32700, ex.Message));
      }

      return this.HandleParsedRequest(context, request);
    }

    /// <summary>
    /// Handles one incoming MCP HTTP request using an already deserialized JSON-RPC body.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="requestBody">The deserialized JSON-RPC request body.</param>
    /// <returns>The completed task.</returns>
    public Task Handle(HttpContext context, DynamicMcpJsonRpcRequest requestBody) {
      if (requestBody == null) {
        return this.WriteJson(context, this.CreateError(JValue.CreateNull(), -32600, "Missing JSON-RPC request body."));
      }

      JObject request = new JObject();
      request["jsonrpc"] = requestBody.Jsonrpc;
      request["method"] = requestBody.Method;
      request["id"] = this.CreateToken(requestBody.Id);
      request["params"] = this.CreateToken(requestBody.Params);
      return this.HandleParsedRequest(context, request);
    }

    private JToken CreateToken(object value) {
      if (value == null) {
        return null;
      }

      if (value is JsonElement jsonElement) {
        return JToken.Parse(jsonElement.GetRawText());
      }

      JToken token = value as JToken;
      if (token != null) {
        return token;
      }

      return JToken.FromObject(value);
    }

    /// <summary>
    /// Handles one already parsed JSON-RPC request object.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="request">The parsed JSON-RPC request object.</param>
    /// <returns>The completed task.</returns>
    private Task HandleParsedRequest(HttpContext context, JObject request) {
      JToken id = request["id"];
      string methodName = this.ReadString(request["method"]);
      if (string.IsNullOrWhiteSpace(methodName)) {
        return this.WriteJson(context, this.CreateError(id, -32600, "Missing JSON-RPC method."));
      }

      if (id == null && methodName.StartsWith("notifications/", StringComparison.OrdinalIgnoreCase)) {
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        return Task.CompletedTask;
      }

      try {
        JObject result = this.HandleMethod(methodName, request, context);
        return this.WriteJson(context, this.CreateResult(id, result));
      } catch (InvalidOperationException ex) {
        return this.WriteJson(context, this.CreateError(id, -32602, ex.Message));
      } catch (NotSupportedException ex) {
        return this.WriteJson(context, this.CreateError(id, -32601, ex.Message));
      } catch (Exception ex) {
        return this.WriteJson(context, this.CreateError(id, -32603, ex.Message));
      }
    }

    /// <summary>
    /// Dispatches a JSON-RPC method to its MCP operation.
    /// </summary>
    /// <param name="methodName">The requested JSON-RPC method name.</param>
    /// <param name="request">The JSON-RPC request object.</param>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The method result object.</returns>
    private JObject HandleMethod(string methodName, JObject request, HttpContext context) {
      if (string.Equals(methodName, "initialize", StringComparison.OrdinalIgnoreCase)) {
        return this.HandleInitialize();
      }
      if (string.Equals(methodName, "ping", StringComparison.OrdinalIgnoreCase)) {
        return new JObject();
      }
      if (string.Equals(methodName, "tools/list", StringComparison.OrdinalIgnoreCase)) {
        return this.HandleToolsList();
      }
      if (string.Equals(methodName, "tools/call", StringComparison.OrdinalIgnoreCase)) {
        return this.HandleToolsCall(request, context);
      }
      if (string.Equals(methodName, "resources/list", StringComparison.OrdinalIgnoreCase)) {
        JObject resourcesResult = new JObject();
        resourcesResult["resources"] = new JArray();
        return resourcesResult;
      }
      if (string.Equals(methodName, "prompts/list", StringComparison.OrdinalIgnoreCase)) {
        JObject promptsResult = new JObject();
        promptsResult["prompts"] = new JArray();
        return promptsResult;
      }

      throw new NotSupportedException("Unsupported MCP method '" + methodName + "'.");
    }

    /// <summary>
    /// Handles the MCP initialize method.
    /// </summary>
    /// <returns>The initialize result.</returns>
    private JObject HandleInitialize() {
      JObject result = new JObject();
      JObject capabilities = new JObject();
      JObject tools = new JObject();
      JObject serverInfo = new JObject();

      tools["listChanged"] = false;
      capabilities["tools"] = tools;
      serverInfo["name"] = _Options.ServerName;
      serverInfo["version"] = _Options.ServerVersion;

      result["protocolVersion"] = "2024-11-05";
      result["capabilities"] = capabilities;
      result["serverInfo"] = serverInfo;
      return result;
    }

    /// <summary>
    /// Handles the MCP tools/list method.
    /// </summary>
    /// <returns>The tools/list result.</returns>
    private JObject HandleToolsList() {
      JObject result = new JObject();
      JArray tools = new JArray();
      DynamicMcpToolDescriptor[] descriptors = _ToolCatalog.GetTools();

      foreach (DynamicMcpToolDescriptor descriptor in descriptors) {
        JObject tool = new JObject();
        tool["name"] = descriptor.Name;
        tool["description"] = descriptor.Description;
        tool["inputSchema"] = descriptor.InputSchema.DeepClone();
        tools.Add(tool);
      }

      result["tools"] = tools;
      return result;
    }

    /// <summary>
    /// Handles the MCP tools/call method.
    /// </summary>
    /// <param name="request">The JSON-RPC request object.</param>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The tools/call result.</returns>
    private JObject HandleToolsCall(JObject request, HttpContext context) {
      JObject parameters = request["params"] as JObject;
      if (parameters == null) {
        throw new InvalidOperationException("Missing tools/call parameters.");
      }

      string toolName = this.ReadString(parameters["name"]);
      if (string.IsNullOrWhiteSpace(toolName)) {
        throw new InvalidOperationException("Missing tools/call tool name.");
      }

      JObject arguments = parameters["arguments"] as JObject;
      if (arguments == null) {
        arguments = new JObject();
      }

      DynamicMcpToolDescriptor tool = _ToolCatalog.FindTool(toolName);
      if (tool == null) {
        throw new InvalidOperationException("Unknown MCP tool '" + toolName + "'.");
      }

      return this.InvokeApiTool(tool, arguments, context);
    }

    /// <summary>
    /// Invokes the ASP.NET Core API operation represented by one MCP tool.
    /// </summary>
    /// <param name="tool">The MCP tool descriptor.</param>
    /// <param name="arguments">The tool call arguments.</param>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The MCP tool call result.</returns>
    private JObject InvokeApiTool(DynamicMcpToolDescriptor tool, JObject arguments, HttpContext context) {
      string pathAndQuery = this.BuildTargetPathAndQuery(tool, arguments);
      Uri targetUri = this.BuildTargetUri(context, pathAndQuery);
      JObject requestBody = this.BuildRequestBody(tool, arguments);

      using (HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, targetUri)) {
        requestMessage.Content = new StringContent(requestBody.ToString(Formatting.None), Encoding.UTF8, "application/json");
        this.CopyRequestHeaders(context, tool, requestMessage);

        using (HttpResponseMessage response = _HttpClient.Send(requestMessage)) {
          string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
          JObject result = new JObject();
          JArray content = new JArray();
          JObject textEntry = new JObject();
          textEntry["type"] = "text";
          textEntry["text"] = responseText;
          content.Add(textEntry);
          result["content"] = content;

          if (!response.IsSuccessStatusCode) {
            result["isError"] = true;
            result["statusCode"] = (int)response.StatusCode;
            result["reasonPhrase"] = response.ReasonPhrase;
          }

          return result;
        }
      }
    }

    /// <summary>
    /// Reads the complete HTTP request body as text.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The request body text.</returns>
    private string ReadRequestText(HttpContext context) {
      using (StreamReader reader = new StreamReader(context.Request.Body, Encoding.UTF8)) {
        return reader.ReadToEnd();
      }
    }

    /// <summary>
    /// Builds the target path and query string for the API operation.
    /// </summary>
    /// <param name="tool">The MCP tool descriptor.</param>
    /// <param name="arguments">The MCP tool arguments.</param>
    /// <returns>The target relative path and query string.</returns>
    private string BuildTargetPathAndQuery(DynamicMcpToolDescriptor tool, JObject arguments) {
      string pathAndQuery = tool.RelativePath;
      foreach (string routeParameterName in this.GetRouteParameterNames(tool.ApiDescription)) {
        JToken routeValueToken = arguments[routeParameterName];
        if (routeValueToken == null) {
          throw new InvalidOperationException("Missing route argument '" + routeParameterName + "' for MCP tool '" + tool.Name + "'.");
        }

        string routeValue = WebUtility.UrlEncode(this.ReadString(routeValueToken));
        pathAndQuery = pathAndQuery.Replace("{" + routeParameterName + "}", routeValue);
      }
      return pathAndQuery;
    }

    /// <summary>
    /// Builds an absolute URI to the local ASP.NET Core API operation.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="pathAndQuery">The relative path and query string.</param>
    /// <returns>The absolute target URI.</returns>
    private Uri BuildTargetUri(HttpContext context, string pathAndQuery) {
      string normalizedPathAndQuery = pathAndQuery;
      if (!normalizedPathAndQuery.StartsWith("/", StringComparison.Ordinal)) {
        normalizedPathAndQuery = "/" + normalizedPathAndQuery;
      }

      string path = normalizedPathAndQuery;
      string query = string.Empty;
      int queryStartIndex = normalizedPathAndQuery.IndexOf('?');
      if (queryStartIndex >= 0) {
        path = normalizedPathAndQuery.Substring(0, queryStartIndex);
        query = normalizedPathAndQuery.Substring(queryStartIndex + 1);
      }

      UriBuilder uriBuilder = new UriBuilder();
      uriBuilder.Scheme = context.Request.Scheme;
      uriBuilder.Host = context.Request.Host.Host;
      if (context.Request.Host.Port.HasValue) {
        uriBuilder.Port = context.Request.Host.Port.Value;
      }
      else {
        uriBuilder.Port = -1;
      }

      string pathBase = context.Request.PathBase.Value;
      if (string.IsNullOrWhiteSpace(pathBase)) {
        pathBase = string.Empty;
      }
      uriBuilder.Path = pathBase + path;
      uriBuilder.Query = query;
      return uriBuilder.Uri;
    }

    /// <summary>
    /// Builds the JSON request body forwarded to the API operation.
    /// </summary>
    /// <param name="tool">The MCP tool descriptor.</param>
    /// <param name="arguments">The MCP tool arguments.</param>
    /// <returns>The API request body.</returns>
    private JObject BuildRequestBody(DynamicMcpToolDescriptor tool, JObject arguments) {
      JObject body = new JObject();
      foreach (JProperty property in arguments.Properties()) {
        body[property.Name] = property.Value.DeepClone();
      }

      foreach (string routeParameterName in this.GetRouteParameterNames(tool.ApiDescription)) {
        body.Remove(routeParameterName);
      }

      return body;
    }

    /// <summary>
    /// Gets all route parameter names declared by API Explorer.
    /// </summary>
    /// <param name="apiDescription">The API Explorer operation.</param>
    /// <returns>The route parameter names.</returns>
    private string[] GetRouteParameterNames(ApiDescription apiDescription) {
      List<string> routeParameterNames = new List<string>();
      foreach (ApiParameterDescription parameter in apiDescription.ParameterDescriptions) {
        if (parameter.Source != null && string.Equals(parameter.Source.Id, "Path", StringComparison.OrdinalIgnoreCase)) {
          routeParameterNames.Add(parameter.Name);
        }
      }
      return routeParameterNames.ToArray();
    }

    /// <summary>
    /// Copies configured request headers from the MCP request to the forwarded API request.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="tool">The MCP tool descriptor.</param>
    /// <param name="requestMessage">The target HTTP request message.</param>
    private void CopyRequestHeaders(HttpContext context, DynamicMcpToolDescriptor tool, HttpRequestMessage requestMessage) {
      if (tool.Options.McpCopyAuthorizationHeader) {
        this.CopyRequestHeader(context, requestMessage, "Authorization");
      }

      string[] copiedHeaderNames = tool.Options.McpCopiedHeaderNames;
      if (copiedHeaderNames == null) {
        return;
      }

      foreach (string headerName in copiedHeaderNames) {
        this.CopyRequestHeader(context, requestMessage, headerName);
      }
    }

    /// <summary>
    /// Copies one request header if it exists.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="requestMessage">The target HTTP request message.</param>
    /// <param name="headerName">The header name.</param>
    private void CopyRequestHeader(HttpContext context, HttpRequestMessage requestMessage, string headerName) {
      if (string.IsNullOrWhiteSpace(headerName)) {
        return;
      }

      StringValues values;
      if (context.Request.Headers.TryGetValue(headerName, out values)) {
        requestMessage.Headers.TryAddWithoutValidation(headerName, values.ToArray());
      }
    }

    /// <summary>
    /// Writes a JSON result to the HTTP response.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="response">The JSON response object.</param>
    /// <returns>The write task.</returns>
    private Task WriteJson(HttpContext context, JObject response) {
      context.Response.ContentType = "application/json";
      return context.Response.WriteAsync(response.ToString(Formatting.None), Encoding.UTF8);
    }

    /// <summary>
    /// Creates a successful JSON-RPC response.
    /// </summary>
    /// <param name="id">The JSON-RPC request id.</param>
    /// <param name="result">The result object.</param>
    /// <returns>The JSON-RPC response.</returns>
    private JObject CreateResult(JToken id, JObject result) {
      JObject response = new JObject();
      response["jsonrpc"] = "2.0";
      response["id"] = this.CloneId(id);
      response["result"] = result;
      return response;
    }

    /// <summary>
    /// Creates a JSON-RPC error response.
    /// </summary>
    /// <param name="id">The JSON-RPC request id.</param>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="message">The error message.</param>
    /// <returns>The JSON-RPC response.</returns>
    private JObject CreateError(JToken id, int code, string message) {
      JObject error = new JObject();
      JObject response = new JObject();

      error["code"] = code;
      error["message"] = message;

      response["jsonrpc"] = "2.0";
      response["id"] = this.CloneId(id);
      response["error"] = error;
      return response;
    }

    /// <summary>
    /// Clones a JSON-RPC id value or creates a JSON null value.
    /// </summary>
    /// <param name="id">The source id.</param>
    /// <returns>The cloned id.</returns>
    private JToken CloneId(JToken id) {
      if (id == null) {
        return JValue.CreateNull();
      }
      return id.DeepClone();
    }

    /// <summary>
    /// Reads a JSON token as a string.
    /// </summary>
    /// <param name="token">The source token.</param>
    /// <returns>The token string value.</returns>
    private string ReadString(JToken token) {
      if (token == null) {
        return null;
      }
      return token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Formatting.None);
    }

  }

}
