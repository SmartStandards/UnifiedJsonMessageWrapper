
namespace System.Web.UJMW {

  /// <summary>
  /// Store for infrastructure-related (NOT user-specific) information, which is required to
  /// address UJMW services: infrastructure zones, hosts with their web applications,
  /// OAuth configurations and 'wellknown' endpoints (mapping of contracts to hosted endpoints).
  /// The store can be implemented locally (see 'FileBasedUjmwManagedInfrastructure'), be provided
  /// by a remote service (consumed via UJMW) or by any custom implementation.
  /// </summary>
  public interface IUjmwManagedInfrastructure {

    /// <summary>
    /// Returns the capabilities of the store (read-only state and resolve support).
    /// </summary>
    UjmwInfrastructureCapabilities GetCapabilities();

    /// <summary>
    /// Returns the root zones of the zone (area) tree.
    /// </summary>
    UjmwInfrastructureZone[] GetZones();

    /// <summary>
    /// Replaces the complete zone (area) tree (only supported if not read-only).
    /// </summary>
    void SaveZones(UjmwInfrastructureZone[] rootZones);

    /// <summary>
    /// Returns all known hosts including their web applications.
    /// </summary>
    UjmwInfrastructureHost[] GetHosts();

    /// <summary>
    /// Replaces all known hosts including their web applications (only supported if not read-only).
    /// </summary>
    void SaveHosts(UjmwInfrastructureHost[] hosts);

    /// <summary>
    /// Returns all known OAuth configurations.
    /// </summary>
    UjmwOAuthConfiguration[] GetOAuthConfigurations();

    /// <summary>
    /// Replaces all known OAuth configurations (only supported if not read-only).
    /// </summary>
    void SaveOAuthConfigurations(UjmwOAuthConfiguration[] configurations);

    /// <summary>
    /// Returns the wellknown endpoints of a contract.
    /// </summary>
    /// <param name="contractName">The full qualified type name of the contract.</param>
    UjmwWellknownEndpoint[] GetWellknownEndpoints(string contractName);

    /// <summary>
    /// Creates or updates a wellknown endpoint (identified by contract name, host url and web application name).
    /// Only supported if not read-only.
    /// </summary>
    void SaveWellknownEndpoint(UjmwWellknownEndpoint endpoint);

    /// <summary>
    /// Deletes a wellknown endpoint (only supported if not read-only).
    /// </summary>
    void DeleteWellknownEndpoint(string contractName, string hostUrl, string webApplicationName);

    /// <summary>
    /// Resolves the URLs of a contract within the given zone (only supported if 'CanResolve' is true).
    /// </summary>
    /// <param name="contractName">The full qualified type name of the contract.</param>
    /// <param name="zone">The name (or path) of the infrastructure zone.</param>
    string[] Resolve(string contractName, string zone);

  }

  /// <summary>
  /// Capabilities of an 'IUjmwManagedInfrastructure'.
  /// </summary>
  public class UjmwInfrastructureCapabilities {

    private bool _IsReadOnly = false;
    private bool _CanResolve = false;

    /// <summary>
    /// If true, all 'Save...' and 'Delete...' methods are not supported.
    /// </summary>
    public bool IsReadOnly {
      get {
        return _IsReadOnly;
      }
      set {
        _IsReadOnly = value;
      }
    }

    /// <summary>
    /// If true, the 'Resolve' method is supported.
    /// </summary>
    public bool CanResolve {
      get {
        return _CanResolve;
      }
      set {
        _CanResolve = value;
      }
    }

  }

  /// <summary>
  /// A node of the infrastructure zone (area) tree.
  /// </summary>
  public class UjmwInfrastructureZone {

    private string _Name = string.Empty;
    private string _Description = string.Empty;
    private UjmwInfrastructureZone[] _ChildZones = new UjmwInfrastructureZone[0];

    public string Name {
      get {
        return _Name;
      }
      set {
        _Name = value;
      }
    }

    public string Description {
      get {
        return _Description;
      }
      set {
        _Description = value;
      }
    }

    public UjmwInfrastructureZone[] ChildZones {
      get {
        return _ChildZones;
      }
      set {
        _ChildZones = value;
      }
    }

  }

