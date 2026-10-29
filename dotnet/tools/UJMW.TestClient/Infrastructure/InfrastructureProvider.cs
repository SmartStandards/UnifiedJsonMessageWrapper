using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Web.UJMW;
using UJMW.TestClient.Model;

namespace UJMW.TestClient.Infrastructure {

	/// <summary>
	/// Creates the 'IUjmwManagedInfrastructure' which is selected in the settings:
	/// 'Local' (JSON file per user), 'External' (UJMW client to a remote store)
	/// or 'Provider' (an implementation which was registered from a DLL added to the client).
	/// </summary>
	internal static class InfrastructureProvider {

		private static readonly Dictionary<string, ProviderLoadContext> _LoadContexts = new Dictionary<string, ProviderLoadContext>(StringComparer.OrdinalIgnoreCase);
		private static readonly object _LoadContextsLock = new object();

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
			if (settings.Mode == InfrastructureSettings._ModeProvider) {
				RegisteredProvider provider = settings.RegisteredProviders.FirstOrDefault(
					(p) => p.TypeFullName == settings.ProviderTypeName
				);
				if (provider == null) {
					throw new InvalidOperationException($"The store provider '{settings.ProviderTypeName}' is not registered.");
				}
				return CreateFromRegisteredProvider(provider);
			}
			return new LocalUjmwManagedInfrastructure(LocalStoreFilePath);
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
			Assembly assembly = context.LoadFromAssemblyPath(Path.GetFullPath(provider.DllPath));
			Type type = assembly.GetType(provider.TypeFullName, false);
			if (type == null || !IsUsableImplementation(type)) {
				throw new InvalidOperationException(
					$"The store provider '{provider.TypeFullName}' is not available in '{provider.DllPath}' (public class with parameterless constructor implementing IUjmwManagedInfrastructure expected)."
				);
			}
			return (IUjmwManagedInfrastructure)Activator.CreateInstance(type);
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
		/// all others are loaded from the folder of the provider DLL.
		/// </summary>
		private sealed class ProviderLoadContext : AssemblyLoadContext {

			private readonly string _Directory;

			public ProviderLoadContext(string dllPath) : base("UJMW.TestClient.Provider:" + dllPath, false) {
				_Directory = Path.GetDirectoryName(dllPath);
			}

			/// <summary>
			/// Resolves dependencies of the provider DLL.
			/// </summary>
			protected override Assembly Load(AssemblyName assemblyName) {
				string clientCandidate = Path.Combine(AppContext.BaseDirectory, assemblyName.Name + ".dll");
				if (File.Exists(clientCandidate)) {
					//shared with the client -> fall back to the default context
					return null;
				}
				foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies) {
					if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)) {
						return null;
					}
				}
				string localCandidate = Path.Combine(_Directory, assemblyName.Name + ".dll");
				if (File.Exists(localCandidate)) {
					return this.LoadFromAssemblyPath(localCandidate);
				}
				return null;
			}

		}

	}

}
