using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace UJMW.CommandLineFacade.Mcp {

  /// <summary>
  /// Exposes registered command line facade services through MCP JSON-RPC over standard input and output.
  /// </summary>
  public static class CommandLineMcpEndpoint {

    /// <summary>
    /// Processes MCP JSON-RPC requests from standard input and writes responses to standard output.
    /// </summary>
    public static void ProcessStdIn() {
      Console.OutputEncoding = Encoding.UTF8;
      string line;
      while ((line = Console.ReadLine()) != null) {
        if (string.IsNullOrWhiteSpace(line)) {
          continue;
        }
        if (line.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase)) {
          break;
        }

        JObject response = HandleLine(line);
        if (response != null) {
          Console.WriteLine(response.ToString(Formatting.None));
        }
      }
    }

    private static JObject HandleLine(string line) {
      JObject request;
      try {
        request = JObject.Parse(line);
      }
      catch (JsonException ex) {
        return CreateError(JValue.CreateNull(), -32700, ex.Message);
      }

      JToken id = request["id"];
      string methodName = ReadString(request["method"]);
      if (string.IsNullOrWhiteSpace(methodName)) {
        return CreateError(id, -32600, "Missing JSON-RPC method.");
      }

      if (id == null && methodName.StartsWith("notifications/", StringComparison.OrdinalIgnoreCase)) {
        return null;
      }

      try {
        JObject result = HandleMethod(methodName, request);
        return CreateResult(id, result);
      }
      catch (InvalidOperationException ex) {
        return CreateError(id, -32602, ex.Message);
      }
      catch (NotSupportedException ex) {
        return CreateError(id, -32601, ex.Message);
      }
      catch (Exception ex) {
        return CreateError(id, -32603, ex.Message);
      }
    }

    private static JObject HandleMethod(string methodName, JObject request) {
      if (string.Equals(methodName, "initialize", StringComparison.OrdinalIgnoreCase)) {
        return HandleInitialize();
      }
      if (string.Equals(methodName, "ping", StringComparison.OrdinalIgnoreCase)) {
        return new JObject();
      }
      if (string.Equals(methodName, "tools/list", StringComparison.OrdinalIgnoreCase)) {
        return HandleToolsList();
      }
      if (string.Equals(methodName, "tools/call", StringComparison.OrdinalIgnoreCase)) {
        return HandleToolsCall(request);
      }
      if (string.Equals(methodName, "resources/list", StringComparison.OrdinalIgnoreCase)) {
        JObject result = new JObject();
        result["resources"] = new JArray();
        return result;
      }
      if (string.Equals(methodName, "prompts/list", StringComparison.OrdinalIgnoreCase)) {
        JObject result = new JObject();
        result["prompts"] = new JArray();
        return result;
      }

      throw new NotSupportedException("Unsupported MCP method '" + methodName + "'.");
    }

    private static JObject HandleInitialize() {
      JObject result = new JObject();
      JObject capabilities = new JObject();
      JObject tools = new JObject();
      JObject serverInfo = new JObject();

      tools["listChanged"] = false;
      capabilities["tools"] = tools;
      serverInfo["name"] = "UJMW CommandLineFacade MCP Server";
      serverInfo["version"] = typeof(CommandLineWrapper).Assembly.GetName().Version?.ToString() ?? "1.0.0";

      result["protocolVersion"] = "2024-11-05";
      result["capabilities"] = capabilities;
      result["serverInfo"] = serverInfo;
      return result;
    }

    private static JObject HandleToolsList() {
      JObject result = new JObject();
      JArray tools = new JArray();

      foreach ((Type ServiceType, MethodInfo Method) entry in CommandLineWrapper.GetRegisteredServiceMethodsForMcp()) {
        JObject tool = new JObject();
        string toolName = BuildToolName(entry.ServiceType, entry.Method);
        tool["name"] = toolName;
        tool["description"] = "Calls " + entry.ServiceType.FullName + "." + entry.Method.Name + " through the UJMW command line facade.";
        tool["inputSchema"] = BuildInputSchema(entry.Method);
        tools.Add(tool);
      }

      result["tools"] = tools;
      return result;
    }

    private static JObject HandleToolsCall(JObject request) {
      JObject parameters = request["params"] as JObject;
      if (parameters == null) {
        throw new InvalidOperationException("Missing tools/call parameters.");
      }

      string toolName = ReadString(parameters["name"]);
      if (string.IsNullOrWhiteSpace(toolName)) {
        throw new InvalidOperationException("Missing tools/call tool name.");
      }

      JObject arguments = parameters["arguments"] as JObject;
      if (arguments == null) {
        arguments = new JObject();
      }

      (Type ServiceType, MethodInfo Method)? method = FindMethod(toolName);
      if (!method.HasValue) {
        throw new InvalidOperationException("Unknown MCP tool '" + toolName + "'.");
      }

      object resultObject = CommandLineWrapper.InvokeServiceMethodForMcp(method.Value.Method.Name, arguments.ToString(Formatting.None));
      JObject result = new JObject();
      JArray content = new JArray();
      JObject textEntry = new JObject();
      textEntry["type"] = "text";
      textEntry["text"] = JsonConvert.SerializeObject(resultObject);
      content.Add(textEntry);
      result["content"] = content;
      return result;
    }

    private static (Type ServiceType, MethodInfo Method)? FindMethod(string toolName) {
      foreach ((Type ServiceType, MethodInfo Method) entry in CommandLineWrapper.GetRegisteredServiceMethodsForMcp()) {
        if (string.Equals(BuildToolName(entry.ServiceType, entry.Method), toolName, StringComparison.OrdinalIgnoreCase)) {
          return entry;
        }
      }
      return null;
    }

    private static string BuildToolName(Type serviceType, MethodInfo methodInfo) {
      return SanitizeToolName(serviceType.Name + "_" + methodInfo.Name);
    }

    private static JObject BuildInputSchema(MethodInfo methodInfo) {
      JObject schema = new JObject();
      JObject properties = new JObject();
      schema["type"] = "object";
      schema["properties"] = properties;

      foreach (ParameterInfo parameter in methodInfo.GetParameters()) {
        if (parameter.IsOut) {
          continue;
        }
        properties[parameter.Name] = CreateSchemaForType(parameter.ParameterType);
      }

      return schema;
    }

    private static JObject CreateSchemaForType(Type type) {
      JObject schema = new JObject();
      Type effectiveType = Nullable.GetUnderlyingType(type) ?? type;

      if (effectiveType == typeof(string) || effectiveType == typeof(Guid) || effectiveType == typeof(DateTime) || effectiveType == typeof(DateTimeOffset)) {
        schema["type"] = "string";
      }
      else if (effectiveType == typeof(bool)) {
        schema["type"] = "boolean";
      }
      else if (effectiveType == typeof(byte) || effectiveType == typeof(short) || effectiveType == typeof(int) || effectiveType == typeof(long)) {
        schema["type"] = "integer";
      }
      else if (effectiveType == typeof(float) || effectiveType == typeof(double) || effectiveType == typeof(decimal)) {
        schema["type"] = "number";
      }
      else if (effectiveType.IsEnum) {
        schema["type"] = "string";
        schema["enum"] = new JArray(Enum.GetNames(effectiveType));
      }
      else {
        schema["type"] = "object";
      }

      return schema;
    }

    private static JObject CreateResult(JToken id, JObject result) {
      JObject response = new JObject();
      response["jsonrpc"] = "2.0";
      response["id"] = id == null ? JValue.CreateNull() : id.DeepClone();
      response["result"] = result;
      return response;
    }

    private static JObject CreateError(JToken id, int code, string message) {
      JObject error = new JObject();
      JObject response = new JObject();
      error["code"] = code;
      error["message"] = message;
      response["jsonrpc"] = "2.0";
      response["id"] = id == null ? JValue.CreateNull() : id.DeepClone();
      response["error"] = error;
      return response;
    }

    private static string ReadString(JToken token) {
      if (token == null || token.Type == JTokenType.Null) {
        return null;
      }
      return token.ToString();
    }

    private static string SanitizeToolName(string value) {
      StringBuilder builder = new StringBuilder();
      foreach (char character in value) {
        if (char.IsLetterOrDigit(character) || character == '_' || character == '-') {
          builder.Append(character);
        }
        else {
          builder.Append('_');
        }
      }
      return builder.ToString();
    }

  }

}
