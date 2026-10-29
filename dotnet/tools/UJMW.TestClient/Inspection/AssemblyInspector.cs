using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace UJMW.TestClient.Inspection {

  /// <summary>
  /// Inspects arbitrary .NET assemblies (any target framework) via MetadataLoadContext,
  /// so that no code of the inspected assembly is ever executed.
  /// </summary>
  internal static class AssemblyInspector {

    public const string _KindInterface = "Interface";
    public const string _KindClass = "Class";
    public const string _KindStatic = "Static";

    private static readonly Dictionary<string, string> _KeywordTypeNames = new Dictionary<string, string>() {
      { "System.Void", "void" },
      { "System.Object", "object" },
      { "System.String", "string" },
      { "System.Boolean", "bool" },
      { "System.Byte", "byte" },
      { "System.SByte", "sbyte" },
      { "System.Int16", "short" },
      { "System.UInt16", "ushort" },
      { "System.Int32", "int" },
      { "System.UInt32", "uint" },
      { "System.Int64", "long" },
      { "System.UInt64", "ulong" },
      { "System.Single", "float" },
      { "System.Double", "double" },
      { "System.Decimal", "decimal" },
      { "System.Char", "char" }
    };

    /// <summary>
    /// Returns the display text for the main tree: assembly name + version.
    /// </summary>
    public static string GetDisplayName(string assemblyPath) {
      string fileName = Path.GetFileName(assemblyPath);
      if (!File.Exists(assemblyPath)) {
        return fileName;
      }
      AssemblyName assemblyName;
      try {
        assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
      } catch (BadImageFormatException) {
        return fileName + " (no .NET assembly)";
      }
      if (assemblyName.Version == null) {
        return assemblyName.Name;
      }
      return assemblyName.Name + " (" + assemblyName.Version.ToString() + ")";
    }

    /// <summary>
    /// Returns all namespaces of the assembly including its public classes and interfaces.
    /// </summary>
    public static NamespaceDescriptor[] GetNamespaces(string assemblyPath) {
      EnsureFileExists(assemblyPath);
      using (MetadataLoadContext context = CreateContext(assemblyPath)) {
        Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);
        Dictionary<string, List<TypeDescriptor>> typesByNamespace = new Dictionary<string, List<TypeDescriptor>>();

        foreach (Type type in GetLoadableTypes(assembly)) {
          if (!type.IsVisible || type.FullName == null || type.FullName.Contains('<')) {
            continue;
          }
          string kind = GetKind(type);
          if (kind == null) {
            continue;
          }
          string namespaceName = type.Namespace;
          if (namespaceName == null) {
            namespaceName = string.Empty;
          }
          TypeDescriptor descriptor = new TypeDescriptor();
          descriptor.FullName = type.FullName;
          descriptor.DisplayName = GetDisplayTypeName(type);
          descriptor.Kind = kind;
          List<TypeDescriptor> namespaceTypes;
          if (!typesByNamespace.TryGetValue(namespaceName, out namespaceTypes)) {
            namespaceTypes = new List<TypeDescriptor>();
            typesByNamespace[namespaceName] = namespaceTypes;
          }
          namespaceTypes.Add(descriptor);
        }

        List<NamespaceDescriptor> result = new List<NamespaceDescriptor>();
        foreach (KeyValuePair<string, List<TypeDescriptor>> entry in typesByNamespace.OrderBy((e) => e.Key, StringComparer.OrdinalIgnoreCase)) {
          NamespaceDescriptor namespaceDescriptor = new NamespaceDescriptor();
          namespaceDescriptor.Name = entry.Key;
          namespaceDescriptor.Types = entry.Value.OrderBy((t) => t.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
          result.Add(namespaceDescriptor);
        }
        return result.ToArray();
      }
    }

    /// <summary>
    /// Returns all methods (UJMW operations) of the given type including prepared request templates.
    /// </summary>
    public static MethodDescriptor[] GetMethods(string assemblyPath, string typeFullName) {
      EnsureFileExists(assemblyPath);
      using (MetadataLoadContext context = CreateContext(assemblyPath)) {
        Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);
        Type type = assembly.GetType(typeFullName, false);
        if (type == null) {
          throw new InvalidOperationException($"Type '{typeFullName}' was not found in '{assemblyPath}'.");
        }
        List<MethodDescriptor> result = new List<MethodDescriptor>();
        foreach (MethodInfo method in CollectMethods(type)) {
          MethodDescriptor descriptor = new MethodDescriptor();
          descriptor.Name = method.Name;
          descriptor.Signature = BuildSignature(method);
          descriptor.RequestTemplate = RequestTemplateBuilder.BuildRequestTemplate(method);
          result.Add(descriptor);
        }
        return result.ToArray();
      }
    }

    /// <summary>
    /// Returns a C#-like, human readable type name (e.g. 'List&lt;string&gt;', 'int?').
    /// </summary>
    public static string GetFriendlyTypeName(Type type) {
      if (type.IsByRef) {
        return GetFriendlyTypeName(type.GetElementType());
      }
      if (type.IsArray) {
        return GetFriendlyTypeName(type.GetElementType()) + "[]";
      }
      if (type.IsGenericParameter) {
        return type.Name;
      }
      if (type.IsGenericType) {
        Type[] arguments = type.GetGenericArguments();
        string definitionName = type.GetGenericTypeDefinition().FullName;
        if (definitionName == "System.Nullable`1") {
          return GetFriendlyTypeName(arguments[0]) + "?";
        }
        string name = type.Name;
        int tickIndex = name.IndexOf('`');
        if (tickIndex >= 0) {
          name = name.Substring(0, tickIndex);
        }
        return name + "<" + string.Join(", ", arguments.Select((a) => GetFriendlyTypeName(a))) + ">";
      }
      string keywordName;
      if (type.FullName != null && _KeywordTypeNames.TryGetValue(type.FullName, out keywordName)) {
        return keywordName;
      }
      return type.Name;
    }

    /// <summary>
    /// Creates a MetadataLoadContext which can resolve references from the folder of the inspected
    /// assembly and from all shared frameworks of the currently running .NET runtime.
    /// </summary>
    private static MetadataLoadContext CreateContext(string assemblyPath) {
      List<string> candidatePaths = new List<string>();
      candidatePaths.Add(assemblyPath);

      string assemblyDirectory = Path.GetDirectoryName(assemblyPath);
      candidatePaths.AddRange(Directory.GetFiles(assemblyDirectory, "*.dll"));

      DirectoryInfo runtimeDirectory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory().TrimEnd('\\', '/'));
      candidatePaths.AddRange(Directory.GetFiles(runtimeDirectory.FullName, "*.dll"));

      DirectoryInfo frameworkDirectory = runtimeDirectory.Parent;
      if (frameworkDirectory != null && frameworkDirectory.Parent != null) {
        foreach (DirectoryInfo siblingFramework in frameworkDirectory.Parent.GetDirectories()) {
          if (string.Equals(siblingFramework.FullName, frameworkDirectory.FullName, StringComparison.OrdinalIgnoreCase)) {
            continue;
          }
          DirectoryInfo versionDirectory = SelectFrameworkVersionDirectory(siblingFramework, runtimeDirectory.Name);
          if (versionDirectory != null) {
            candidatePaths.AddRange(Directory.GetFiles(versionDirectory.FullName, "*.dll"));
          }
        }
      }

      PathAssemblyResolver resolver = new PathAssemblyResolver(candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase));
      return new MetadataLoadContext(resolver, typeof(object).Assembly.GetName().Name);
    }

    /// <summary>
    /// Selects the version folder of a shared framework which matches the running runtime version
    /// (or the highest available version as fallback).
    /// </summary>
    private static DirectoryInfo SelectFrameworkVersionDirectory(DirectoryInfo framework, string runtimeVersion) {
      DirectoryInfo[] versions = framework.GetDirectories();
      foreach (DirectoryInfo version in versions) {
        if (version.Name == runtimeVersion) {
          return version;
        }
      }
      return versions.OrderByDescending((v) => v.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    /// <summary>
    /// Returns all types which can be read, also when some of them reference missing assemblies.
    /// </summary>
    private static Type[] GetLoadableTypes(Assembly assembly) {
      try {
        return assembly.GetTypes();
      } catch (ReflectionTypeLoadException ex) {
        return ex.Types.Where((t) => t != null).ToArray();
      }
    }

    /// <summary>
    /// Returns the kind of the type or null if it should not be listed (structs, enums).
    /// </summary>
    private static string GetKind(Type type) {
      if (type.IsInterface) {
        return _KindInterface;
      }
      try {
        if (type.IsValueType) {
          return null;
        }
      } catch (FileNotFoundException) {
        //the base type lives in an assembly which is not available -> it cannot be a struct or enum
      }
      if (type.IsAbstract && type.IsSealed) {
        return _KindStatic;
      }
      return _KindClass;
    }

    /// <summary>
    /// Returns the type name without namespace (nested types are separated by '.').
    /// </summary>
    private static string GetDisplayTypeName(Type type) {
      string name = type.FullName;
      if (!string.IsNullOrEmpty(type.Namespace)) {
        name = name.Substring(type.Namespace.Length + 1);
      }
      return name.Replace('+', '.');
    }

    /// <summary>
    /// Collects the public instance methods of a type (for interfaces including all inherited interfaces).
    /// </summary>
    private static MethodInfo[] CollectMethods(Type type) {
      List<MethodInfo> methods = new List<MethodInfo>();
      if (type.IsInterface) {
        AddOperationMethods(type.GetMethods(BindingFlags.Public | BindingFlags.Instance), methods);
        Type[] inheritedInterfaces;
        try {
          inheritedInterfaces = type.GetInterfaces();
        } catch (FileNotFoundException) {
          inheritedInterfaces = new Type[0];
        }
        foreach (Type inheritedInterface in inheritedInterfaces) {
          AddOperationMethods(inheritedInterface.GetMethods(BindingFlags.Public | BindingFlags.Instance), methods);
        }
      } else {
        MethodInfo[] classMethods;
        try {
          classMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        } catch (FileNotFoundException) {
          classMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }
        AddOperationMethods(classMethods, methods);
      }
      return methods.OrderBy((m) => m.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Adds all methods which represent operations (no property/event accessors, no System.Object members).
    /// </summary>
    private static void AddOperationMethods(MethodInfo[] candidates, List<MethodInfo> target) {
      foreach (MethodInfo candidate in candidates) {
        if (candidate.IsSpecialName) {
          continue;
        }
        if (candidate.DeclaringType != null && candidate.DeclaringType.FullName == "System.Object") {
          continue;
        }
        target.Add(candidate);
      }
    }

    /// <summary>
    /// Builds a C#-like signature text for the method.
    /// </summary>
    private static string BuildSignature(MethodInfo method) {
      StringBuilder signature = new StringBuilder();
      signature.Append(GetFriendlyTypeName(method.ReturnType));
      signature.Append(' ');
      signature.Append(method.Name);
      signature.Append('(');
      ParameterInfo[] parameters = method.GetParameters();
      for (int i = 0; i < parameters.Length; i++) {
        ParameterInfo parameter = parameters[i];
        if (i > 0) {
          signature.Append(", ");
        }
        if (parameter.ParameterType.IsByRef) {
          if (parameter.IsOut) {
            signature.Append("out ");
          } else {
            signature.Append("ref ");
          }
        }
        signature.Append(GetFriendlyTypeName(parameter.ParameterType));
        signature.Append(' ');
        signature.Append(parameter.Name);
        if (parameter.HasDefaultValue) {
          signature.Append(" = ");
          if (parameter.RawDefaultValue == null) {
            signature.Append("null");
          } else {
            signature.Append(parameter.RawDefaultValue.ToString());
          }
        }
      }
      signature.Append(')');
      return signature.ToString();
    }

    /// <summary>
    /// Throws a meaningful exception if the assembly file does not exist (anymore).
    /// </summary>
    private static void EnsureFileExists(string assemblyPath) {
      if (!File.Exists(assemblyPath)) {
        throw new FileNotFoundException($"The file '{assemblyPath}' does not exist (anymore).", assemblyPath);
      }
    }

  }

}
