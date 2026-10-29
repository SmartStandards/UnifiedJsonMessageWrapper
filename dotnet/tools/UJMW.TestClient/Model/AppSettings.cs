using System;

namespace UJMW.TestClient.Model {

  /// <summary>
  /// Root of all settings which are persisted per user.
  /// </summary>
  public sealed class AppSettings {

    private DllEntry[] _Dlls = new DllEntry[0];
    private UrlHistoryEntry[] _UrlHistory = new UrlHistoryEntry[0];
    private Preset[] _Presets = new Preset[0];
    private SideChannelTemplate[] _SideChannelTemplates = new SideChannelTemplate[0];
    private InfrastructureZone[] _Zones = new InfrastructureZone[0];
    private JwtGeneratorSettings _Jwt = new JwtGeneratorSettings();
    private OAuthSettings _OAuth = new OAuthSettings();
    private string _SideChannelText = "{\r\n}";
    private int _LeftPanelWidth = 380;
    private int _RequestTimeoutSeconds = 5;
    private string _CurrentZone = string.Empty;
    private InfrastructureSettings _Infrastructure = new InfrastructureSettings();
    private PinnedEndpoint[] _PinnedEndpoints = new PinnedEndpoint[0];

    /// <summary>
    /// User-specific pinned endpoint URLs (not stored in the managed infrastructure).
    /// </summary>
    public PinnedEndpoint[] PinnedEndpoints {
      get {
        return _PinnedEndpoints;
      }
      set {
        _PinnedEndpoints = value;
      }
    }

    /// <summary>
    /// Timeout for UJMW invocations (default: 5 seconds).
    /// </summary>
    public int RequestTimeoutSeconds {
      get {
        return _RequestTimeoutSeconds;
      }
      set {
        _RequestTimeoutSeconds = value;
      }
    }

    /// <summary>
    /// Last selected (or manually entered) infrastructure zone, used for the {zone} placeholder and for resolving.
    /// </summary>
    public string CurrentZone {
      get {
        return _CurrentZone;
      }
      set {
        _CurrentZone = value;
      }
    }

    /// <summary>
    /// Selection of the 'IUjmwManagedInfrastructure' source.
    /// </summary>
    public InfrastructureSettings Infrastructure {
      get {
        return _Infrastructure;
      }
      set {
        _Infrastructure = value;
      }
    }

    public DllEntry[] Dlls {
      get {
        return _Dlls;
      }
      set {
        _Dlls = value;
      }
    }

    public UrlHistoryEntry[] UrlHistory {
      get {
        return _UrlHistory;
      }
      set {
        _UrlHistory = value;
      }
    }

    public Preset[] Presets {
      get {
        return _Presets;
      }
      set {
        _Presets = value;
      }
    }

    public SideChannelTemplate[] SideChannelTemplates {
      get {
        return _SideChannelTemplates;
      }
      set {
        _SideChannelTemplates = value;
      }
    }

    public InfrastructureZone[] Zones {
      get {
        return _Zones;
      }
      set {
        _Zones = value;
      }
    }

    public JwtGeneratorSettings Jwt {
      get {
        return _Jwt;
      }
      set {
        _Jwt = value;
      }
    }

    public OAuthSettings OAuth {
      get {
        return _OAuth;
      }
      set {
        _OAuth = value;
      }
    }

    /// <summary>
    /// Last content of the SideChannel text box.
    /// </summary>
    public string SideChannelText {
      get {
        return _SideChannelText;
      }
      set {
        _SideChannelText = value;
      }
    }

    public int LeftPanelWidth {
      get {
        return _LeftPanelWidth;
      }
      set {
        _LeftPanelWidth = value;
      }
    }

  }

  /// <summary>
  /// Selects which 'IUjmwManagedInfrastructure' is used ('FileBased', 'External' or 'Provider').
  /// </summary>
  public sealed class InfrastructureSettings {

    public const string _ModeFileBased = "FileBased";
    public const string _ModeExternal = "External";
    public const string _ModeProvider = "Provider";

    /// <summary>
    /// Former name of the file based mode (migrated on load).
    /// </summary>
    public const string _ModeLegacyLocal = "Local";

    private string _Mode = _ModeFileBased;
    private string _FileBasedStorePath = string.Empty;
    private string _ExternalUrl = string.Empty;
    private string _ExternalAuthHeader = string.Empty;
    private string _ProviderTypeName = string.Empty;
    private RegisteredProvider[] _RegisteredProviders = new RegisteredProvider[0];
    private bool _LegacyDataMigrated = false;

    /// <summary>
    /// Store providers which were registered from types of added DLLs (stay available when the DLL is removed from the list).
    /// </summary>
    public RegisteredProvider[] RegisteredProviders {
      get {
        return _RegisteredProviders;
      }
      set {
        _RegisteredProviders = value;
      }
    }

