using Logging.SmartStandards.CopyForUJMW;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;

namespace System.Web.UJMW {

  public static class UjmwExtensions {

    /// <summary>
    /// Checks if the given alias matches to the currentContract, and returns a UjmwContractAliasMatchResult indicating the result of the match.
    /// </summary>
    /// <param name="currentContract"></param>
    /// <param name="endpointQualifingNameToCompare"></param>
    /// <param name="versionMatching"></param>
    /// <param name="reverse">
    ///  Only relevant for UjmwContractVersionMatching.AnyHigher/.SemanticSafe, this indicates whether the version compatibility should be checked in reverse.
    ///  Normally the endpointQualifingNameToCompare can be higher than the currentContract, but with reverse=true, the endpointQualifingNameToCompare can be lower than the currentContract (view from the 'hoster's position).
    /// </param>
    /// <returns></returns>
    public static UjmwContractAliasMatchResult MatchUjmwEndpointQualifyingName(this Type currentContract, string endpointQualifingNameToCompare, UjmwContractVersionMatching versionMatching = UjmwContractVersionMatching.SemanticSafe, bool reverse = false) {
    
      string alias = currentContract.BuildTypeAliasRecursive(false, 1, null);
      Version currentContractVersion = currentContract.Assembly.GetName().Version ?? new Version(0, 0, 0);

      if (!endpointQualifingNameToCompare.StartsWith("UJMW:")) {
        return UjmwContractAliasMatchResult.NotAnUjmwContract;
      }

      if (!endpointQualifingNameToCompare.StartsWith("UJMW:" + alias, StringComparison.InvariantCultureIgnoreCase)) {
        return UjmwContractAliasMatchResult.ContractAliasMismatch;
      }

      if(versionMatching == UjmwContractVersionMatching.IgnoreVersion) {
        return UjmwContractAliasMatchResult.Match;
      }

      Version versionToCompare = new Version(0, 0, 0);
      int vIdx = endpointQualifingNameToCompare.IndexOf("/");
      if (vIdx > 0) {
        string versionPart = endpointQualifingNameToCompare.Substring(vIdx + 1);
        if (!Version.TryParse(versionPart, out versionToCompare)) {
          return UjmwContractAliasMatchResult.ContractVersionMismatch;
        }
      }
      else {
        return UjmwContractAliasMatchResult.ContractVersionNotPresent;
      }

      if (reverse) {
        return MatchVersions(
          versionToCompare, currentContractVersion, versionMatching, out bool _
        ) ? UjmwContractAliasMatchResult.Match : UjmwContractAliasMatchResult.ContractVersionMismatch;
      }
      else {
        return MatchVersions(
          currentContractVersion, versionToCompare, versionMatching, out bool _
        ) ? UjmwContractAliasMatchResult.Match : UjmwContractAliasMatchResult.ContractVersionMismatch;
      }

    }

