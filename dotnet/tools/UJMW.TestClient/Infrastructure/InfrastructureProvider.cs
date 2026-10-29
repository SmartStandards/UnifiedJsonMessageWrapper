using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Web.UJMW;
using UJMW.TestClient.Model;

namespace UJMW.TestClient.Infrastructure {

  /// <summary>
  /// Creates the 'IUjmwManagedInfrastructure' which is selected in the settings:
  /// 'Local' (JSON file per user), 'External' (UJMW client to a remote store)
  /// or 'Reflection' (an implementation offered by a DLL located next to the client).
  /// </summary>
  internal static class InfrastructureProvider {

    private const string _ContractAssemblyName = "UJMW.DynamicClient";

    /// <summary>
    /// The file which is used by the local store of the test client.
    /// </summary>
    public static string LocalStoreFilePath {
      get {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "UJMW.TestClient", "infrastructure.json");
      }
    }

    /// <summary>
    /// Creates the configured store.
    /// </summary>
    public static IUjmwManagedInfrastructure Create(InfrastructureSettings settings) {
      if (settings.Mode == InfrastructureSettings._ModeExternal) {
        if (string.IsNullOrWhiteSpace(settings.ExternalUrl)) {
          throw new InvalidOperationException("The url of the externally managed infrastructure is not configured.");
        }
        string url = settings.ExternalUrl.Trim();
        string authHeader = NormalizeAuthHeader(settings.ExternalAuthHeader);
        return DynamicClientFactory.CreateInstance<IUjmwManagedInfrastructure>(() => url, () => authHeader);
      }
      if (settings.Mode == InfrastructureSettings._ModeReflection) {
        return CreateFromReflection(settings.ReflectionTypeName);
      }
      return new LocalUjmwManagedInfrastructure(LocalStoreFilePath);
    }

    /// <summary>
    /// Returns the assembly qualified names of all public, non-abstract implementations of
    /// 'IUjmwManagedInfrastructure' (with a parameterless constructor) in DLLs next to the client.
    /// Only assemblies which reference 'UJMW.DynamicClient' are loaded.
    /// </summary>
    public static string[] FindReflectionCandidates() {
      List<string> candidates = new List<string>();
      string ownAssemblyPath = typeof(InfrastructureProvider).Assembly.Location;
      foreach (string dllPath in Directory.GetFiles(AppContext.BaseDirectory, "*.dll")) {
        if (string.Equals(dllPath, ownAssemblyPath, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }
        if (!ReferencesContractAssembly(dllPath)) {
          continue;
        }
        Assembly assembly;
        try {
          assembly = Assembly.LoadFrom(dllPath);
        } catch (BadImageFormatException) {
          continue;
        } catch (FileLoadException) {
          continue;
        }
        foreach (Type type in GetLoadableTypes(assembly)) {
          if (IsUsableImplementation(type)) {
            candidates.Add(type.AssemblyQualifiedName);
          }
        }
      }
      return candidates.OrderBy((c) => c, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Instantiates an implementation by its assembly qualified name.
    /// </summary>
    private static IUjmwManagedInfrastructure CreateFromReflection(string assemblyQualifiedName) {
      if (string.IsNullOrWhiteSpace(assemblyQualifiedName)) {
        throw new InvalidOperationException("No infrastructure implementation was selected.");
      }
      //make sure, that the candidate assemblies are loaded (from the client directory)
      Type type = Type.GetType(assemblyQualifiedName, false);
      if (type == null) {
        FindReflectionCandidates();
        type = Type.GetType(assemblyQualifiedName, false);
      }
      if (type == null) {
        type = AppDomain.CurrentDomain.GetAssemblies()
          .SelectMany((a) => GetLoadableTypes(a))
          .FirstOrDefault((t) => t.AssemblyQualifiedName == assemblyQualifiedName);
      }
      if (type == null || !IsUsableImplementation(type)) {
        throw new InvalidOperationException($"The infrastructure implementation '{assemblyQualifiedName}' is not available.");
      }
      return (IUjmwManagedInfrastructure)Activator.CreateInstance(type);
    }

    /// <summary>
    /// Returns true for public, non-abstract classes implementing the interface with a parameterless constructor.
    /// </summary>
    private static bool IsUsableImplementation(Type type) {
      if (!type.IsClass || type.IsAbstract || !type.IsPublic) {
        return false;
      }
      if (!typeof(IUjmwManagedInfrastructure).IsAssignableFrom(type)) {
        return false;
      }
      return type.GetConstructor(Type.EmptyTypes) != null;
    }

    /// <summary>
    /// Reads the assembly references from the metadata (without loading the assembly).
    /// </summary>
    private static bool ReferencesContractAssembly(string dllPath) {
      try {
        using (FileStream stream = File.OpenRead(dllPath))
        using (PEReader peReader = new PEReader(stream)) {
          if (!peReader.HasMetadata) {
            return false;
          }
          MetadataReader metadataReader = peReader.GetMetadataReader();
          if (metadataReader.IsAssembly && metadataReader.GetString(metadataReader.GetAssemblyDefinition().Name) == _ContractAssemblyName) {
            return false;
          }
          foreach (AssemblyReferenceHandle handle in metadataReader.AssemblyReferences) {
            AssemblyReference reference = metadataReader.GetAssemblyReference(handle);
            if (metadataReader.GetString(reference.Name) == _ContractAssemblyName) {
              return true;
            }
          }
        }
      } catch (BadImageFormatException) {
        return false;
      } catch (IOException) {
        return false;
      }
      return false;
    }

    /// <summary>
    /// Returns all types which can be loaded.
    /// </summary>
    private static Type[] GetLoadableTypes(Assembly assembly) {
      try {
        return assembly.GetTypes();
      } catch (ReflectionTypeLoadException ex) {
        return ex.Types.Where((t) => t != null).ToArray();
      }
    }

    /// <summary>
    /// A plain token (without scheme) is sent as bearer token.
    /// </summary>
    private static string NormalizeAuthHeader(string authHeader) {
      if (string.IsNullOrWhiteSpace(authHeader)) {
        return null;
      }
      string trimmed = authHeader.Trim();
      if (trimmed.Contains(" ")) {
        return trimmed;
      }
      return "Bearer " + trimmed;
    }

  }

}
