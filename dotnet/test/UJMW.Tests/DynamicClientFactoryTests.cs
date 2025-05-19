using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Web.UJMW
{
  [TestClass]
  public class DynamicClientFactoryTests
  {
    public interface IBaseInterface
    {
      void FirstMethod();
    }

    public interface IIntermediateInterface : IBaseInterface
    {
      new void FirstMethod();
      void SecondMethod();
    }

    public interface IDerivedInterface : IIntermediateInterface
    {
      new void FirstMethod();
      void ThirdMethod();
    }

    [TestMethod]
    public void CreateInstance_InterfaceWithInheritanceChainAndShadowing_ShouldNotCauseException()
    {
      DynamicClientFactory.CreateInstance<IDerivedInterface>();
    }
  }

}