    /// <summary>
    /// Checks if the given alias matches to the currentContract, and returns a UjmwContractAliasMatchResult indicating the result of the match.
    /// </summary>
    /// <param name="currentContract"></param>
    /// <param name="endpointQualifingNameToCompare"></param>
    /// <param name="versionMatching"></param>
    /// <param name="reverse">
    ///  Only relevant for UjmwContractVersionMatching.AnyHigher/.SemanticSafe, this indicates whether the version compatibility should be checked in reverse.
    ///  Normally the endpointQualifingNameToCompare can be higher than the currentContract, but with reverse=true, the endpointQualifingNameToCompare can be lower than the currentContract (view from the 'hoster's position).
    /// </param>
    /// <returns></returns>
    public static UjmwContractAliasMatchResult MatchSimplifiedUjmwEndpointQualifyingName(this Type currentContract, string endpointQualifingNameToCompare, UjmwContractVersionMatching versionMatching = UjmwContractVersionMatching.SemanticSafe, bool reverse = false) {

      string alias = currentContract.BuildTypeAliasRecursive(true, 1, null);
      Version currentContractVersion = currentContract.Assembly.GetName().Version ?? new Version(0, 0, 0);

      if (!endpointQualifingNameToCompare.StartsWith("UJMW:")) {
        return UjmwContractAliasMatchResult.NotAnUjmwContract;
      }

      if (!endpointQualifingNameToCompare.StartsWith("UJMW:" + alias, StringComparison.InvariantCultureIgnoreCase)) {
        return UjmwContractAliasMatchResult.ContractAliasMismatch;
      }

      if (versionMatching == UjmwContractVersionMatching.IgnoreVersion) {
        return UjmwContractAliasMatchResult.Match;
      }

      Version versionToCompare = new Version(0, 0, 0);
      int vIdx = endpointQualifingNameToCompare.IndexOf("/");
      if (vIdx > 0) {
        string versionPart = endpointQualifingNameToCompare.Substring(vIdx + 1);
        if (!Version.TryParse(versionPart, out versionToCompare)) {
          return UjmwContractAliasMatchResult.ContractVersionMismatch;
        }
      }
      else {
        return UjmwContractAliasMatchResult.ContractVersionNotPresent;
      }

      if (reverse) {
        return MatchVersions(
          versionToCompare, currentContractVersion, versionMatching, out bool _
        ) ? UjmwContractAliasMatchResult.Match : UjmwContractAliasMatchResult.ContractVersionMismatch;
      }
      else {
        return MatchVersions(
          currentContractVersion, versionToCompare, versionMatching, out bool _
        ) ? UjmwContractAliasMatchResult.Match : UjmwContractAliasMatchResult.ContractVersionMismatch;
      }

    }

    /// <summary>
    /// Checks if the given alias matches to the currentContract, and throws a UjmwContractMismatchException if not.
    /// </summary>
    /// <param name="currentContract"></param>
    /// <param name="endpointQualifingNameToCompare"></param>
    /// <param name="versionMatching"></param>
    /// <param name="reverse">
    ///  Only relevant for UjmwContractVersionMatching.AnyHigher/.SemanticSafe, this indicates whether the version compatibility should be checked in reverse.
    ///  Normally the endpointQualifingNameToCompare can be higher than the currentContract, but with reverse=true, the endpointQualifingNameToCompare can be lower than the currentContract (view from the 'hoster's position).
    /// </param>
    /// <exception cref="UjmwContractMismatchException"></exception>
    public static void EnsureUjmwEndpointQualifyingNameMatches(this Type currentContract, string endpointQualifingNameToCompare, UjmwContractVersionMatching versionMatching = UjmwContractVersionMatching.SemanticSafe, bool reverse = false) {
   
      string alias = currentContract.BuildTypeAliasRecursive( false, 1, null);
      Version currentContractVersion = currentContract.Assembly.GetName().Version ?? new Version(0, 0, 0);

      if (!endpointQualifingNameToCompare.StartsWith("UJMW:")) {
        throw new UjmwContractMismatchException($"The given EndpointQualifingName '{endpointQualifingNameToCompare}' cannot be matched to 'UJMW:{alias}/{currentContractVersion.ToString(3)}' because it is not 'UJMW:' - prefixed! #72091");
      }

      if (!endpointQualifingNameToCompare.StartsWith("UJMW:" + alias, StringComparison.InvariantCultureIgnoreCase)) {
        throw new UjmwContractMismatchException($"The given EndpointQualifingName '{endpointQualifingNameToCompare}' does not match to 'UJMW:{alias}/{currentContractVersion.ToString(3)}' because this is a different Contract! #72092");
      }

      if (versionMatching == UjmwContractVersionMatching.IgnoreVersion) {
        return;
      }

      Version versionToCompare = new Version(0, 0, 0);
      int vIdx = endpointQualifingNameToCompare.IndexOf("/");
      if (vIdx > 0) {
        string versionPart = endpointQualifingNameToCompare.Substring(vIdx + 1);
        if (!Version.TryParse(versionPart, out versionToCompare)) {
          throw new UjmwContractMismatchException($"The given EndpointQualifingName '{endpointQualifingNameToCompare}' cannot be matched to 'UJMW:{alias}/{currentContractVersion.ToString(3)}' because it contains no parsable version information! #72094");
        }
      }
      else {
        throw new UjmwContractMismatchException($"The given EndpointQualifingName '{endpointQualifingNameToCompare}' cannot be matched to 'UJMW:{alias}/{currentContractVersion.ToString(3)}' because it contains no parsable version information! #72094");
      }

      bool wasMajorTooHigh = false;
      if (reverse) {
        if(MatchVersions(versionToCompare, currentContractVersion, versionMatching, out wasMajorTooHigh)) {
          return;
        }
      }
      else {
        if(MatchVersions(currentContractVersion, versionToCompare, versionMatching, out wasMajorTooHigh)) {
          return;
        }
      }

      if(versionMatching == UjmwContractVersionMatching.Exact) {
        throw new UjmwContractMismatchException($"The given EndpointQualifingName '{endpointQualifingNameToCompare}' does not have to the exactly required version'{currentContractVersion.ToString(3)}'! #72093");
      }
      else if (wasMajorTooHigh) {
        throw new UjmwContractMismatchException($"The given EndpointQualifingName '{endpointQualifingNameToCompare}' does not have a version, which is compatible to '{currentContractVersion.ToString(3)}'! #72093");
      }
      else {
        throw new UjmwContractMismatchException($"The given EndpointQualifingName '{endpointQualifingNameToCompare}' does not have a version, which is compatible to '{currentContractVersion.ToString(3)}' (Semantic-Versioning treats higher major versions as incompatible)! #72093");
      }

    }

