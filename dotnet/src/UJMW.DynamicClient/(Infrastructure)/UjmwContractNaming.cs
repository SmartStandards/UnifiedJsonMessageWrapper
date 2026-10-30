using System;

namespace System.Web.UJMW {

  /// <summary>
  /// UJMW-specific naming convention for contract names as they appear in endpoint routes
  /// (same rule as the dynamic UJMW controller: a leading 'I' of interfaces is removed
  /// when followed by an uppercase letter, the generic arity suffix is removed).
  /// </summary>
  public static class UjmwContractNaming {

    /// <summary>
    /// Returns the route contract name for the given contract type.
    /// </summary>
    public static string GetContractName(Type contractType) {
      if (contractType == null) {
        throw new ArgumentNullException(nameof(contractType));
      }
      return GetContractName(contractType.Name, contractType.IsInterface);
    }

    /// <summary>
    /// Returns the route contract name for a (full or simple) type name.
    /// Use this overload when the type is only known by its name (e.g. metadata inspection).
    /// </summary>
    /// <param name="typeName">Simple or full qualified type name (namespace and nesting are cut off).</param>
    /// <param name="isInterface">True when the type is an interface.</param>
    public static string GetContractName(string typeName, bool isInterface) {
      if (string.IsNullOrWhiteSpace(typeName)) {
        return string.Empty;
      }
      string name = typeName.Trim();
      int lastSeparatorIndex = Math.Max(name.LastIndexOf('.'), name.LastIndexOf('+'));
      if (lastSeparatorIndex >= 0) {
        name = name.Substring(lastSeparatorIndex + 1);
      }
      int genericIndex = name.IndexOf('`');
      if (genericIndex >= 0) {
        name = name.Substring(0, genericIndex);
      }
      if (isInterface && name.Length > 1 && name.StartsWith("I") && char.IsUpper(name[1])) {
        name = name.Substring(1);
      }
      return name;
    }

    /// <summary>
    /// Returns the qualifying contract name (without 'UJMW:' prefix and version) exactly as it is used within
    /// the endpoint qualifying names (see 'BuildUjmwEndpointQualifyingName') - for example
    /// 'System.Data.Fuse.IRepository_MyNamespace.Person_Int32' for a closed generic contract.
    /// This is the name to be used for resolving contracts via an infrastructure.
    /// </summary>
    public static string GetQualifyingContractName(Type contractType) {
      if (contractType == null) {
        throw new ArgumentNullException(nameof(contractType));
      }
      return TypeAliasBuilder.BuildTypeAliasRecursive(contractType, false, 1, null);
    }

  }

}
