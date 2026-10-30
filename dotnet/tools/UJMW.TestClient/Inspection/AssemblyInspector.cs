using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UJMW.TestClient.Inspection {

  /// <summary>
  /// Inspects arbitrary .NET assemblies (any target framework) via MetadataLoadContext,
  /// so that no code of the inspected assembly is ever executed.
  /// </summary>
  internal static class AssemblyInspector {

    public const string _KindInterface = "Interface";
    public const string _KindClass = "Class";
    public const string _KindStatic = "Static";

    private const string _InfrastructureContractFullName = "System.Web.UJMW.IUjmwManagedInfrastructure";

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
          descriptor.IsInfrastructureProvider = IsInfrastructureProvider(type);
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
    /// Returns true when the type can be registered as store provider (public, non-abstract class
    /// with a public parameterless constructor implementing 'IUjmwManagedInfrastructure').
    /// </summary>
    private static bool IsInfrastructureProvider(Type type) {
      if (!type.IsClass || type.IsAbstract || !type.IsPublic || type.IsGenericTypeDefinition) {
        return false;
      }
      try {
        bool implementsContract = type.GetInterfaces().Any((i) => i.FullName == _InfrastructureContractFullName);
        if (!implementsContract) {
          return false;
        }
        return type.GetConstructors().Any((c) => c.GetParameters().Length == 0);
      } catch (FileNotFoundException) {
        //a referenced assembly is not resolvable -> can not be a usable provider
        return false;
      } catch (TypeLoadException) {
        return false;
      }
    }

    /// <summary>
    /// Returns the documentation and all methods (UJMW operations) of the given type including prepared request templates.
    /// </summary>
    public static ContractDescriptor GetContract(string assemblyPath, string typeFullName) {
      EnsureFileExists(assemblyPath);
      using (MetadataLoadContext context = CreateContext(assemblyPath)) {
        Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);
        Type type = assembly.GetType(typeFullName, false);
        if (type == null) {
          throw new InvalidOperationException($"Type '{typeFullName}' was not found in '{assemblyPath}'.");
        }
        XmlDocReader typeDocs = XmlDocReader.ForAssemblyPath(assemblyPath);
        ContractDescriptor contract = new ContractDescriptor();
        contract.HasDocumentationFile = File.Exists(Path.ChangeExtension(assemblyPath, ".xml"));
        contract.Summary = typeDocs.GetTypeSummary(type);
        contract.Warning = GetBaseTypeResolutionWarning(type);
        contract.FuseEntity = FuseRepositoryDetector.TryDetect(type);

        List<MethodDescriptor> result = new List<MethodDescriptor>();
        foreach (MethodInfo method in CollectMethods(type)) {
          XmlDocReader methodDocs = typeDocs;
          if (method.DeclaringType != null && method.DeclaringType.Assembly != assembly) {
            methodDocs = XmlDocReader.ForAssembly(method.DeclaringType.Assembly);
          }
          MethodDescriptor descriptor = new MethodDescriptor();
          descriptor.Name = method.Name;
          descriptor.Signature = BuildSignature(method);
          descriptor.RequestTemplate = RequestTemplateBuilder.BuildRequestTemplate(method);
          descriptor.Summary = methodDocs.GetMethodSummary(method);
          descriptor.ReturnTypeName = GetFriendlyTypeName(method.ReturnType);
          descriptor.ReturnsDescription = methodDocs.GetMethodReturns(method);
          descriptor.ReturnMembers = BuildMembers(method.ReturnType);
          descriptor.Parameters = BuildParameters(method, methodDocs);
          result.Add(descriptor);
        }
        contract.Methods = result.ToArray();
        return contract;
      }
    }

    /// <summary>
    /// Returns a warning text if the inherited interfaces / base types of the type can not be resolved
    /// (in this case inherited methods and the FUSE data mode are not available), otherwise null.
    /// </summary>
    private static string GetBaseTypeResolutionWarning(Type type) {

      try {
        type.GetInterfaces();
        Type baseType = type.BaseType;
        while (baseType != null) {
          baseType = baseType.BaseType;
        }
      } catch (FileNotFoundException ex) {
        //expected: a referenced assembly is neither next to the DLL nor listed in its deps.json / the NuGet cache
        return (
          "Not all inherited interfaces/base types could be resolved (missing assembly: '" + ex.FileName + "'). " +
          "Inherited methods (and the FUSE data mode) are therefore not available. " +
          "Place the missing assembly next to the DLL or restore the NuGet packages of the contract project."
        );
      } catch (TypeLoadException ex) {
        return "Not all inherited interfaces/base types could be resolved: " + ex.Message;
      }

      return null;
    }

    /// <summary>
    /// Builds the parameter descriptors of a method (including their documentation).
    /// </summary>
    private static ParameterDescriptor[] BuildParameters(MethodInfo method, XmlDocReader docs) {
      List<ParameterDescriptor> parameters = new List<ParameterDescriptor>();
      foreach (ParameterInfo parameter in method.GetParameters()) {
        ParameterDescriptor descriptor = new ParameterDescriptor();
        descriptor.Name = parameter.Name;
        descriptor.TypeName = GetFriendlyTypeName(parameter.ParameterType);
        if (parameter.ParameterType.IsByRef) {
          if (parameter.IsOut) {
            descriptor.Direction = ParameterDescriptor._DirectionOut;
          } else {
            descriptor.Direction = ParameterDescriptor._DirectionRef;
          }
        }
        descriptor.IsOptional = parameter.HasDefaultValue;
        descriptor.Description = docs.GetParameterDescription(method, parameter.Name);
        descriptor.Members = BuildMembers(parameter.ParameterType);
        parameters.Add(descriptor);
      }
      return parameters.ToArray();
    }

    /// <summary>
    /// Builds the descriptors of the public properties/fields of a complex type (one level deep),
    /// so that the documentation of wrapper types like 'MyMethodRequest' becomes visible.
    /// Simple types, enums, interfaces and framework types return an empty array.
    /// </summary>
    private static MemberDescriptor[] BuildMembers(Type type) {

      Type complexType = GetDocumentableComplexType(type);
      if (complexType == null) {
        return new MemberDescriptor[0];
      }

      XmlDocReader docs = XmlDocReader.ForAssembly(complexType.Assembly);
      List<MemberDescriptor> members = new List<MemberDescriptor>();
      HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

      PropertyInfo[] properties;
      FieldInfo[] fields;
      try {
        properties = complexType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        fields = complexType.GetFields(BindingFlags.Public | BindingFlags.Instance);
      } catch (FileNotFoundException) {
        //a base type lives in an assembly which is not available
        properties = complexType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        fields = complexType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
      }

      foreach (PropertyInfo property in properties) {
        if (property.GetIndexParameters().Length > 0 || property.GetGetMethod() == null) {
          continue;
        }
        AddMember(members, names, property, property.PropertyType);
      }

      foreach (FieldInfo field in fields) {
        AddMember(members, names, field, field.FieldType);
      }

      return members.ToArray();
    }

    /// <summary>
    /// Adds the descriptor of a single property/field (documentation is read from the declaring assembly).
    /// </summary>
    private static void AddMember(List<MemberDescriptor> members, HashSet<string> names, MemberInfo member, Type memberType) {

      string jsonName = RequestTemplateBuilder.GetJsonName(member.Name);
      if (!names.Add(jsonName)) {
        return;
      }

      XmlDocReader docs = XmlDocReader.ForAssembly(member.DeclaringType.Assembly);
      MemberDescriptor descriptor = new MemberDescriptor();
      descriptor.Name = jsonName;
      descriptor.TypeName = GetFriendlyTypeName(memberType);
      descriptor.Description = docs.GetMemberSummary(member);
      members.Add(descriptor);
    }

    /// <summary>
    /// Returns the complex type whose members should be documented (by-ref and array element types
    /// are unwrapped), or null if the type is simple, an enum, an interface or a framework type.
    /// </summary>
    private static Type GetDocumentableComplexType(Type type) {

      Type current = type;
      while (current.IsByRef || current.IsArray) {
        current = current.GetElementType();
      }

      if (current.IsGenericParameter || current.IsPrimitive || current.IsEnum || current.IsInterface || current.IsPointer) {
        return null;
      }

      if (current.FullName == null) {
        return null;
      }

      string assemblyName = current.Assembly.GetName().Name;
      bool isFrameworkAssembly = (
        assemblyName == "mscorlib" || assemblyName == "netstandard" ||
        assemblyName.StartsWith("System.", StringComparison.Ordinal) || assemblyName == "System" ||
        assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal)
      );
      if (isFrameworkAssembly) {
        return null;
      }

      return current;
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
      candidatePaths.AddRange(GetPackageAssemblyPathsFromDepsFiles(assemblyDirectory));

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
    /// Returns the paths of all NuGet package assemblies which are listed in the '*.deps.json' files of the
    /// given folder and exist within the global NuGet packages folder. This makes references resolvable which
    /// are not copied next to the inspected assembly (e.g. the 'dist' output of a class library).
    /// </summary>
    internal static string[] GetPackageAssemblyPathsFromDepsFiles(string assemblyDirectory) {

      List<string> result = new List<string>();
      string packagesRoot = GetNuGetPackagesRoot();
      if (!Directory.Exists(packagesRoot)) {
        return result.ToArray();
      }

      foreach (string depsFile in Directory.GetFiles(assemblyDirectory, "*.deps.json")) {

        JObject deps;
        try {
          deps = JObject.Parse(File.ReadAllText(depsFile));
        } catch (JsonReaderException) {
          //an invalid deps file is simply ignored (it is only a resolution hint)
          continue;
        }

        JObject libraries = deps["libraries"] as JObject;
        JObject targets = deps["targets"] as JObject;
        if (libraries == null || targets == null) {
          continue;
        }

        foreach (JProperty target in targets.Properties()) {

          JObject targetLibraries = target.Value as JObject;
          if (targetLibraries == null) {
            continue;
          }

          foreach (JProperty targetLibrary in targetLibraries.Properties()) {

            JObject library = libraries[targetLibrary.Name] as JObject;
            if (library == null || library.Value<string>("type") != "package") {
              continue;
            }

            string libraryPath = library.Value<string>("path");
            JObject runtime = targetLibrary.Value["runtime"] as JObject;
            if (string.IsNullOrEmpty(libraryPath) || runtime == null) {
              continue;
            }

            foreach (JProperty runtimeAssembly in runtime.Properties()) {
              string candidate = Path.Combine(packagesRoot, libraryPath, runtimeAssembly.Name.Replace('/', Path.DirectorySeparatorChar));
              if (File.Exists(candidate)) {
                result.Add(candidate);
              }
            }
          }
        }
      }

      return result.ToArray();
    }

    /// <summary>
    /// Returns the global NuGet packages folder ('NUGET_PACKAGES' or '%USERPROFILE%\.nuget\packages').
    /// </summary>
    private static string GetNuGetPackagesRoot() {

      string configuredRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
      if (!string.IsNullOrWhiteSpace(configuredRoot)) {
        return configuredRoot;
      }

      return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
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
