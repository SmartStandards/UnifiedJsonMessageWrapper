
namespace UJMW.TestClient.Model {

  /// <summary>
  /// Persisted inputs of the JWT generator (including the sign key).
  /// </summary>
  public sealed class JwtGeneratorSettings {

    private string _Algorithm = "HS256";
    private string _SignKey = string.Empty;
    private bool _SignKeyIsBase64 = false;
    private string _PayloadJson = "{\r\n  \"sub\": \"testuser\",\r\n  \"iss\": \"ujmw-testclient\",\r\n  \"aud\": \"\",\r\n  \"scope\": \"\"\r\n}";
    private int _LifetimeMinutes = 60;
    private bool _AddTimestamps = true;

    /// <summary>
    /// Name of a jose-jwt 'JwsAlgorithm' (e.g. HS256, RS256, ES256, none).
    /// </summary>
    public string Algorithm {
      get {
        return _Algorithm;
      }
      set {
        _Algorithm = value;
      }
    }

    /// <summary>
    /// Shared secret (HS*) or PEM encoded private key (RS*, PS*, ES*).
    /// </summary>
    public string SignKey {
      get {
        return _SignKey;
      }
      set {
        _SignKey = value;
      }
    }

    public bool SignKeyIsBase64 {
      get {
        return _SignKeyIsBase64;
      }
      set {
        _SignKeyIsBase64 = value;
      }
    }

    public string PayloadJson {
      get {
        return _PayloadJson;
      }
      set {
        _PayloadJson = value;
      }
    }

    public int LifetimeMinutes {
      get {
        return _LifetimeMinutes;
      }
      set {
        _LifetimeMinutes = value;
      }
    }

    /// <summary>
    /// If true, 'iat', 'nbf' and 'exp' are added/overwritten automatically.
    /// </summary>
    public bool AddTimestamps {
      get {
        return _AddTimestamps;
      }
      set {
        _AddTimestamps = value;
      }
    }

  }

  /// <summary>
  /// Persisted inputs of the OAuth authorization code flow requestor.
  /// </summary>
  public sealed class OAuthSettings {

    private string _AuthorizeUrl = string.Empty;
    private string _TokenUrl = string.Empty;
    private string _ClientId = string.Empty;
    private string _ClientSecret = string.Empty;
    private string _RedirectUri = "http://localhost/ujmw-testclient/callback";
    private string _Scope = "openid";
    private bool _UsePkce = true;

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

}
