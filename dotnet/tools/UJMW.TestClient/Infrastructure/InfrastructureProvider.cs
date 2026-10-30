using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Web.UJMW;
using UJMW.TestClient.Inspection;
using UJMW.TestClient.Model;

namespace UJMW.TestClient.Infrastructure {

	/// <summary>
	/// Creates the 'IUjmwManagedInfrastructure' which is selected in the settings:
	/// 'FileBased' (JSON file, by default per user), 'External' (UJMW client to a remote store)
	/// or 'Provider' (an implementation which was registered from a DLL added to the client).
	/// </summary>
	internal static class InfrastructureProvider {

		private static readonly Dictionary<string, ProviderLoadContext> _LoadContexts = new Dictionary<string, ProviderLoadContext>(StringComparer.OrdinalIgnoreCase);
		private static readonly object _LoadContextsLock = new object();

		/// <summary>
		/// The default file of the file based store (located in the roaming profile of the user).
		/// </summary>
		public static string DefaultFileBasedStorePath {
			get {
				string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
				return Path.Combine(appData, "UJMW.TestClient", "infrastructure.json");
			}
		}

		/// <summary>
		/// Returns the configured file of the file based store (the default file if nothing is configured).
		/// Environment variables (e.g. '%APPDATA%') are expanded.
		/// </summary>
		public static string GetFileBasedStorePath(InfrastructureSettings settings) {

			if (string.IsNullOrWhiteSpace(settings.FileBasedStorePath)) {
				return DefaultFileBasedStorePath;
			}

			return Environment.ExpandEnvironmentVariables(settings.FileBasedStorePath.Trim().Trim('"'));
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
			if (settings.Mode == InfrastructureSettings._ModeProvider) {
				RegisteredProvider provider = settings.RegisteredProviders.FirstOrDefault(
					(p) => p.TypeFullName == settings.ProviderTypeName
				);
				if (provider == null) {
					throw new InvalidOperationException($"The store provider '{settings.ProviderTypeName}' is not registered.");
				}
				return CreateFromRegisteredProvider(provider);
			}
			return new FileBasedUjmwManagedInfrastructure(GetFileBasedStorePath(settings));
		}

		/// <summary>
		/// Instantiates a registered provider. The DLL is loaded from its registered path into an own
		/// load context (dependencies are resolved from the DLL folder, 'UJMW.DynamicClient' and all
		/// assemblies of the client itself are shared with the client).
		/// </summary>
		private static IUjmwManagedInfrastructure CreateFromRegisteredProvider(RegisteredProvider provider) {
			if (!File.Exists(provider.DllPath)) {
				throw new InvalidOperationException($"The DLL of the store provider '{provider.TypeFullName}' was not found: '{provider.DllPath}'.");
			}

			ProviderLoadContext context = GetLoadContext(provider.DllPath);
			string header = $"The store provider '{provider.TypeFullName}' from '{provider.DllPath}' could not be loaded";

			//phase 1: load the provider assembly itself
			Assembly assembly;
			try {
				assembly = context.LoadFromAssemblyPath(Path.GetFullPath(provider.DllPath));
			} catch (Exception ex) {
				throw new InvalidOperationException(BuildDiagnostic(header, "loading the provider assembly", ex, context), ex);
			}

			//phase 2: resolve the type (this also loads the assemblies of base types and interfaces)
			Type type;
			try {
				type = assembly.GetType(provider.TypeFullName, true);
			} catch (Exception ex) {
				throw new InvalidOperationException(BuildDiagnostic(header, "resolving the provider type", ex, context), ex);
			}

			//phase 3: validate the contract requirements (each violated requirement is reported explicitly)
			string violation = GetImplementationViolation(type);
			if (violation != null) {
				throw new InvalidOperationException(header + ": " + violation);
			}

			//phase 4: invoke the constructor
			object instance;
			try {
				instance = Activator.CreateInstance(type);
			} catch (Exception ex) {
				throw new InvalidOperationException(BuildDiagnostic(header, "invoking the parameterless constructor", ex, context), ex);
			}

			return (IUjmwManagedInfrastructure)instance;
		}

