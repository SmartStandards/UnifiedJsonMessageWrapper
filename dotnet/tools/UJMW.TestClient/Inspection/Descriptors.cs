
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
    private string _Summary = string.Empty;
    private string _ReturnTypeName = string.Empty;
    private string _ReturnsDescription = string.Empty;
    private ParameterDescriptor[] _Parameters = new ParameterDescriptor[0];

    /// <summary>
    /// XML documentation summary of the method.
    /// </summary>
    public string Summary {
      get {
        return _Summary;
      }
      set {
        _Summary = value;
      }
    }

    public string ReturnTypeName {
      get {
        return _ReturnTypeName;
      }
      set {
        _ReturnTypeName = value;
      }
    }

    /// <summary>
    /// XML documentation of the return value.
    /// </summary>
    public string ReturnsDescription {
      get {
        return _ReturnsDescription;
      }
      set {
        _ReturnsDescription = value;
      }
    }

    public ParameterDescriptor[] Parameters {
      get {
        return _Parameters;
      }
      set {
        _Parameters = value;
      }
    }

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

  /// <summary>
  /// A parameter of a method including its XML documentation.
  /// </summary>
  public sealed class ParameterDescriptor {

    public const string _DirectionIn = "in";
    public const string _DirectionRef = "ref";
    public const string _DirectionOut = "out";

    private string _Name = string.Empty;
    private string _TypeName = string.Empty;
    private string _Direction = _DirectionIn;
    private bool _IsOptional = false;
    private string _Description = string.Empty;

    public string Name {
      get {
        return _Name;
      }
      set {
        _Name = value;
      }
    }

    public string TypeName {
      get {
        return _TypeName;
      }
      set {
        _TypeName = value;
      }
    }

    /// <summary>
    /// 'in', 'ref' or 'out' (out parameters are part of the response only).
    /// </summary>
    public string Direction {
      get {
        return _Direction;
      }
      set {
        _Direction = value;
      }
    }

    public bool IsOptional {
      get {
        return _IsOptional;
      }
      set {
        _IsOptional = value;
      }
    }

    /// <summary>
    /// XML documentation of the parameter.
    /// </summary>
    public string Description {
      get {
        return _Description;
      }
      set {
        _Description = value;
      }
    }

  }

  /// <summary>
  /// Methods and documentation of a contract type.
  /// </summary>
  public sealed class ContractDescriptor {

    private string _Summary = string.Empty;
    private bool _HasDocumentationFile = false;
    private MethodDescriptor[] _Methods = new MethodDescriptor[0];

    /// <summary>
    /// XML documentation summary of the type.
    /// </summary>
    public string Summary {
      get {
        return _Summary;
      }
      set {
        _Summary = value;
      }
    }

    /// <summary>
    /// True if an XML documentation file was found next to the assembly.
    /// </summary>
    public bool HasDocumentationFile {
      get {
        return _HasDocumentationFile;
      }
      set {
        _HasDocumentationFile = value;
      }
    }

    public MethodDescriptor[] Methods {
      get {
        return _Methods;
      }
      set {
        _Methods = value;
      }
    }

  }

}