  /// <summary>
  /// A host (pure server url like 'https://server01:8443') and its web applications.
  /// </summary>
  public class UjmwInfrastructureHost {

    private string _HostUrl = string.Empty;
    private string _Description = string.Empty;
    private string _Zone = string.Empty;
    private UjmwWebApplication[] _WebApplications = new UjmwWebApplication[0];

    public string HostUrl {
      get {
        return _HostUrl;
      }
      set {
        _HostUrl = value;
      }
    }

    /// <summary>
    /// The zone (full area path joined by '/', e.g. 'Production/EU') where the host is located (optional).
    /// </summary>
    public string Zone {
      get {
        return _Zone;
      }
      set {
        _Zone = value;
      }
    }

    public string Description {
      get {
        return _Description;
      }
      set {
        _Description = value;
      }
    }

    public UjmwWebApplication[] WebApplications {
      get {
        return _WebApplications;
      }
      set {
        _WebApplications = value;
      }
    }

  }

  /// <summary>
  /// A web application hosted on an 'UjmwInfrastructureHost'.
  /// </summary>
  public class UjmwWebApplication {

    private string _Name = string.Empty;
    private string _ApplicationBaseUrl = string.Empty;

    public string Name {
      get {
        return _Name;
      }
      set {
        _Name = value;
      }
    }

    /// <summary>
    /// Relative base url of the application on its host (e.g. 'myApp/api').
    /// </summary>
    public string ApplicationBaseUrl {
      get {
        return _ApplicationBaseUrl;
      }
      set {
        _ApplicationBaseUrl = value;
      }
    }

  }

  /// <summary>
  /// An OAuth (authorization code flow) configuration.
  /// </summary>
  public class UjmwOAuthConfiguration {

    private string _Name = string.Empty;
    private string _AuthorizeUrl = string.Empty;
    private string _TokenUrl = string.Empty;
    private string _ClientId = string.Empty;
    private string _ClientSecret = string.Empty;
    private string _RedirectUri = string.Empty;
    private string _Scope = string.Empty;
    private bool _UsePkce = true;

    public string Name {
      get {
        return _Name;
      }
      set {
        _Name = value;
      }
    }

    public string AuthorizeUrl {
      get {
        return _AuthorizeUrl;
      }
      set {
        _AuthorizeUrl = value;
      }
    }

    public string TokenUrl {
      get {
        return _TokenUrl;
      }
      set {
        _TokenUrl = value;
      }
    }

    public string ClientId {
      get {
        return _ClientId;
      }
      set {
        _ClientId = value;
      }
    }

    public string ClientSecret {
      get {
        return _ClientSecret;
      }
      set {
        _ClientSecret = value;
      }
    }

    public string RedirectUri {
      get {
        return _RedirectUri;
      }
      set {
        _RedirectUri = value;
      }
    }

    public string Scope {
      get {
        return _Scope;
      }
      set {
        _Scope = value;
      }
    }

    public bool UsePkce {
      get {
        return _UsePkce;
      }
      set {
        _UsePkce = value;
      }
    }

  }

  /// <summary>
  /// Maps a contract to an endpoint which is available on a known host + web application.
  /// The resulting url is '&lt;HostUrl&gt;/&lt;ApplicationBaseUrl&gt;/&lt;EndpointUrl&gt;'.
  /// </summary>
  public class UjmwWellknownEndpoint {

    private string _ContractName = string.Empty;
    private string _HostUrl = string.Empty;
    private string _WebApplicationName = string.Empty;
    private string _EndpointUrl = string.Empty;

    /// <summary>
    /// The full qualified type name of the contract.
    /// </summary>
    public string ContractName {
      get {
        return _ContractName;
      }
      set {
        _ContractName = value;
      }
    }

    public string HostUrl {
      get {
        return _HostUrl;
      }
      set {
        _HostUrl = value;
      }
    }

    public string WebApplicationName {
      get {
        return _WebApplicationName;
      }
      set {
        _WebApplicationName = value;
      }
    }

    /// <summary>
    /// Contract-specific relative endpoint url (maintained exclusively here).
    /// </summary>
    public string EndpointUrl {
      get {
        return _EndpointUrl;
      }
      set {
        _EndpointUrl = value;
      }
    }

  }

}
