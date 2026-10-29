
namespace UJMW.TestClient.Inspection {

  /// <summary>
  /// A namespace of an inspected assembly including its public types.
  /// </summary>
  public sealed class NamespaceDescriptor {

    private string _Name = string.Empty;
    private TypeDescriptor[] _Types = new TypeDescriptor[0];

    public string Name {
      get {
        return _Name;
      }
      set {
        _Name = value;
      }
    }

    public TypeDescriptor[] Types {
      get {
        return _Types;
      }
      set {
        _Types = value;
      }
    }

  }

  /// <summary>
  /// A public type of an inspected assembly.
  /// </summary>
  public sealed class TypeDescriptor {

    private string _FullName = string.Empty;
    private string _DisplayName = string.Empty;
    private string _Kind = string.Empty;

    public string FullName {
      get {
        return _FullName;
      }
      set {
        _FullName = value;
      }
    }

    public string DisplayName {
      get {
        return _DisplayName;
      }
      set {
        _DisplayName = value;
      }
    }

    /// <summary>
    /// One of the 'AssemblyInspector._Kind...' constants.
    /// </summary>
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
  /// A method (UJMW operation) of a contract type including a prepared request template.
  /// </summary>
  public sealed class MethodDescriptor {

    private string _Name = string.Empty;
    private string _Signature = string.Empty;
    private string _RequestTemplate = string.Empty;

    public string Name {
      get {
        return _Name;
      }
      set {
        _Name = value;
      }
    }

    public string Signature {
      get {
        return _Signature;
      }
      set {
        _Signature = value;
      }
    }

    public string RequestTemplate {
      get {
        return _RequestTemplate;
      }
      set {
        _RequestTemplate = value;
      }
    }

  }

}
