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
