
namespace UJMW.TestClient.Model {

  /// <summary>
  /// A persisted request setup. Every '/' within the name represents a virtual folder level.
  /// </summary>
  public sealed class Preset {

    private string _Name = string.Empty;
    private string _TypeFullName = string.Empty;
    private string _Url = string.Empty;
    private string _AuthHeader = string.Empty;
    private string _SideChannel = string.Empty;
    private string _MethodName = string.Empty;
    private string _RequestJson = string.Empty;

    public string Name {
      get {
        return _Name;
      }
      set {
        _Name = value;
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

    public string Url {
      get {
        return _Url;
      }
      set {
        _Url = value;
      }
    }

    public string AuthHeader {
      get {
        return _AuthHeader;
      }
      set {
        _AuthHeader = value;
      }
    }

    public string SideChannel {
      get {
        return _SideChannel;
      }
      set {
        _SideChannel = value;
      }
    }

    public string MethodName {
      get {
        return _MethodName;
      }
      set {
        _MethodName = value;
      }
    }

    public string RequestJson {
      get {
        return _RequestJson;
      }
      set {
        _RequestJson = value;
      }
    }

  }

  /// <summary>
  /// A named template for the content of the SideChannel text box.
  /// </summary>
  public sealed class SideChannelTemplate {

    private string _Name = string.Empty;
    private string _Content = string.Empty;

    public string Name {
      get {
        return _Name;
      }
      set {
        _Name = value;
      }
    }

    public string Content {
      get {
        return _Content;
      }
      set {
        _Content = value;
      }
    }

  }

  /// <summary>
  /// An infrastructure zone which can be used to resolve service URLs via an ApiCatalog service.
  /// </summary>
  public sealed class InfrastructureZone {

    private string _Name = string.Empty;
    private string _ApiCatalogUrl = string.Empty;
    private string _Description = string.Empty;

    public string Name {
      get {
        return _Name;
      }
      set {
        _Name = value;
      }
    }

    public string ApiCatalogUrl {
      get {
        return _ApiCatalogUrl;
      }
      set {
        _ApiCatalogUrl = value;
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

  }

}
