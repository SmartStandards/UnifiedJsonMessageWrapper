using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;

namespace UJMW.TestClient.Inspection {

  /// <summary>
  /// Detects whether a contract type serves a concrete entity via the FUSE-fx repository contract
  /// ('System.Data.Fuse.IRepository&lt;TEntity, TKey&gt;' with closed generic arguments).
  /// NOTE: the FUSE-fx assemblies are intentionally NOT referenced - everything is detected by name
  /// (snapshot knowledge of the FUSE-fx contract and entity annotations).
  /// </summary>
  internal static class FuseRepositoryDetector {

    private const string _RepositoryDefinitionFullName = "System.Data.Fuse.IRepository`2";
    private const string _CompositeKeyPrefix = "System.Data.Fuse.CompositeKey";
    private const string _PrimaryIdentityAttributeName = "PrimaryIdentityAttribute";
    private const string _PropertyGroupAttributeName = "PropertyGroupAttribute";
    private const string _UniquePropertyGroupAttributeName = "UniquePropertyGroupAttribute";
    private const string _KeyAttributeName = "KeyAttribute";

    /// <summary>
    /// Returns the entity descriptor if the type closes the FUSE repository contract with a resolvable entity, otherwise null.
    /// </summary>
    public static FuseEntityDescriptor TryDetect(Type type) {

      Type repositoryInterface = FindClosedRepositoryInterface(type);
      if (repositoryInterface == null) {
        return null;
      }

      Type[] arguments = repositoryInterface.GetGenericArguments();
      Type entityType = arguments[0];
      Type keyType = arguments[1];

      try {
        return BuildDescriptor(entityType, keyType);
      } catch (FileNotFoundException) {
        //the entity type (or one of its base types) lives in an assembly which is not resolvable
        return null;
      } catch (TypeLoadException) {
        return null;
      }
    }

    /// <summary>
    /// Searches the interfaces of the type (which includes everything inherited from base types)
    /// for a closed 'IRepository&lt;TEntity, TKey&gt;'.
    /// </summary>
    private static Type FindClosedRepositoryInterface(Type type) {

      List<Type> candidates = new List<Type>();
      if (type.IsInterface) {
        candidates.Add(type);
      }

      try {
        candidates.AddRange(type.GetInterfaces());
      } catch (FileNotFoundException) {
        return null;
      } catch (TypeLoadException) {
        return null;
      }

      foreach (Type candidate in candidates) {

        if (!candidate.IsGenericType || candidate.ContainsGenericParameters) {
          continue;
        }

        if (candidate.GetGenericTypeDefinition().FullName == _RepositoryDefinitionFullName) {
          return candidate;
        }
      }

      return null;
    }

    /// <summary>
    /// Builds the descriptor (fields, key fields and an empty entity template).
    /// </summary>
    private static FuseEntityDescriptor BuildDescriptor(Type entityType, Type keyType) {

      XmlDocReader docs = XmlDocReader.ForAssembly(entityType.Assembly);

      FuseEntityDescriptor descriptor = new FuseEntityDescriptor();
      descriptor.EntityName = AssemblyInspector.GetFriendlyTypeName(entityType);
      descriptor.EntityFullName = entityType.FullName;
      descriptor.EntitySummary = docs.GetTypeSummary(entityType);
      descriptor.KeyTypeName = AssemblyInspector.GetFriendlyTypeName(keyType);
      descriptor.EntityTemplateJson = RequestTemplateBuilder.BuildEmptyObjectJson(entityType);

      List<PropertyInfo> properties = GetEntityProperties(entityType);
      string[] keyPropertyNames = ResolveKeyPropertyNames(entityType, keyType, properties);

      bool isComposite = (keyType.FullName != null && keyType.FullName.StartsWith(_CompositeKeyPrefix, StringComparison.Ordinal));
      if (isComposite) {
        descriptor.KeyKind = FuseEntityDescriptor._KeyKindComposite;
      } else {
        descriptor.KeyKind = FuseEntityDescriptor._KeyKindScalar;
      }

      List<FuseFieldDescriptor> fields = new List<FuseFieldDescriptor>();
      foreach (PropertyInfo property in properties) {

        XmlDocReader memberDocs = XmlDocReader.ForAssembly(property.DeclaringType.Assembly);

        FuseFieldDescriptor field = new FuseFieldDescriptor();
        field.Name = RequestTemplateBuilder.GetJsonName(property.Name);
        field.PropertyName = property.Name;
        field.TypeName = AssemblyInspector.GetFriendlyTypeName(property.PropertyType);
        field.Description = memberDocs.GetMemberSummary(property);
        field.IsKey = keyPropertyNames.Contains(property.Name, StringComparer.Ordinal);
        ApplyValueKind(field, property.PropertyType);

        fields.Add(field);
      }

      descriptor.Fields = fields.ToArray();
      descriptor.KeyFieldNames = keyPropertyNames.Select((name) => RequestTemplateBuilder.GetJsonName(name)).ToArray();

      return descriptor;
    }

    /// <summary>
    /// Returns the readable, non-indexed public instance properties of the entity in declaration order.
    /// </summary>
    private static List<PropertyInfo> GetEntityProperties(Type entityType) {

      PropertyInfo[] properties;
      try {
        properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
      } catch (FileNotFoundException) {
        properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
      }

      List<PropertyInfo> result = new List<PropertyInfo>();
      HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

      foreach (PropertyInfo property in properties) {

        if (property.GetIndexParameters().Length > 0 || property.GetGetMethod() == null) {
          continue;
        }

        if (names.Add(property.Name)) {
          result.Add(property);
        }
      }

      return result;
    }

