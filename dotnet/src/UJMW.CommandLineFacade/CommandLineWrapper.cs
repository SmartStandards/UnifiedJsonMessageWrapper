using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace UJMW.CommandLineFacade {

  /// <summary>
  /// A wrapper for registering services and invoking their methods via command line.  
  /// </summary>
  public class CommandLineWrapper {

    // Cache for service factories by type
    private static readonly ConcurrentDictionary<Type, Delegate> _ServiceFactories =
      new ConcurrentDictionary<Type, Delegate>();

    // Method cache: maps method name to (service type, MethodInfo)
    private static readonly ConcurrentDictionary<string, (Type ServiceType, MethodInfo Method)> _MethodCache =
      new ConcurrentDictionary<string, (Type, MethodInfo)>();

    /// <summary>
    /// Registers a service type with its factory function. 
    /// </summary>
    /// <typeparam name="TServiceType"></typeparam>
    /// <param name="serviceFactory"></param>
    public static void RegisterService<TServiceType>(Func<TServiceType> serviceFactory) {
      _ServiceFactories[typeof(TServiceType)] = serviceFactory;
      RebuildMethodCache();
    }

    private static void RebuildMethodCache() {
      _MethodCache.Clear();

      foreach (var kvp in _ServiceFactories) {
        var serviceType = kvp.Key;
        var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var method in methods) {
          // If overloaded, use "MethodName|ParamType1,ParamType2" as key
          var paramTypes = method.GetParameters();
          var methodKey = paramTypes.Length == 0
            ? method.Name
            : $"{method.Name}|{string.Join(",", paramTypes.Select(p => p.ParameterType.FullName))}";

          _MethodCache[methodKey] = (serviceType, method);
        }
      }
    }

    /// <summary>
    /// Gets the registered service factory for the specified service type.
    /// </summary>
    /// <typeparam name="TServiceType"></typeparam>
    /// <returns></returns>
    public static Func<TServiceType> GetServiceFactory<TServiceType>() {
      if (_ServiceFactories.TryGetValue(typeof(TServiceType), out var factory)) {
        return factory as Func<TServiceType>;
      }
      return null;
    }

    /// <summary>
    /// Invokes a registered service method by name with parameters provided as a JSON string.
    /// </summary>
    /// <param name="methodName"></param>
    /// <param name="paramsJson"></param>
    /// <returns></returns>
    /// <exception cref="MissingMethodException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static object InvokeServiceMethod(string methodName, string paramsJson) {

      // Find all candidate methods by name
      var candidates = _MethodCache
        .Where(kvp => kvp.Key.StartsWith(methodName + "|") || kvp.Key == methodName)
        .Select(kvp => kvp.Value)
        .ToList();

      if (candidates.Count == 0)
        throw new MissingMethodException($"Method '{methodName}' not found.");

      // Parse JSON to dictionary
      Dictionary<string, JsonElement> paramDict = null;
      Dictionary<string, string> underscoreDict = null;

      if (string.IsNullOrWhiteSpace(paramsJson)) {
        paramDict = new Dictionary<string, JsonElement>();
      }
      else {
        try {
          paramDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paramsJson)
                     ?? new Dictionary<string, JsonElement>();
          // Extract special property "_" if present and is an object
          if (paramDict.TryGetValue("_", out var underscoreValue) && underscoreValue.ValueKind == JsonValueKind.Object) {
            var tempDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(underscoreValue.GetRawText());
            underscoreDict = tempDict?.ToDictionary(
              kvp => kvp.Key,
              kvp => kvp.Value.ValueKind == JsonValueKind.String
                ? kvp.Value.GetString()
                : kvp.Value.ToString()
            );
          }          
        }
        catch {
          paramDict = new Dictionary<string, JsonElement>();
        }
      }

      foreach (var (serviceType, method) in candidates) {
        var paramInfos = method.GetParameters();
        var orderedParams = new object[paramInfos.Length];
        bool conversionFailed = false;

        for (int i = 0; i < paramInfos.Length; i++) {
          var paramInfo = paramInfos[i];
          if (paramInfo.IsOut) {
            // For out parameters, create an instance of the parameter type
            orderedParams[i] = GetDefault(paramInfo.ParameterType);
            continue;
          }
          if (!paramDict.TryGetValue(paramInfo.Name, out var jsonValue)) {
            // Parameter missing in JSON
            if (paramInfo.HasDefaultValue) {
              orderedParams[i] = paramInfo.DefaultValue;
              continue;
            }
            conversionFailed = true;
            break;
          }
          try {
            orderedParams[i] = jsonValue.Deserialize(paramInfo.ParameterType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
          }
          catch {
            conversionFailed = true;
            break;
          }
        }
        if (conversionFailed)
          continue;

        if (underscoreDict != null) {
          UjmwHostConfiguration.GetRequestSideChannelConfiguration(serviceType)?.ProcessingMethod?.Invoke(
            method, underscoreDict
          );
        }

        var factory = _ServiceFactories[serviceType];
        var serviceInstance = factory.DynamicInvoke();
        var result = method.Invoke(serviceInstance, orderedParams);

        // Collect out/ref parameter values
        var outParams = new Dictionary<string, object>();
        for (int i = 0; i < paramInfos.Length; i++) {
          var paramInfo = paramInfos[i];
          if (paramInfo.IsOut || paramInfo.ParameterType.IsByRef) {
            outParams[paramInfo.Name] = orderedParams[i];
          }
        }

        // If there are out/ref parameters, return both result and out values
        var resultDict = new Dictionary<string, object> { { "return", result } };

        // Dynamically create an object with a property for Result and one for each out/ref parameter
        foreach (var kvp in outParams)
          resultDict[kvp.Key] = kvp.Value;
        return resultDict;

      }

      throw new ArgumentException($"No suitable overload for method '{methodName}' with provided parameters.");
    }

    /// <summary>
    /// Invokes a registered service method from command line arguments.
    /// </summary>
    /// <param name="args"></param>
    public static void InvokeFromCommandLine(string[] args) {
      if (args.Length < 1) {
        ProcessStdIn();
        return;
      }
      var methodName = args[0];
      var paramsJson = args.Length > 1 ? args[1] : "{}";
      try {
        var result = InvokeServiceMethod(methodName, paramsJson);
        if (result != null) {
          Console.WriteLine(JsonSerializer.Serialize(result));
        }
      }
      catch (Exception ex) {
        Console.WriteLine($"Error: {ex.Message}");

      }
    }

    /// <summary>
    /// Processes multiple method invocations from standard input concurrently.
    /// </summary>
    public static void ProcessStdIn() {
      string line;
      var tasks = new List<Task>();
      while ((line = Console.ReadLine()) != null) {
        if (string.IsNullOrWhiteSpace(line))
          continue;
        if (line.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase))
          break;

        // Split line into up to 3 parts: methodName, paramsJson, taskId
        var parts = line.Split(new[] { ' ' }, 3);
        var methodName = parts[0];
        var paramsJson = parts.Length > 1 ? parts[1] : "{}";
        var taskId = parts.Length > 2 ? parts[2] : null;

        // Start each invocation in a separate task
        var task = Task.Run(() => {
          try {
            var result = InvokeServiceMethod(methodName, paramsJson);
            var output = result != null ? JsonSerializer.Serialize(result) : string.Empty;
            if (!string.IsNullOrEmpty(taskId)) {
              Console.WriteLine($"{taskId} {output}");
            } else {
              Console.WriteLine(output);
            }
          }
          catch (Exception ex) {
            var errorMsg = $"Error: {ex.Message}";
            if (!string.IsNullOrEmpty(taskId)) {
              Console.WriteLine($"{taskId} {errorMsg}");
            } else {
              Console.WriteLine(errorMsg);
            }
          }
        });
        tasks.Add(task);
      }
      Task.WaitAll(tasks.ToArray());
    }

    // Helper to get default value for a type
    private static object GetDefault(Type type) {
      if (type.IsValueType) return Activator.CreateInstance(type);
      return null;
    }
  }
}
