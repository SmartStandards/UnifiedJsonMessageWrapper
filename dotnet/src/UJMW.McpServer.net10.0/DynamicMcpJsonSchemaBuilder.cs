using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace System.Web.UJMW {

  /// <summary>
  /// Creates simple JSON schema documents for MCP tool input objects.
  /// </summary>
  public sealed class DynamicMcpJsonSchemaBuilder {

    private readonly DynamicMcpXmlDocumentationProvider _XmlDocumentationProvider;

    /// <summary>
    /// Creates a new JSON schema builder.
    /// </summary>
    /// <param name="xmlDocumentationProvider">The XML documentation provider.</param>
    public DynamicMcpJsonSchemaBuilder(DynamicMcpXmlDocumentationProvider xmlDocumentationProvider) {
      _XmlDocumentationProvider = xmlDocumentationProvider;
    }

    /// <summary>
    /// Builds the MCP input schema for one API operation.
    /// </summary>
    /// <param name="apiDescription">The API Explorer description.</param>
    /// <param name="contractMethod">The service contract method.</param>
    /// <returns>The generated JSON schema object.</returns>
    public JObject BuildInputSchema(ApiDescription apiDescription, MethodInfo contractMethod) {
      JObject schema = new JObject();
      JObject properties = new JObject();
      Dictionary<string, string> parameterSummaries = _XmlDocumentationProvider.GetParameterSummaries(contractMethod);

      schema["type"] = "object";
      schema["properties"] = properties;

      foreach (ApiParameterDescription parameter in apiDescription.ParameterDescriptions) {
        if (parameter.Source != null && string.Equals(parameter.Source.Id, "Body", StringComparison.OrdinalIgnoreCase)) {
          Type bodyType = parameter.Type;
          this.AppendTypeProperties(properties, bodyType, new List<Type>());
        }
      }

      foreach (KeyValuePair<string, string> parameterSummary in parameterSummaries) {
        JToken propertyToken = properties[parameterSummary.Key];
        JObject propertySchema = propertyToken as JObject;
        if (propertySchema != null) {
          propertySchema["description"] = parameterSummary.Value;
        }
      }

      foreach (ApiParameterDescription parameter in apiDescription.ParameterDescriptions) {
        if (parameter.Source != null && string.Equals(parameter.Source.Id, "Path", StringComparison.OrdinalIgnoreCase)) {
          JObject parameterSchema = this.CreateSchemaForType(parameter.Type, new List<Type>());
          if (parameterSummaries.ContainsKey(parameter.Name)) {
            parameterSchema["description"] = parameterSummaries[parameter.Name];
          }
          properties[parameter.Name] = parameterSchema;
        }
      }

      return schema;
    }

    /// <summary>
    /// Appends object properties from the given DTO type to the target schema properties object.
    /// </summary>
    /// <param name="targetProperties">The schema properties target.</param>
    /// <param name="type">The DTO type.</param>
    /// <param name="visitedTypes">The types already visited during recursive schema creation.</param>
    private void AppendTypeProperties(JObject targetProperties, Type type, List<Type> visitedTypes) {
      if (type == null) {
        return;
      }
      if (visitedTypes.Contains(type)) {
        return;
      }
      visitedTypes.Add(type);

      PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
      foreach (PropertyInfo property in properties) {
        if (!property.CanRead) {
          continue;
        }
        targetProperties[property.Name] = this.CreateSchemaForType(property.PropertyType, visitedTypes);
      }
    }

    /// <summary>
    /// Creates a JSON schema node for the given CLR type.
    /// </summary>
    /// <param name="type">The CLR type.</param>
    /// <param name="visitedTypes">The types already visited during recursive schema creation.</param>
    /// <returns>The generated JSON schema node.</returns>
    private JObject CreateSchemaForType(Type type, List<Type> visitedTypes) {
      JObject schema = new JObject();
      Type effectiveType = this.GetEffectiveType(type);

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
      else if (effectiveType.IsArray) {
        schema["type"] = "array";
        schema["items"] = this.CreateSchemaForType(effectiveType.GetElementType(), visitedTypes);
      }
      else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(effectiveType) && effectiveType != typeof(string)) {
        Type itemType = typeof(object);
        if (effectiveType.IsGenericType) {
          Type[] arguments = effectiveType.GetGenericArguments();
          if (arguments.Length == 1) {
            itemType = arguments[0];
          }
        }
        schema["type"] = "array";
        schema["items"] = this.CreateSchemaForType(itemType, visitedTypes);
      }
      else {
        schema["type"] = "object";
        JObject nestedProperties = new JObject();
        schema["properties"] = nestedProperties;
        this.AppendTypeProperties(nestedProperties, effectiveType, visitedTypes);
      }

      return schema;
    }

    /// <summary>
    /// Resolves nullable wrapper types to their underlying type.
    /// </summary>
    /// <param name="type">The source type.</param>
    /// <returns>The effective schema type.</returns>
    private Type GetEffectiveType(Type type) {
      Type nullableType = Nullable.GetUnderlyingType(type);
      if (nullableType != null) {
        return nullableType;
      }
      return type;
    }

  }

}