		/// <summary>
		/// Builds a multi-line diagnostic message containing the failed phase, the complete exception
		/// chain (including loader exceptions and missing files) and the assemblies which could not be
		/// resolved by the provider load context.
		/// </summary>
		private static string BuildDiagnostic(string header, string phase, Exception ex, ProviderLoadContext context) {

			List<string> lines = new List<string>();
			lines.Add(header + " (failed while " + phase + ").");

			Exception current = ex;
			int depth = 0;
			while (current != null && depth < 10) {

				lines.Add(new string(' ', depth * 2) + "-> " + current.GetType().Name + ": " + current.Message);

				FileNotFoundException fileNotFound = current as FileNotFoundException;
				if (fileNotFound != null && !string.IsNullOrEmpty(fileNotFound.FileName)) {
					lines.Add(new string(' ', depth * 2) + "   missing: " + fileNotFound.FileName);
				}

				FileLoadException fileLoad = current as FileLoadException;
				if (fileLoad != null && !string.IsNullOrEmpty(fileLoad.FileName)) {
					lines.Add(new string(' ', depth * 2) + "   not loadable: " + fileLoad.FileName);
				}

				ReflectionTypeLoadException typeLoad = current as ReflectionTypeLoadException;
				if (typeLoad != null) {
					foreach (string loaderMessage in typeLoad.LoaderExceptions.Where((le) => le != null).Select((le) => le.Message).Distinct()) {
						lines.Add(new string(' ', depth * 2) + "   loader: " + loaderMessage);
					}
				}

				current = current.InnerException;
				depth++;
			}

			string[] unresolved = context.GetUnresolvedAssemblies();
			if (unresolved.Length > 0) {
				lines.Add("Assemblies which could not be found (neither in the client, in the provider folder nor via its '*.deps.json' in the NuGet cache):");
				foreach (string name in unresolved) {
					lines.Add("  - " + name);
				}
				lines.Add("Probed provider folder: " + context.Directory);
			}

			return string.Join(Environment.NewLine, lines);
		}

		/// <summary>
		/// Returns the (cached) load context for a provider DLL.
		/// </summary>
		private static ProviderLoadContext GetLoadContext(string dllPath) {
			string fullPath = Path.GetFullPath(dllPath);
			lock (_LoadContextsLock) {
				ProviderLoadContext context;
				if (!_LoadContexts.TryGetValue(fullPath, out context)) {
					context = new ProviderLoadContext(fullPath);
					_LoadContexts[fullPath] = context;
				}
				return context;
			}
		}