    /// <summary>
    /// Resolves the names of the key properties:
    /// (1) [PrimaryIdentity(group)] + [PropertyGroup]/[UniquePropertyGroup] with the same group name,
    /// (2) properties annotated with [Key],
    /// (3) a property named 'Id' or '&lt;Entity&gt;Id',
    /// (4) for scalar keys: the first property whose type matches the key type.
    /// </summary>
    private static string[] ResolveKeyPropertyNames(Type entityType, Type keyType, List<PropertyInfo> properties) {

      string[] fromGroups = ResolveKeyByPropertyGroups(entityType);
      if (fromGroups.Length > 0) {
        return fromGroups;
      }

      string[] fromKeyAttributes = properties
        .Where((p) => HasAttribute(p.GetCustomAttributesData(), _KeyAttributeName))
        .Select((p) => p.Name)
        .ToArray();
      if (fromKeyAttributes.Length > 0) {
        return fromKeyAttributes;
      }

      PropertyInfo idProperty = properties.FirstOrDefault((p) => string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase));
      if (idProperty == null) {
        idProperty = properties.FirstOrDefault((p) => string.Equals(p.Name, entityType.Name + "Id", StringComparison.OrdinalIgnoreCase));
      }
      if (idProperty != null) {
        return new string[] { idProperty.Name };
      }

      PropertyInfo typeMatch = properties.FirstOrDefault((p) => p.PropertyType == keyType);
      if (typeMatch != null) {
        return new string[] { typeMatch.Name };
      }

      return new string[0];
    }

    /// <summary>
    /// Evaluates the FUSE entity annotations [PrimaryIdentity] and [(Unique)PropertyGroup] (by attribute name only).
    /// </summary>
    private static string[] ResolveKeyByPropertyGroups(Type entityType) {

      IList<CustomAttributeData> attributes;
      try {
        attributes = entityType.GetCustomAttributesData();
      } catch (FileNotFoundException) {
        return new string[0];
      }

      CustomAttributeData primaryIdentity = attributes.FirstOrDefault((a) => a.AttributeType.Name == _PrimaryIdentityAttributeName);
      if (primaryIdentity == null || primaryIdentity.ConstructorArguments.Count < 1) {
        return new string[0];
      }

      string groupName = primaryIdentity.ConstructorArguments[0].Value as string;
      if (string.IsNullOrEmpty(groupName)) {
        return new string[0];
      }

      foreach (CustomAttributeData attribute in attributes) {

        string attributeName = attribute.AttributeType.Name;
        bool isGroup = (attributeName == _PropertyGroupAttributeName || attributeName == _UniquePropertyGroupAttributeName);
        if (!isGroup || attribute.ConstructorArguments.Count < 2) {
          continue;
        }

        string currentGroupName = attribute.ConstructorArguments[0].Value as string;
        if (!string.Equals(currentGroupName, groupName, StringComparison.Ordinal)) {
          continue;
        }

        List<string> propertyNames = new List<string>();
        for (int i = 1; i < attribute.ConstructorArguments.Count; i++) {
          AddArgumentValues(propertyNames, attribute.ConstructorArguments[i]);
        }

        return propertyNames.ToArray();
      }

      return new string[0];
    }

    /// <summary>
    /// Adds a string constructor argument (or all strings of a params array argument).
    /// </summary>
    private static void AddArgumentValues(List<string> target, CustomAttributeTypedArgument argument) {

      string singleValue = argument.Value as string;
      if (singleValue != null) {
        target.Add(singleValue);
        return;
      }

      ReadOnlyCollection<CustomAttributeTypedArgument> arrayValue = argument.Value as ReadOnlyCollection<CustomAttributeTypedArgument>;
      if (arrayValue != null) {
        foreach (CustomAttributeTypedArgument element in arrayValue) {
          string elementValue = element.Value as string;
          if (elementValue != null) {
            target.Add(elementValue);
          }
        }
      }
    }

    /// <summary>
    /// Returns true if an attribute with the given (short) type name is present.
    /// </summary>
    private static bool HasAttribute(IList<CustomAttributeData> attributes, string attributeName) {

      foreach (CustomAttributeData attribute in attributes) {
        if (attribute.AttributeType.Name == attributeName) {
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Classifies the field type for editors and filter operators.
    /// </summary>
    private static void ApplyValueKind(FuseFieldDescriptor field, Type propertyType) {

      Type type = propertyType;
      field.IsNullable = !type.IsValueType;
      if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1") {
        type = type.GetGenericArguments()[0];
        field.IsNullable = true;
      }

      if (type.IsEnum) {
        field.ValueKind = FuseFieldDescriptor._ValueKindEnum;
        field.EnumValues = type.GetEnumNames();
        return;
      }

      switch (type.FullName) {
        case "System.String":
        case "System.Char":
          field.ValueKind = FuseFieldDescriptor._ValueKindString;
          return;
        case "System.Boolean":
          field.ValueKind = FuseFieldDescriptor._ValueKindBoolean;
          return;
        case "System.Byte":
        case "System.SByte":
        case "System.Int16":
        case "System.UInt16":
        case "System.Int32":
        case "System.UInt32":
        case "System.Int64":
        case "System.UInt64":
        case "System.Single":
        case "System.Double":
        case "System.Decimal":
          field.ValueKind = FuseFieldDescriptor._ValueKindNumber;
          return;
        case "System.DateTime":
        case "System.DateTimeOffset":
          field.ValueKind = FuseFieldDescriptor._ValueKindDate;
          return;
        case "System.Guid":
          field.ValueKind = FuseFieldDescriptor._ValueKindGuid;
          return;
        case "System.TimeSpan":
          field.ValueKind = FuseFieldDescriptor._ValueKindString;
          return;
      }

      field.ValueKind = FuseFieldDescriptor._ValueKindComplex;
    }

  }

}
