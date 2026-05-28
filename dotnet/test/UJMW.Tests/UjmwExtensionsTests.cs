using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Web.UJMW.SomeNamespace1;
using System.Web.UJMW.SomeNamespace1.Keys;
using System.Web.UJMW.SomeNamespace2;
using static System.Web.UJMW.DynamicClientFactory;
using static System.Web.UJMW.UjmwExtensions;

namespace System.Web.UJMW {

  [TestClass]
  public class UjmwExtensionsTests {

    [TestMethod]
    public void ContractAliasTest_Full() {

      Type contractType = typeof(IFoo<FooEntity, FooKey, BarEntity>);
      string thisAssemblyVersion = typeof(UjmwExtensionsTests).Assembly.GetName().Version.ToString(3);

      string alias = contractType.BuildUjmwEndpointQualifyingName();

      Assert.AreEqual(
        "UJMW:System.Web.UJMW.SomeNamespace1.IFoo_FooEntity_Keys.FooKey_System.Web.UJMW.SomeNamespace2.BarEntity/"
        + thisAssemblyVersion, alias
      );

    }

    [TestMethod]
    public void ContractAliasTest_Simplified() {

      Type contractType = typeof(IFoo<FooEntity, FooKey, BarEntity>);
      string thisAssemblyVersion = typeof(UjmwExtensionsTests).Assembly.GetName().Version.ToString(3);

      string alias = contractType.BuildSimplifiedUjmwEndpointQualifyingName();

      Assert.AreEqual(
        "UJMW:IFoo_FooEntity_FooKey_BarEntity/"
        + thisAssemblyVersion, alias
      );

    }

    [TestMethod]
    public void ContractMathchingTest() {

      Type contractType = typeof(IBar);
      Version thisAssemblyVersion = typeof(UjmwExtensionsTests).Assembly.GetName().Version;

      Assert.AreEqual(
        UjmwContractAliasMatchResult.NotAnUjmwContract,
        contractType.MatchUjmwEndpointQualifyingName(contractType.FullName)
      );

      Assert.AreEqual(
        UjmwContractAliasMatchResult.ContractAliasMismatch,
        contractType.MatchUjmwEndpointQualifyingName("UJMW:IFoo")
      );

      Assert.AreEqual(
        UjmwContractAliasMatchResult.ContractVersionNotPresent,
        contractType.MatchUjmwEndpointQualifyingName($"UJMW:{contractType.FullName}")
      );

      Assert.AreEqual(
        UjmwContractAliasMatchResult.Match,
        contractType.MatchUjmwEndpointQualifyingName($"UJMW:{contractType.FullName}", UjmwContractVersionMatching.IgnoreVersion)
      );

      string aliasWithExactVersion = $"UJMW:{contractType.FullName}/{thisAssemblyVersion.ToString(3)}";
      Assert.AreEqual(
        UjmwContractAliasMatchResult.Match,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithExactVersion, UjmwContractVersionMatching.IgnoreVersion)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.Match,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithExactVersion, UjmwContractVersionMatching.EqualOrHigher)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.Match,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithExactVersion, UjmwContractVersionMatching.SemanticSafe)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.Match,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithExactVersion, UjmwContractVersionMatching.Exact)
      );

      string aliasWithLowerVersion = $"UJMW:{contractType.FullName}/{new Version(thisAssemblyVersion.Major - 1, thisAssemblyVersion.Minor, thisAssemblyVersion.Build).ToString(3)}";
      Assert.AreEqual(
        UjmwContractAliasMatchResult.Match,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithLowerVersion, UjmwContractVersionMatching.IgnoreVersion)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.ContractVersionMismatch,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithLowerVersion, UjmwContractVersionMatching.EqualOrHigher)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.ContractVersionMismatch,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithLowerVersion, UjmwContractVersionMatching.SemanticSafe)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.ContractVersionMismatch,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithLowerVersion, UjmwContractVersionMatching.Exact)
      );

      string aliasWithHigherMajorVersion = $"UJMW:{contractType.FullName}/{new Version(thisAssemblyVersion.Major + 1, thisAssemblyVersion.Minor, thisAssemblyVersion.Build).ToString(3)}";
      Assert.AreEqual(
        UjmwContractAliasMatchResult.Match,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithHigherMajorVersion, UjmwContractVersionMatching.IgnoreVersion)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.Match,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithHigherMajorVersion, UjmwContractVersionMatching.EqualOrHigher)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.ContractVersionMismatch,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithHigherMajorVersion, UjmwContractVersionMatching.SemanticSafe)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.ContractVersionMismatch,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithHigherMajorVersion, UjmwContractVersionMatching.Exact)
      );

      string aliasWithHigherVersion = $"UJMW:{contractType.FullName}/{new Version(thisAssemblyVersion.Major, thisAssemblyVersion.Minor + 1, thisAssemblyVersion.Build).ToString(3)}";
      Assert.AreEqual(
        UjmwContractAliasMatchResult.Match,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithHigherVersion, UjmwContractVersionMatching.IgnoreVersion)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.Match,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithHigherVersion, UjmwContractVersionMatching.EqualOrHigher)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.Match,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithHigherVersion, UjmwContractVersionMatching.SemanticSafe)
      );
      Assert.AreEqual(
        UjmwContractAliasMatchResult.ContractVersionMismatch,
        contractType.MatchUjmwEndpointQualifyingName(aliasWithHigherVersion, UjmwContractVersionMatching.Exact)
      );

    }

  }

  namespace SomeNamespace1 {

    internal interface IFoo<TEntity,TKey,TForeignEntity> { 
    }

    internal class  FooEntity {     
    }

    namespace Keys{

      internal class FooKey {
      }

    }

  }

  namespace SomeNamespace2 {

    internal interface IBar {
    }

    internal class BarEntity {
    }

  }

}
