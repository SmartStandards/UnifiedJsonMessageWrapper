using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UJMW.TestClient.Inspection {

  /// <summary>
  /// Builds meaningfully pre-filled UJMW request JSON for a contract method
  /// (works on MetadataLoadContext types, so nothing is instantiated).
  /// </summary>
  internal static class RequestTemplateBuilder {

    /// <summary>
    /// Token which is replaced by the content of the SideChannel text box immediately before sending.
    /// </summary>
    public const string _AutoSideChannelToken = "{AUTO}";

    private const string _AutoSideChannelPlaceholder = "__UJMW_AUTO_SIDECHANNEL__";
    private const int _MaxDepth = 4;

    private static readonly CamelCaseNamingStrategy _NamingStrategy = new CamelCaseNamingStrategy();

    private static readonly HashSet<string> _CollectionDefinitions = new HashSet<string>() {
      "System.Collections.Generic.List`1",
      "System.Collections.Generic.IList`1",
      "System.Collections.Generic.ICollection`1",
      "System.Collections.Generic.IEnumerable`1",
      "System.Collections.Generic.IReadOnlyList`1",
      "System.Collections.Generic.IReadOnlyCollection`1",
      "System.Collections.Generic.HashSet`1",
      "System.Collections.Generic.ISet`1",
      "System.Collections.ObjectModel.Collection`1",
      "System.Collections.ObjectModel.ObservableCollection`1",
      "System.Collections.ObjectModel.ReadOnlyCollection`1"
    };

    private static readonly HashSet<string> _DictionaryDefinitions = new HashSet<string>() {
      "System.Collections.Generic.Dictionary`2",
      "System.Collections.Generic.IDictionary`2",
      "System.Collections.Generic.IReadOnlyDictionary`2",
      "System.Collections.Concurrent.ConcurrentDictionary`2"
    };

    /// <summary>
    /// Builds the request JSON text for the given method. The sidechannel property is always
    /// emitted as '"_": {AUTO}' (which is intentionally no valid JSON until it is replaced).
    /// </summary>
    public static string BuildRequestTemplate(MethodInfo method) {
      JObject request = new JObject();
      request.Add("_", _AutoSideChannelPlaceholder);

      foreach (ParameterInfo parameter in method.GetParameters()) {
        Type parameterType = parameter.ParameterType;
        if (parameterType.IsByRef) {
          if (parameter.IsOut) {
            //out-parameters are not part of the request
            continue;
          }
          parameterType = parameterType.GetElementType();
        }
        JToken value;
        if (parameter.HasDefaultValue) {
          value = ConvertDefaultValue(parameter.RawDefaultValue, parameterType);
        } else {
          value = BuildSampleValue(parameterType, 0, new HashSet<string>());
        }
        request.Add(parameter.Name, value);
      }

      string json = request.ToString(Formatting.Indented);
      return json.Replace("\"" + _AutoSideChannelPlaceholder + "\"", _AutoSideChannelToken);
    }

    /// <summary>
    /// Converts a compiled default value into JSON (enum values are emitted by name).
    /// </summary>
    private static JToken ConvertDefaultValue(object rawDefaultValue, Type parameterType) {
      if (rawDefaultValue == null) {
        return JValue.CreateNull();
      }
      Type valueType = UnwrapNullable(parameterType);
      if (valueType.IsEnum) {
        foreach (FieldInfo field in valueType.GetFields(BindingFlags.Public | BindingFlags.Static)) {
          if (rawDefaultValue.Equals(field.GetRawConstantValue())) {
            return new JValue(field.Name);
          }
        }
      }
      return new JValue(rawDefaultValue);
    }

    /// <summary>
    /// Builds a meaningful sample value for the given type.
    /// </summary>
    private static JToken BuildSampleValue(Type type, int depth, HashSet<string> typesInProgress) {
      if (type.IsGenericParameter) {
        return JValue.CreateNull();
      }
      type = UnwrapNullable(type);

      switch (type.FullName) {
        case "System.String":
          return new JValue(string.Empty);
        case "System.Boolean":
          return new JValue(false);
        case "System.Byte":
        case "System.SByte":
        case "System.Int16":
        case "System.UInt16":
        case "System.Int32":
        case "System.UInt32":
        case "System.Int64":
        case "System.UInt64":
          return new JValue(0);
        case "System.Single":
        case "System.Double":
        case "System.Decimal":
          return new JValue(0.0);
        case "System.Char":
          return new JValue("a");
        case "System.DateTime":
          return new JValue(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
        case "System.DateTimeOffset":
          return new JValue(DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"));
        case "System.TimeSpan":
          return new JValue("00:00:00");
        case "System.Guid":
          return new JValue(Guid.Empty.ToString());
        case "System.Byte[]":
          return new JValue(string.Empty);
        case "System.Object":
          return JValue.CreateNull();
      }

      if (type.IsEnum) {
        string[] names = type.GetEnumNames();
        if (names.Length > 0) {
          return new JValue(names[0]);
        }
        return new JValue(0);
      }

      if (type.IsArray) {
        return new JArray(BuildSampleValue(type.GetElementType(), depth + 1, typesInProgress));
      }

      if (type.IsGenericType) {
        string definitionName = type.GetGenericTypeDefinition().FullName;
        Type[] arguments = type.GetGenericArguments();
        if (_CollectionDefinitions.Contains(definitionName)) {
          return new JArray(BuildSampleValue(arguments[0], depth + 1, typesInProgress));
        }
        if (_DictionaryDefinitions.Contains(definitionName)) {
          JObject dictionary = new JObject();
          dictionary.Add("key", BuildSampleValue(arguments[1], depth + 1, typesInProgress));
          return dictionary;
        }
      }

      if (depth >= _MaxDepth || type.FullName == null || typesInProgress.Contains(type.FullName)) {
        return JValue.CreateNull();
      }
      if (type.IsInterface || type.IsAbstract) {
        return new JObject();
      }

      return BuildComplexSample(type, depth, typesInProgress);
    }

    /// <summary>
    /// Builds a JSON object containing all public instance properties and fields of a complex type.
    /// </summary>
    private static JToken BuildComplexSample(Type type, int depth, HashSet<string> typesInProgress) {
      JObject result = new JObject();
      typesInProgress.Add(type.FullName);
      try {
        PropertyInfo[] properties;
        FieldInfo[] fields;
        try {
          properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
          fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        } catch (FileNotFoundException) {
          //a base type lives in an assembly which is not available
          properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
          fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }
        foreach (PropertyInfo property in properties) {
          if (property.GetIndexParameters().Length > 0 || property.GetGetMethod() == null) {
            continue;
          }
          string jsonName = _NamingStrategy.GetPropertyName(property.Name, false);
          if (!result.ContainsKey(jsonName)) {
            result.Add(jsonName, BuildSampleValue(property.PropertyType, depth + 1, typesInProgress));
          }
        }
        foreach (FieldInfo field in fields) {
          string jsonName = _NamingStrategy.GetPropertyName(field.Name, false);
          if (!result.ContainsKey(jsonName)) {
            result.Add(jsonName, BuildSampleValue(field.FieldType, depth + 1, typesInProgress));
          }
        }
      } finally {
        typesInProgress.Remove(type.FullName);
      }
      return result;
    }

    /// <summary>
    /// Returns T for Nullable&lt;T&gt;, otherwise the type itself.
    /// </summary>
    private static Type UnwrapNullable(Type type) {
      if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1") {
        return type.GetGenericArguments()[0];
      }
      return type;
    }

  }

}
