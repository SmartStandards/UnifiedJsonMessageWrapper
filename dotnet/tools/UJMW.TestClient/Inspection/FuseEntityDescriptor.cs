namespace UJMW.TestClient.Inspection {

  /// <summary>
  /// Describes the entity which is served by a type implementing the FUSE-fx 'IRepository&lt;TEntity, TKey&gt;'
  /// contract with concrete generic arguments (used for the data mode of the test client).
  /// </summary>
  public sealed class FuseEntityDescriptor {

    private string _EntityName = string.Empty;
    private string _EntityFullName = string.Empty;
    private string _EntitySummary = string.Empty;
    private string _KeyTypeName = string.Empty;
    private string _KeyKind = _KeyKindScalar;
    private string[] _KeyFieldNames = new string[0];
    private FuseFieldDescriptor[] _Fields = new FuseFieldDescriptor[0];
    private string _EntityTemplateJson = "{}";

    /// <summary>
    /// The key is a single scalar value (e.g. int, string, Guid).
    /// </summary>
    public const string _KeyKindScalar = "Scalar";

    /// <summary>
    /// The key is a FUSE 'CompositeKeyN' object with the properties 'field1'..'fieldN'.
    /// </summary>
    public const string _KeyKindComposite = "Composite";

    /// <summary>
    /// Short name of the entity type.
    /// </summary>
    public string EntityName {
      get {
        return _EntityName;
      }
      set {
        _EntityName = value;
      }
    }

    /// <summary>
    /// Full name of the entity type.
    /// </summary>
    public string EntityFullName {
      get {
        return _EntityFullName;
      }
      set {
        _EntityFullName = value;
      }
    }

    /// <summary>
    /// XML documentation summary of the entity type.
    /// </summary>
    public string EntitySummary {
      get {
        return _EntitySummary;
      }
      set {
        _EntitySummary = value;
      }
    }

    /// <summary>
    /// Human readable name of the key type.
    /// </summary>
    public string KeyTypeName {
      get {
        return _KeyTypeName;
      }
      set {
        _KeyTypeName = value;
      }
    }

    /// <summary>
    /// 'Scalar' or 'Composite' (see constants).
    /// </summary>
    public string KeyKind {
      get {
        return _KeyKind;
      }
      set {
        _KeyKind = value;
      }
    }

    /// <summary>
    /// JSON names of the entity fields which build the key (in key order).
    /// </summary>
    public string[] KeyFieldNames {
      get {
        return _KeyFieldNames;
      }
      set {
        _KeyFieldNames = value;
      }
    }

    /// <summary>
    /// All public fields/properties of the entity.
    /// </summary>
    public FuseFieldDescriptor[] Fields {
      get {
        return _Fields;
      }
      set {
        _Fields = value;
      }
    }

    /// <summary>
    /// Empty JSON object of the entity (used for the 'create new' dialog).
    /// </summary>
    public string EntityTemplateJson {
      get {
        return _EntityTemplateJson;
      }
      set {
        _EntityTemplateJson = value;
      }
    }

  }

  /// <summary>
  /// Describes a single field of an entity served via the FUSE-fx repository contract.
  /// </summary>
  public sealed class FuseFieldDescriptor {

    private string _Name = string.Empty;
    private string _PropertyName = string.Empty;
    private string _TypeName = string.Empty;
    private string _ValueKind = _ValueKindString;
    private string _Description = string.Empty;
    private bool _IsKey = false;
    private bool _IsNullable = false;
    private string[] _EnumValues = new string[0];

    public const string _ValueKindString = "String";
    public const string _ValueKindNumber = "Number";
    public const string _ValueKindBoolean = "Boolean";
    public const string _ValueKindDate = "Date";
    public const string _ValueKindGuid = "Guid";
    public const string _ValueKindEnum = "Enum";
    public const string _ValueKindComplex = "Complex";

    /// <summary>
    /// JSON (camelCase) name of the field.
    /// </summary>
    public string Name {
      get {
        return _Name;
      }
      set {
        _Name = value;
      }
    }

    /// <summary>
    /// Original .NET property name (used for FUSE field names within filters, sorting and field dictionaries).
    /// </summary>
    public string PropertyName {
      get {
        return _PropertyName;
      }
      set {
        _PropertyName = value;
      }
    }

    /// <summary>
    /// Human readable type name.
    /// </summary>
    public string TypeName {
      get {
        return _TypeName;
      }
      set {
        _TypeName = value;
      }
    }

    /// <summary>
    /// Value category which controls editors and filter operators in the UI (see constants).
    /// </summary>
    public string ValueKind {
      get {
        return _ValueKind;
      }
      set {
        _ValueKind = value;
      }
    }

    /// <summary>
    /// XML documentation summary of the field.
    /// </summary>
    public string Description {
      get {
        return _Description;
      }
      set {
        _Description = value;
      }
    }

    /// <summary>
    /// True if the field is part of the entity key.
    /// </summary>
    public bool IsKey {
      get {
        return _IsKey;
      }
      set {
        _IsKey = value;
      }
    }

    /// <summary>
    /// True for nullable value types and reference types.
    /// </summary>
    public bool IsNullable {
      get {
        return _IsNullable;
      }
      set {
        _IsNullable = value;
      }
    }

    /// <summary>
    /// Names of the enum values (only for 'Enum').
    /// </summary>
    public string[] EnumValues {
      get {
        return _EnumValues;
      }
      set {
        _EnumValues = value;
      }
    }

  }

}
