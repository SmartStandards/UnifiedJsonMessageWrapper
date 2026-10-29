using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace System.Web.UJMW {

  /// <summary>
  /// Default implementation of 'IUjmwManagedInfrastructure' which persists all information
  /// within a JSON file (by default in the roaming profile of the user, but it can also be located on a network share). It is editable and does not support 'Resolve'.
  /// </summary>
  public class FileBasedUjmwManagedInfrastructure : IUjmwManagedInfrastructure {

    /// <summary>
    /// Persisted content of the file based store.
    /// </summary>
    public class StoreContent {

      private UjmwInfrastructureZone[] _Zones = new UjmwInfrastructureZone[0];
      private UjmwInfrastructureHost[] _Hosts = new UjmwInfrastructureHost[0];
      private UjmwOAuthConfiguration[] _OAuthConfigurations = new UjmwOAuthConfiguration[0];
      private UjmwWellknownEndpoint[] _WellknownEndpoints = new UjmwWellknownEndpoint[0];

      public UjmwInfrastructureZone[] Zones {
        get {
          return _Zones;
        }
        set {
          _Zones = value;
        }
      }

      public UjmwInfrastructureHost[] Hosts {
        get {
          return _Hosts;
        }
        set {
          _Hosts = value;
        }
      }

      public UjmwOAuthConfiguration[] OAuthConfigurations {
        get {
          return _OAuthConfigurations;
        }
        set {
          _OAuthConfigurations = value;
        }
      }

      public UjmwWellknownEndpoint[] WellknownEndpoints {
        get {
          return _WellknownEndpoints;
        }
        set {
          _WellknownEndpoints = value;
        }
      }

    }

    private readonly string _FilePath;
    private readonly object _SyncRoot = new object();

    /// <summary>
    /// Creates a store using the default file '%APPDATA%\UJMW\ManagedInfrastructure.json'.
    /// </summary>
    public FileBasedUjmwManagedInfrastructure() : this(GetDefaultFilePath()) {
    }

    /// <summary>
    /// Creates a store using the given file (which will be created on first save).
    /// </summary>
    public FileBasedUjmwManagedInfrastructure(string filePath) {
      if (string.IsNullOrWhiteSpace(filePath)) {
        throw new ArgumentException("The file path must not be empty.", nameof(filePath));
      }
      _FilePath = filePath;
    }

    /// <summary>
    /// The JSON file which is used for persistence.
    /// </summary>
    public string FilePath {
      get {
        return _FilePath;
      }
    }

    /// <summary>
    /// Returns '%APPDATA%\UJMW\ManagedInfrastructure.json'.
    /// </summary>
    public static string GetDefaultFilePath() {
      string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
      return Path.Combine(appData, "UJMW", "ManagedInfrastructure.json");
    }

    /// <summary>
    /// The file based store is editable but can not resolve urls.
    /// </summary>
    public UjmwInfrastructureCapabilities GetCapabilities() {
      UjmwInfrastructureCapabilities capabilities = new UjmwInfrastructureCapabilities();
      capabilities.IsReadOnly = false;
      capabilities.CanResolve = false;
      return capabilities;
    }

    /// <summary>
    /// Returns the root zones of the zone tree.
    /// </summary>
    public UjmwInfrastructureZone[] GetZones() {
      return this.Load().Zones;
    }

    /// <summary>
    /// Replaces the zone tree.
    /// </summary>
    public void SaveZones(UjmwInfrastructureZone[] rootZones) {
      this.Update((content) => {
        content.Zones = NormalizeZones(rootZones);
      });
    }

    /// <summary>
    /// Returns all hosts.
    /// </summary>
    public UjmwInfrastructureHost[] GetHosts() {
      return this.Load().Hosts;
    }

    /// <summary>
    /// Replaces all hosts.
    /// </summary>
    public void SaveHosts(UjmwInfrastructureHost[] hosts) {
      this.Update((content) => {
        if (hosts == null) {
          hosts = new UjmwInfrastructureHost[0];
        }
        content.Hosts = hosts.Where((h) => h != null && !string.IsNullOrWhiteSpace(h.HostUrl)).ToArray();
        foreach (UjmwInfrastructureHost host in content.Hosts) {
          host.HostUrl = host.HostUrl.Trim().TrimEnd('/');
          if (host.Zone == null) {
            host.Zone = string.Empty;
          }
          host.Zone = host.Zone.Trim().Trim('/');
          if (host.WebApplications == null) {
            host.WebApplications = new UjmwWebApplication[0];
          }
          host.WebApplications = host.WebApplications.Where((a) => a != null && !string.IsNullOrWhiteSpace(a.Name)).ToArray();
        }
      });
    }

    /// <summary>
    /// Returns all OAuth configurations.
    /// </summary>
    public UjmwOAuthConfiguration[] GetOAuthConfigurations() {
      return this.Load().OAuthConfigurations;
    }

    /// <summary>
    /// Replaces all OAuth configurations.
    /// </summary>
    public void SaveOAuthConfigurations(UjmwOAuthConfiguration[] configurations) {
      this.Update((content) => {
        if (configurations == null) {
          configurations = new UjmwOAuthConfiguration[0];
        }
        content.OAuthConfigurations = configurations.Where((c) => c != null && !string.IsNullOrWhiteSpace(c.Name)).ToArray();
      });
    }

    /// <summary>
    /// Returns the wellknown endpoints of a contract.
    /// </summary>
    public UjmwWellknownEndpoint[] GetWellknownEndpoints(string contractName) {
      return this.Load().WellknownEndpoints.Where((e) => e.ContractName == contractName).ToArray();
    }

    /// <summary>
    /// Creates or updates a wellknown endpoint.
    /// </summary>
    public void SaveWellknownEndpoint(UjmwWellknownEndpoint endpoint) {
      if (endpoint == null || string.IsNullOrWhiteSpace(endpoint.ContractName) || string.IsNullOrWhiteSpace(endpoint.HostUrl)) {
        throw new ArgumentException("Contract name and host url are required.", nameof(endpoint));
      }
      this.Update((content) => {
        List<UjmwWellknownEndpoint> endpoints = content.WellknownEndpoints.Where(
          (e) => !IsSameEndpoint(e, endpoint.ContractName, endpoint.HostUrl, endpoint.WebApplicationName)
        ).ToList();
        endpoints.Add(endpoint);
        content.WellknownEndpoints = endpoints.ToArray();
      });
    }

    /// <summary>
    /// Deletes a wellknown endpoint.
    /// </summary>
    public void DeleteWellknownEndpoint(string contractName, string hostUrl, string webApplicationName) {
      this.Update((content) => {
        content.WellknownEndpoints = content.WellknownEndpoints.Where(
          (e) => !IsSameEndpoint(e, contractName, hostUrl, webApplicationName)
        ).ToArray();
      });
    }

    /// <summary>
    /// Not supported by the file based store.
    /// </summary>
    public string[] Resolve(string contractName, string zone) {
      throw new NotSupportedException("The file based infrastructure store does not support url resolution.");
    }

    /// <summary>
    /// Compares the identifying fields of a wellknown endpoint.
    /// </summary>
    private static bool IsSameEndpoint(UjmwWellknownEndpoint endpoint, string contractName, string hostUrl, string webApplicationName) {
      if (endpoint.ContractName != contractName) {
        return false;
      }
      if (!string.Equals(NormalizeText(endpoint.HostUrl).TrimEnd('/'), NormalizeText(hostUrl).TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
      return string.Equals(NormalizeText(endpoint.WebApplicationName), NormalizeText(webApplicationName), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a trimmed, non-null string.
    /// </summary>
    private static string NormalizeText(string text) {
      if (text == null) {
        return string.Empty;
      }
      return text.Trim();
    }

    /// <summary>
    /// Removes empty zone names (recursive) and replaces null arrays.
    /// </summary>
    private static UjmwInfrastructureZone[] NormalizeZones(UjmwInfrastructureZone[] zones) {
      if (zones == null) {
        return new UjmwInfrastructureZone[0];
      }
      UjmwInfrastructureZone[] validZones = zones.Where((z) => z != null && !string.IsNullOrWhiteSpace(z.Name)).ToArray();
      foreach (UjmwInfrastructureZone zone in validZones) {
        zone.Name = zone.Name.Trim();
        zone.ChildZones = NormalizeZones(zone.ChildZones);
      }
      return validZones;
    }

    /// <summary>
    /// Loads the store content (an empty content if the file does not exist).
    /// </summary>
    private StoreContent Load() {
      lock (_SyncRoot) {
        if (!File.Exists(_FilePath)) {
          return new StoreContent();
        }
        string json = File.ReadAllText(_FilePath);
        StoreContent content = JsonConvert.DeserializeObject<StoreContent>(json);
        if (content == null) {
          content = new StoreContent();
        }
        if (content.Zones == null) {
          content.Zones = new UjmwInfrastructureZone[0];
        }
        if (content.Hosts == null) {
          content.Hosts = new UjmwInfrastructureHost[0];
        }
        if (content.OAuthConfigurations == null) {
          content.OAuthConfigurations = new UjmwOAuthConfiguration[0];
        }
        if (content.WellknownEndpoints == null) {
          content.WellknownEndpoints = new UjmwWellknownEndpoint[0];
        }
        return content;
      }
    }

    /// <summary>
    /// Loads, modifies and saves the store content.
    /// </summary>
    private void Update(Action<StoreContent> modification) {
      lock (_SyncRoot) {
        StoreContent content = this.Load();
        modification.Invoke(content);
        string directory = Path.GetDirectoryName(_FilePath);
        if (!string.IsNullOrEmpty(directory)) {
          Directory.CreateDirectory(directory);
        }
        File.WriteAllText(_FilePath, JsonConvert.SerializeObject(content, Formatting.Indented));
      }
    }

  }

}