    public string Mode {
      get {
        return _Mode;
      }
      set {
        _Mode = value;
      }
    }

    /// <summary>
    /// JSON file of the file based store (mode 'FileBased'). Empty means the default file in the
    /// roaming profile of the user ('%APPDATA%\UJMW.TestClient\infrastructure.json'); it can be
    /// changed to a network share to share the infrastructure within a team.
    /// </summary>
    public string FileBasedStorePath {
      get {
        return _FileBasedStorePath;
      }
      set {
        _FileBasedStorePath = value;
      }
    }

    /// <summary>
    /// Base url of the externally managed UJMW service (mode 'External').
    /// </summary>
    public string ExternalUrl {
      get {
        return _ExternalUrl;
      }
      set {
        _ExternalUrl = value;
      }
    }

    /// <summary>
    /// Authorization header (or plain token) for the externally managed service (mode 'External').
    /// </summary>
    public string ExternalAuthHeader {
      get {
        return _ExternalAuthHeader;
      }
      set {
        _ExternalAuthHeader = value;
      }
    }

    /// <summary>
    /// Full name of the selected registered provider type (mode 'Provider').
    /// </summary>
    public string ProviderTypeName {
      get {
        return _ProviderTypeName;
      }
      set {
        _ProviderTypeName = value;
      }
    }

    /// <summary>
    /// True when the former zones/OAuth settings of the client were moved into the local infrastructure store.
    /// </summary>
    public bool LegacyDataMigrated {
      get {
        return _LegacyDataMigrated;
      }
      set {
        _LegacyDataMigrated = value;
      }
    }

  }

  /// <summary>
  /// An 'IUjmwManagedInfrastructure' implementation which was registered as store provider.
  /// </summary>
  public sealed class RegisteredProvider {

    private string _DllPath = string.Empty;
    private string _TypeFullName = string.Empty;

    public string DllPath {
      get {
        return _DllPath;
      }
      set {
        _DllPath = value;
      }
    }

    public string TypeFullName {
      get {
        return _TypeFullName;
      }
      set {
        _TypeFullName = value;
      }
    }

  }

  /// <summary>
  /// A user-specific pinned endpoint URL of a contract.
  /// </summary>
  public sealed class PinnedEndpoint {

    private string _TypeFullName = string.Empty;
    private string _Url = string.Empty;
    private string _HostUrl = string.Empty;
    private string _WebApplicationName = string.Empty;

    public string TypeFullName {
      get {
        return _TypeFullName;
      }
      set {
        _TypeFullName = value;
      }
    }

    public string Url {
      get {
        return _Url;
      }
      set {
        _Url = value;
      }
    }

    /// <summary>
    /// Host the pin was created for (needed to promote it into the store).
    /// </summary>
    public string HostUrl {
      get {
        return _HostUrl;
      }
      set {
        _HostUrl = value;
      }
    }

    /// <summary>
    /// Web application the pin was created for (needed to promote it into the store).
    /// </summary>
    public string WebApplicationName {
      get {
        return _WebApplicationName;
      }
      set {
        _WebApplicationName = value;
      }
    }

  }

  /// <summary>
  /// A DLL registered in the main tree including the types which were checked by the user.
  /// </summary>
  public sealed class DllEntry {

    private string _Path = string.Empty;
    private CheckedType[] _CheckedTypes = new CheckedType[0];

    public string Path {
      get {
        return _Path;
      }
      set {
        _Path = value;
      }
    }

    public CheckedType[] CheckedTypes {
      get {
        return _CheckedTypes;
      }
      set {
        _CheckedTypes = value;
      }
    }

  }

  /// <summary>
  /// A type which was checked in the type selection of a DLL.
  /// The kind is persisted as well, so that the node can be rendered when the DLL is missing.
  /// </summary>
  public sealed class CheckedType {

    private string _FullName = string.Empty;
    private string _Kind = string.Empty;

    public string FullName {
      get {
        return _FullName;
      }
      set {
        _FullName = value;
      }
    }

    public string Kind {
      get {
        return _Kind;
      }
      set {
        _Kind = value;
      }
    }

  }

  /// <summary>
  /// One entry of the URL history (which is maintained per full qualified type name).
  /// </summary>
  public sealed class UrlHistoryEntry {

    private string _TypeFullName = string.Empty;
    private string _Url = string.Empty;
    private DateTime _LastUsedUtc = DateTime.MinValue;

    public string TypeFullName {
      get {
        return _TypeFullName;
      }
      set {
        _TypeFullName = value;
      }
    }

    public string Url {
      get {
        return _Url;
      }
      set {
        _Url = value;
      }
    }

    public DateTime LastUsedUtc {
      get {
        return _LastUsedUtc;
      }
      set {
        _LastUsedUtc = value;
      }
    }

  }

}