		/// <summary>
		/// Returns null for public, non-abstract classes implementing the interface with a parameterless constructor,
		/// otherwise a description of the violated requirement.
		/// </summary>
		private static string GetImplementationViolation(Type type) {

			if (!type.IsClass) {
				return "the type is not a class.";
			}
			if (type.IsAbstract) {
				return "the type is abstract.";
			}
			if (!type.IsPublic) {
				return "the type is not public.";
			}

			if (!typeof(IUjmwManagedInfrastructure).IsAssignableFrom(type)) {

				Type foreignInterface = type.GetInterfaces().FirstOrDefault(
					(i) => i.FullName == typeof(IUjmwManagedInfrastructure).FullName
				);
				if (foreignInterface != null) {
					return $"the type implements '{foreignInterface.FullName}' from '{foreignInterface.Assembly.FullName}' (location: '{foreignInterface.Assembly.Location}'), " +
						$"but the client uses '{typeof(IUjmwManagedInfrastructure).Assembly.FullName}' - the interface was loaded twice (type identity mismatch).";
				}
				return "the type does not implement 'IUjmwManagedInfrastructure'.";
			}

			if (type.GetConstructor(Type.EmptyTypes) == null) {
				return "the type has no public parameterless constructor.";
			}

			return null;
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

		/// <summary>
		/// Load context for a provider DLL: assemblies which are part of the client (especially
		/// 'UJMW.DynamicClient' containing the interface) are taken from the default context,
		/// all others are loaded from the folder of the provider DLL or (as fallback) from the
		/// global NuGet cache based on the '*.deps.json' of the provider folder.
		/// </summary>
		private sealed class ProviderLoadContext : AssemblyLoadContext {

			private readonly string _Directory;
			private readonly object _SyncRoot = new object();
			private readonly HashSet<string> _Unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			private Dictionary<string, string> _PackageAssemblies = null;

			public ProviderLoadContext(string dllPath) : base("UJMW.TestClient.Provider:" + dllPath, false) {
				_Directory = Path.GetDirectoryName(dllPath);
			}

			/// <summary>
			/// The folder of the provider DLL (first probing location).
			/// </summary>
			public string Directory {
				get {
					return _Directory;
				}
			}

			/// <summary>
			/// Returns the names of all assemblies which were requested but could not be resolved.
			/// </summary>
			public string[] GetUnresolvedAssemblies() {

				lock (_SyncRoot) {
					return _Unresolved.OrderBy((n) => n).ToArray();
				}
			}

			/// <summary>
			/// Resolves dependencies of the provider DLL.
			/// </summary>
			protected override Assembly Load(AssemblyName assemblyName) {

				string clientCandidate = Path.Combine(AppContext.BaseDirectory, assemblyName.Name + ".dll");
				string localCandidate = Path.Combine(_Directory, assemblyName.Name + ".dll");

				//assemblies of the client are shared (type identity of 'IUjmwManagedInfrastructure'),
				//but only if the client version satisfies the requested version - otherwise the default
				//context would fail with a FileLoadException and the provider copy is used instead
				if (File.Exists(clientCandidate)) {
					if (IsVersionSufficient(AssemblyName.GetAssemblyName(clientCandidate), assemblyName) || !File.Exists(localCandidate)) {
						return null;
					}
					return this.LoadFromAssemblyPath(localCandidate);
				}
				foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies) {
					if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)) {
						if (IsVersionSufficient(loaded.GetName(), assemblyName) || !File.Exists(localCandidate)) {
							return null;
						}
						return this.LoadFromAssemblyPath(localCandidate);
					}
				}

				if (File.Exists(localCandidate)) {
					return this.LoadFromAssemblyPath(localCandidate);
				}

				string packageCandidate;
				if (this.GetPackageAssemblies().TryGetValue(assemblyName.Name, out packageCandidate)) {
					return this.LoadFromAssemblyPath(packageCandidate);
				}

				if (!IsFrameworkAssembly(assemblyName)) {
					lock (_SyncRoot) {
						_Unresolved.Add(assemblyName.FullName);
					}
				}

				//the default context gets a last chance (framework assemblies)
				return null;
			}

			/// <summary>
			/// Returns true if the available version is equal or higher than the requested one.
			/// </summary>
			private static bool IsVersionSufficient(AssemblyName available, AssemblyName requested) {

				if (requested.Version == null || available.Version == null) {
					return true;
				}

				return available.Version >= requested.Version;
			}

			/// <summary>
			/// Returns true if the assembly is part of a shared framework (these are resolved by the default context).
			/// </summary>
			private static bool IsFrameworkAssembly(AssemblyName assemblyName) {

				string frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
				if (File.Exists(Path.Combine(frameworkDirectory, assemblyName.Name + ".dll"))) {
					return true;
				}

				string sharedRoot = Path.GetDirectoryName(Path.GetDirectoryName(frameworkDirectory));
				if (!System.IO.Directory.Exists(sharedRoot)) {
					return false;
				}

				foreach (string framework in System.IO.Directory.GetDirectories(sharedRoot)) {
					foreach (string version in System.IO.Directory.GetDirectories(framework)) {
						if (File.Exists(Path.Combine(version, assemblyName.Name + ".dll"))) {
							return true;
						}
					}
				}

				return false;
			}

			/// <summary>
			/// Returns the package assemblies (by simple name) which are listed in the '*.deps.json' of the provider folder.
			/// </summary>
			private Dictionary<string, string> GetPackageAssemblies() {

				lock (_SyncRoot) {
					if (_PackageAssemblies == null) {
						_PackageAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						foreach (string path in AssemblyInspector.GetPackageAssemblyPathsFromDepsFiles(_Directory)) {
							string name = Path.GetFileNameWithoutExtension(path);
							if (!_PackageAssemblies.ContainsKey(name)) {
								_PackageAssemblies[name] = path;
							}
						}
					}
					return _PackageAssemblies;
				}
			}

		}

	}

}
