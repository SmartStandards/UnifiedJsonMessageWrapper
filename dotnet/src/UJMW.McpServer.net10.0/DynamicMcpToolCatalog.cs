using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace System.Web.UJMW {

  /// <summary>
  /// Discovers API Explorer operations and exposes them as MCP tool descriptors.
  /// </summary>
  public sealed class DynamicMcpToolCatalog {

    private const string _DynamicControllerBaseFullName = "System.Web.UJMW.DynamicUjmwControllerFactory+DynamicControllerBase`1";

    private readonly IApiDescriptionGroupCollectionProvider _ApiDescriptionProvider;
    private readonly DynamicMcpControllerRegistrar _Registrar;
    private readonly DynamicMcpJsonSchemaBuilder _SchemaBuilder;
    private readonly DynamicMcpXmlDocumentationProvider _XmlDocumentationProvider;

    /// <summary>
    /// Creates a new tool catalog instance.
    /// </summary>
    /// <param name="apiDescriptionProvider">The API Explorer provider.</param>
    /// <param name="registrar">The MCP access registrar.</param>
    /// <param name="schemaBuilder">The JSON schema builder.</param>
    public DynamicMcpToolCatalog(
      IApiDescriptionGroupCollectionProvider apiDescriptionProvider,
      DynamicMcpControllerRegistrar registrar,
      DynamicMcpJsonSchemaBuilder schemaBuilder,
      DynamicMcpXmlDocumentationProvider xmlDocumentationProvider
    ) {
      _ApiDescriptionProvider = apiDescriptionProvider;
      _Registrar = registrar;
      _SchemaBuilder = schemaBuilder;
      _XmlDocumentationProvider = xmlDocumentationProvider;
    }

    /// <summary>
    /// Gets all currently discoverable MCP tools.
    /// </summary>
    /// <returns>The discovered MCP tools.</returns>
    public DynamicMcpToolDescriptor[] GetTools() {
      List<DynamicMcpToolDescriptor> tools = new List<DynamicMcpToolDescriptor>();
      Dictionary<string, int> toolNameUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      DynamicMcpAccessRegistration[] registrations = _Registrar.Entries;

      foreach (ApiDescriptionGroup group in _ApiDescriptionProvider.ApiDescriptionGroups.Items) {
        foreach (ApiDescription apiDescription in group.Items) {
          if (!this.IsCallableApi(apiDescription)) {
            continue;
          }

          Type serviceType = this.TryResolveServiceType(apiDescription);
          DynamicMcpAccessRegistration registration = this.FindRegistration(registrations, serviceType, apiDescription.GroupName);
          if (registration == null) {
            continue;
          }

          DynamicMcpToolDescriptor tool = this.CreateToolDescriptor(apiDescription, registration, toolNameUsage);
          tools.Add(tool);
        }
      }

      return tools.ToArray();
    }

    /// <summary>
    /// Finds one MCP tool by name.
    /// </summary>
    /// <param name="toolName">The requested MCP tool name.</param>
    /// <returns>The matching tool or null.</returns>
    public DynamicMcpToolDescriptor FindTool(string toolName) {
      DynamicMcpToolDescriptor[] tools = this.GetTools();
      foreach (DynamicMcpToolDescriptor tool in tools) {
        if (string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase)) {
          return tool;
        }
      }
      return null;
    }

    /// <summary>
    /// Checks whether the API Explorer operation can be called through MCP.
    /// </summary>
    /// <param name="apiDescription">The API Explorer operation.</param>
    /// <returns>True if the operation can be exposed as an MCP tool.</returns>
    private bool IsCallableApi(ApiDescription apiDescription) {
      if (apiDescription == null) {
        return false;
      }
      if (string.IsNullOrWhiteSpace(apiDescription.RelativePath)) {
        return false;
      }
      if (!string.Equals(apiDescription.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
      if (string.Equals(apiDescription.GroupName, "hidden", StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
      return true;
    }

    /// <summary>
    /// Creates a descriptor for a registered API operation.
    /// </summary>
    /// <param name="apiDescription">The API Explorer operation.</param>
    /// <param name="registration">The matching access registration.</param>
    /// <param name="toolNameUsage">The name collision tracker.</param>
    /// <returns>The created MCP tool descriptor.</returns>
    private DynamicMcpToolDescriptor CreateToolDescriptor(
      ApiDescription apiDescription,
      DynamicMcpAccessRegistration registration,
      Dictionary<string, int> toolNameUsage
    ) {
      ControllerActionDescriptor controllerAction = apiDescription.ActionDescriptor as ControllerActionDescriptor;
      string methodName = apiDescription.ActionDescriptor.RouteValues["action"];
      if (controllerAction != null && controllerAction.MethodInfo != null && !string.IsNullOrWhiteSpace(controllerAction.MethodInfo.Name)) {
        methodName = controllerAction.MethodInfo.Name;
      }
      if (string.IsNullOrWhiteSpace(methodName)) {
        methodName = "Call";
      }

      string toolName = this.BuildToolName(registration.ServiceType, methodName, apiDescription.GroupName, registration.Options);
      toolName = this.MakeUniqueName(toolName, toolNameUsage);

      DynamicMcpToolDescriptor tool = new DynamicMcpToolDescriptor();
      tool.Name = toolName;
      MethodInfo contractMethod = this.FindContractMethod(registration.ServiceType, methodName, apiDescription);
      tool.Description = this.BuildDescription(registration.ServiceType, methodName, registration.Options, contractMethod);
      tool.RelativePath = apiDescription.RelativePath;
      tool.HttpMethod = apiDescription.HttpMethod;
      tool.ServiceType = registration.ServiceType;
      tool.Options = registration.Options;
      tool.ApiDescription = apiDescription;
      tool.InputSchema = _SchemaBuilder.BuildInputSchema(apiDescription, contractMethod);
      return tool;
    }

    /// <summary>
    /// Finds the access registration that matches the discovered service type.
    /// </summary>
    /// <param name="registrations">The configured registrations.</param>
    /// <param name="serviceType">The discovered service type.</param>
    /// <param name="apiGroupName">The API Explorer group name.</param>
    /// <returns>The matching registration or null.</returns>
    private DynamicMcpAccessRegistration FindRegistration(
      DynamicMcpAccessRegistration[] registrations,
      Type serviceType,
      string apiGroupName
    ) {
      if (serviceType == null) {
        return null;
      }

      foreach (DynamicMcpAccessRegistration registration in registrations) {
        if (!this.IsServiceMatch(registration.ServiceType, serviceType)) {
          continue;
        }
        if (!string.IsNullOrWhiteSpace(registration.Options.ApiGroupName)) {
          if (!string.Equals(registration.Options.ApiGroupName, apiGroupName, StringComparison.OrdinalIgnoreCase)) {
            continue;
          }
        }
        return registration;
      }

      return null;
    }

    /// <summary>
    /// Checks whether the registered interface matches the discovered API service type.
    /// </summary>
    /// <param name="registeredServiceType">The registered service type.</param>
    /// <param name="discoveredServiceType">The discovered service type.</param>
    /// <returns>True if both types match.</returns>
    private bool IsServiceMatch(Type registeredServiceType, Type discoveredServiceType) {
      if (registeredServiceType == discoveredServiceType) {
        return true;
      }
      if (registeredServiceType.IsAssignableFrom(discoveredServiceType)) {
        return true;
      }
      return false;
    }

    /// <summary>
    /// Resolves the service contract represented by an API Explorer operation.
    /// </summary>
    /// <param name="apiDescription">The API Explorer operation.</param>
    /// <returns>The discovered service type or null.</returns>
    private Type TryResolveServiceType(ApiDescription apiDescription) {
      ControllerActionDescriptor controllerAction = apiDescription.ActionDescriptor as ControllerActionDescriptor;
      if (controllerAction == null) {
        return null;
      }
      if (controllerAction.ControllerTypeInfo == null) {
        return null;
      }

      Type controllerType = controllerAction.ControllerTypeInfo.AsType();
      Type dynamicContractType = this.TryResolveDynamicControllerContractType(controllerType);
      if (dynamicContractType != null) {
        return dynamicContractType;
      }

      foreach (DynamicMcpAccessRegistration registration in _Registrar.Entries) {
        if (registration.ServiceType.IsAssignableFrom(controllerType)) {
          return registration.ServiceType;
        }
      }

      return null;
    }

    /// <summary>
    /// Resolves the generic contract type used by UJMW dynamic controllers.
    /// </summary>
    /// <param name="controllerType">The concrete controller type.</param>
    /// <returns>The dynamic UJMW contract type or null.</returns>
    private Type TryResolveDynamicControllerContractType(Type controllerType) {
      Type currentType = controllerType;
      while (currentType != null) {
        if (currentType.IsGenericType) {
          Type genericDefinition = currentType.GetGenericTypeDefinition();
          if (genericDefinition.FullName == _DynamicControllerBaseFullName) {
            return currentType.GetGenericArguments()[0];
          }
        }
        currentType = currentType.BaseType;
      }
      return null;
    }

    /// <summary>
    /// Builds the MCP tool name.
    /// </summary>
    /// <param name="serviceType">The service interface type.</param>
    /// <param name="methodName">The API method name.</param>
    /// <param name="apiGroupName">The API Explorer group name.</param>
    /// <param name="options">The access options.</param>
    /// <returns>The sanitized MCP tool name.</returns>
    private string BuildToolName(Type serviceType, string methodName, string apiGroupName, DynamicMcpAccessOptions options) {
      string interfaceName = serviceType.Name;
      string pattern = options.ToolNamePattern;
      if (string.IsNullOrWhiteSpace(pattern)) {
        pattern = "{Interface}_{Method}";
      }

      string rawName = pattern.Replace("{Interface}", interfaceName).Replace("{Method}", methodName).Replace("{Group}", apiGroupName);
      if (!string.IsNullOrWhiteSpace(options.ToolNamePrefix)) {
        rawName = options.ToolNamePrefix + rawName;
      }

      return this.SanitizeToolName(rawName);
    }

    /// <summary>
    /// Builds the MCP tool description.
    /// </summary>
    /// <param name="serviceType">The service interface type.</param>
    /// <param name="methodName">The API method name.</param>
    /// <param name="options">The access options.</param>
    /// <returns>The MCP tool description.</returns>
    private string BuildDescription(Type serviceType, string methodName, DynamicMcpAccessOptions options, MethodInfo contractMethod) {
      string targetDescription = serviceType.FullName + "." + methodName;
      string xmlSummary = _XmlDocumentationProvider.GetMethodSummary(contractMethod);
      if (!string.IsNullOrWhiteSpace(xmlSummary)) {
        if (!string.IsNullOrWhiteSpace(options.Description)) {
          return options.Description + " " + xmlSummary;
        }
        return xmlSummary;
      }
      if (!string.IsNullOrWhiteSpace(options.Description)) {
        return options.Description + " " + targetDescription;
      }
      return "Calls " + targetDescription + " through the ASP.NET Core API Explorer endpoint.";
    }

    /// <summary>
    /// Finds the contract method represented by the API Explorer operation.
    /// </summary>
    /// <param name="serviceType">The service interface type.</param>
    /// <param name="methodName">The discovered method name.</param>
    /// <param name="apiDescription">The API Explorer operation.</param>
    /// <returns>The matching contract method or null.</returns>
    private MethodInfo FindContractMethod(Type serviceType, string methodName, ApiDescription apiDescription) {
      if (serviceType == null || string.IsNullOrWhiteSpace(methodName)) {
        return null;
      }

      string[] apiParameterNames = apiDescription.ParameterDescriptions.Select((parameter) => parameter.Name).ToArray();
      MethodInfo[] methods = serviceType.GetMethods();
      foreach (MethodInfo methodInfo in methods) {
        if (!string.Equals(methodInfo.Name, methodName, StringComparison.Ordinal)) {
          continue;
        }

        ParameterInfo[] parameters = methodInfo.GetParameters();
        bool allParametersMatch = true;
        foreach (ParameterInfo parameterInfo in parameters) {
          if (!apiParameterNames.Contains(parameterInfo.Name, StringComparer.OrdinalIgnoreCase)) {
            allParametersMatch = false;
            break;
          }
        }

        if (allParametersMatch) {
          return methodInfo;
        }
      }

      return serviceType.GetMethod(methodName);
    }

    /// <summary>
    /// Makes a generated tool name unique within one discovery result.
    /// </summary>
    /// <param name="toolName">The generated tool name.</param>
    /// <param name="toolNameUsage">The name collision tracker.</param>
    /// <returns>The unique tool name.</returns>
    private string MakeUniqueName(string toolName, Dictionary<string, int> toolNameUsage) {
      if (!toolNameUsage.ContainsKey(toolName)) {
        toolNameUsage[toolName] = 1;
        return toolName;
      }

      int usageCount = toolNameUsage[toolName] + 1;
      toolNameUsage[toolName] = usageCount;
      return toolName + "_" + usageCount.ToString();
    }

    /// <summary>
    /// Sanitizes a value so it can be used as an MCP tool name.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The sanitized value.</returns>
    private string SanitizeToolName(string value) {
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
