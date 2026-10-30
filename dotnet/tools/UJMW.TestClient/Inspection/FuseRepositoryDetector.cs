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
    private const int _MaxCompositeKeyFieldCount = 5;

    /// <summary>
    /// Name of the FUSE-fx assembly which contains 'IRepository&lt;TEntity, TKey&gt;' and 'CompositeKeyN'.
    /// </summary>
    public const string _RepositoryContractAssemblyName = "FUSE-fx.RepositoryContract";
    private const string _PrimaryIdentityAttributeName = "PrimaryIdentityAttribute";
    private const string _PropertyGroupAttributeName = "PropertyGroupAttribute";
    private const string _UniquePropertyGroupAttributeName = "UniquePropertyGroupAttribute";
    private const string _KeyAttributeName = "KeyAttribute";

    /// <summary>
    /// Returns the entity descriptor if the type closes the FUSE repository contract with a resolvable entity, otherwise null.
    /// </summary>
    public static FuseEntityDescriptor TryDetect(Type type) {
      return TryDetect(type, null);
    }

    /// <summary>
    /// Returns the entity descriptor if the type closes the FUSE repository contract with a resolvable entity, otherwise null.
    /// When key property names are given, they are used instead of the automatic key resolution.
    /// </summary>
    public static FuseEntityDescriptor TryDetect(Type type, string[] keyPropertyNamesOverride) {

      Type repositoryInterface = FindClosedRepositoryInterface(type);
      if (repositoryInterface == null) {
        return null;
      }

      Type[] arguments = repositoryInterface.GetGenericArguments();
      Type entityType = arguments[0];
      Type keyType = arguments[1];

      try {
        return BuildDescriptor(entityType, keyType, keyPropertyNamesOverride);
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
    private static FuseEntityDescriptor BuildDescriptor(Type entityType, Type keyType, string[] keyPropertyNamesOverride) {

      XmlDocReader docs = XmlDocReader.ForAssembly(entityType.Assembly);

      FuseEntityDescriptor descriptor = new FuseEntityDescriptor();
      descriptor.EntityName = AssemblyInspector.GetFriendlyTypeName(entityType);
      descriptor.EntityFullName = entityType.FullName;
      descriptor.EntitySummary = docs.GetTypeSummary(entityType);
      descriptor.KeyTypeName = AssemblyInspector.GetFriendlyTypeName(keyType);
      descriptor.EntityTemplateJson = RequestTemplateBuilder.BuildEmptyObjectJson(entityType);

      List<PropertyInfo> properties = GetEntityProperties(entityType);
      string[] keyPropertyNames;
      if (keyPropertyNamesOverride != null && keyPropertyNamesOverride.Length > 0) {
        keyPropertyNames = keyPropertyNamesOverride;
      } else {
        keyPropertyNames = ResolveKeyPropertyNames(entityType, keyType, properties);
      }

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
    internal static List<PropertyInfo> GetEntityProperties(Type entityType) {

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
    /// (3) a property named 'Id' or '&lt;Entity&gt;Id' or 'Uid',
    /// (4) for scalar keys: the first property whose type matches the key type (only if a key type is given).
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
      if (idProperty == null) {
        idProperty = properties.FirstOrDefault((p) => string.Equals(p.Name, "Uid", StringComparison.OrdinalIgnoreCase));
      }
      if (idProperty != null) {
        return new string[] { idProperty.Name };
      }

      if (keyType == null) {
        return new string[0];
      }

      PropertyInfo typeMatch = properties.FirstOrDefault((p) => p.PropertyType == keyType);
      if (typeMatch != null) {
        return new string[] { typeMatch.Name };
      }

      return new string[0];
    }

    /// <summary>
    /// Resolves the key properties of an entity class without a given key type ('AUTO' key selection):
    /// FUSE key annotations first, then a property named 'Id', '&lt;Entity&gt;Id' or 'Uid'.
    /// </summary>
    public static string[] ResolveAutoKeyPropertyNames(Type entityType, List<PropertyInfo> properties) {
      return ResolveKeyPropertyNames(entityType, null, properties);
    }

    /// <summary>
    /// Builds the closed type 'IRepository&lt;TEntity, TKey&gt;' for an entity class (synthetic repository contract).
    /// More than one key property results in a FUSE 'CompositeKeyN' key, otherwise the given key type is used.
    /// </summary>
    public static Type MakeRepositoryType(MetadataLoadContext context, Type entityType, string keyTypeFullName, string[] keyPropertyNames) {

      Assembly contractAssembly;
      try {
        contractAssembly = context.LoadFromAssemblyName(_RepositoryContractAssemblyName);
      } catch (FileNotFoundException) {
        //expected: neither the DLL folder, its deps.json nor the NuGet cache provides FUSE-fx
        throw new InvalidOperationException(
          $"The assembly '{_RepositoryContractAssemblyName}' could not be found (neither next to the DLL nor in the NuGet cache). " +
          "Restore the NuGet package 'FUSE-fx.RepositoryContract' once (e.g. in any project) to use entity repositories."
        );
      }

      Type repositoryDefinition = contractAssembly.GetType(_RepositoryDefinitionFullName, false);
      if (repositoryDefinition == null) {
        throw new InvalidOperationException($"'{_RepositoryDefinitionFullName}' was not found in '{contractAssembly.FullName}'.");
      }

      List<PropertyInfo> properties = GetEntityProperties(entityType);
      Type keyType;

      if (keyPropertyNames != null && keyPropertyNames.Length > 1) {

        if (keyPropertyNames.Length > _MaxCompositeKeyFieldCount) {
          throw new InvalidOperationException($"A composite key can consist of at most {_MaxCompositeKeyFieldCount} properties.");
        }

        string compositeName = _CompositeKeyPrefix + keyPropertyNames.Length + "`" + keyPropertyNames.Length;
        Type compositeDefinition = contractAssembly.GetType(compositeName, false);
        if (compositeDefinition == null) {
          throw new InvalidOperationException($"'{compositeName}' was not found in '{contractAssembly.FullName}'.");
        }

        Type[] fieldTypes = new Type[keyPropertyNames.Length];
        for (int i = 0; i < keyPropertyNames.Length; i++) {
          string propertyName = keyPropertyNames[i];
          PropertyInfo property = properties.FirstOrDefault((p) => p.Name == propertyName);
          if (property == null) {
            throw new InvalidOperationException($"The entity '{entityType.FullName}' has no property '{propertyName}'.");
          }
          fieldTypes[i] = property.PropertyType;
        }

        keyType = compositeDefinition.MakeGenericType(fieldTypes);

      } else {
        keyType = ResolveKeyType(context, entityType, properties, keyTypeFullName);
      }

      return repositoryDefinition.MakeGenericType(entityType, keyType);
    }

    /// <summary>
    /// Resolves a scalar key type by full name: types of entity properties, base types of the entity,
    /// the core library and finally the assembly of the entity.
    /// </summary>
    private static Type ResolveKeyType(MetadataLoadContext context, Type entityType, List<PropertyInfo> properties, string keyTypeFullName) {

      if (string.IsNullOrWhiteSpace(keyTypeFullName)) {
        throw new InvalidOperationException("No key type was selected.");
      }

      foreach (PropertyInfo property in properties) {
        Type propertyType = GetUnderlyingType(property.PropertyType);
        if (propertyType.FullName == keyTypeFullName) {
          return propertyType;
        }
      }

      foreach (Type baseType in GetBaseTypes(entityType)) {
        if (baseType.FullName == keyTypeFullName) {
          return baseType;
        }
      }

      Type coreType = context.CoreAssembly.GetType(keyTypeFullName, false);
      if (coreType != null) {
        return coreType;
      }

      Type entityAssemblyType = entityType.Assembly.GetType(keyTypeFullName, false);
      if (entityAssemblyType != null) {
        return entityAssemblyType;
      }

      throw new InvalidOperationException($"The key type '{keyTypeFullName}' could not be resolved.");
    }

    /// <summary>
    /// Returns the base types of a class (nearest first, without 'System.Object'). Unresolvable base types end the chain.
    /// </summary>
    public static Type[] GetBaseTypes(Type type) {

      List<Type> result = new List<Type>();
      try {
        Type current = type.BaseType;
        while (current != null && current.FullName != "System.Object") {
          result.Add(current);
          current = current.BaseType;
        }
      } catch (FileNotFoundException) {
        //a base type lives in an assembly which is not available -> the chain ends here
      } catch (TypeLoadException) {
        //same as above
      }

      return result.ToArray();
    }

    /// <summary>
    /// Returns the underlying type of 'Nullable&lt;T&gt;' or the type itself.
    /// </summary>
    public static Type GetUnderlyingType(Type type) {

      if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1") {
        return type.GetGenericArguments()[0];
      }

      return type;
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
