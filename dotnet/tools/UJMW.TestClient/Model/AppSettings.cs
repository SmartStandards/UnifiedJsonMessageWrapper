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
  /// Selects which 'IUjmwManagedInfrastructure' is used ('Local', 'External' or 'Reflection').
  /// </summary>
  public sealed class InfrastructureSettings {

    public const string _ModeLocal = "Local";
    public const string _ModeExternal = "External";
    public const string _ModeReflection = "Reflection";

    private string _Mode = _ModeLocal;
    private string _ExternalUrl = string.Empty;
    private string _ExternalAuthHeader = string.Empty;
    private string _ReflectionTypeName = string.Empty;
    private bool _LegacyDataMigrated = false;

    public string Mode {
      get {
        return _Mode;
      }
      set {
        _Mode = value;
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
    /// Assembly qualified name of an implementation found in a DLL next to the client (mode 'Reflection').
    /// </summary>
    public string ReflectionTypeName {
      get {
        return _ReflectionTypeName;
      }
      set {
        _ReflectionTypeName = value;
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