    private static bool MatchVersions(
      Version leftVersion, Version rightVersion,
      UjmwContractVersionMatching versionMatching, out bool toHighMajor
    ) {      
      toHighMajor = false;    
      if (versionMatching == UjmwContractVersionMatching.IgnoreVersion) {
        return true;
      }

      if (versionMatching == UjmwContractVersionMatching.Exact) {
        return leftVersion.Major == rightVersion.Major && leftVersion.Minor == rightVersion.Minor && leftVersion.Build == rightVersion.Build;
      }

      if (versionMatching == UjmwContractVersionMatching.SemanticSafe && leftVersion.Major < rightVersion.Major) {
        toHighMajor = true;
        return false;
      }

      if (leftVersion.Major > rightVersion.Major) {
        return false;
      }
      else if (leftVersion.Major < rightVersion.Major) {
        return true;
      }

      if (leftVersion.Minor > rightVersion.Minor) {
        return false;
      }
      else if (leftVersion.Minor < rightVersion.Minor) {
        return true;
      }

      if (leftVersion.Build > rightVersion.Build) {
        //to discuss...
        return false;
      }
      return true;
    }

    /// <summary>
    /// Returns an alias like 'UJMW:MyNamepsace.MyApiContract.IRepository_BusinessEntity/7.12.3'
    /// </summary>
    /// <param name="t"></param>
    /// <returns></returns>
    public static string BuildUjmwEndpointQualifyingName(this Type t) {
       string alias = $"UJMW:{TypeAliasBuilder.BuildTypeAliasRecursive(t, false, 1, null)}/{t.Assembly.GetName().Version?.ToString(3)}";
       return alias; 
    }

    /// <summary>
    /// Returns an alias like 'UJMW:MyApiContract.IRepository_BusinessEntity/7.12.3'
    /// </summary>
    /// <param name="t"></param>
    /// <returns></returns>
    public static string BuildSimplifiedUjmwEndpointQualifyingName(this Type t) {
      string alias = $"UJMW:{TypeAliasBuilder.BuildTypeAliasRecursive(t, true, 1, null)}/{t.Assembly.GetName().Version?.ToString(3)}";
      return alias;
    }

  }

  public class UjmwContractMismatchException : Exception {
    public UjmwContractMismatchException(string message) : base(message) {
    }
  }

  public enum UjmwContractAliasMatchResult {
    Match = 0,
    NotAnUjmwContract = 72091,
    ContractAliasMismatch = 72092,
    ContractVersionMismatch = 72093,
    ContractVersionNotPresent = 72094,
  }

  public enum UjmwContractVersionMatching {
    IgnoreVersion = 0,
    EqualOrHigher = 1,
    SemanticSafe = 2,
    Exact = 3,
  }

}
