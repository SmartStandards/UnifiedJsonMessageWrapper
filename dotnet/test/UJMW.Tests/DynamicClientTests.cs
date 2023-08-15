using Microsoft.VisualStudio.TestTools.UnitTesting;
using static System.Web.UJMW.DynamicClientFactory;

namespace System.Web.UJMW {

  [TestClass]
  public class DynamicClientTests {

    internal const string dummyRootUrl = "https://dummy/";

    [TestMethod]
    public void DynamicClientTest1() {

      HttpPostMethod mockHttpCaller = (
        (string targetUrl, string rawJsonContent) => {
          string methodName = targetUrl.Substring(dummyRootUrl.Length);
          if (methodName == nameof(IMyService.ThisIsAVoid)) {
            return "{\n}";
          }
          else if (methodName == nameof(IMyService.Calculate)) {
            return "{ \"_\":{ \"lastError\":null }, \"return\": 42}";
          }
          else if (methodName == nameof(IMyService.IntByRef)) {
            return "{ \"foo\": 43}";
          }
          else if (methodName == nameof(IMyService.IntOut)) {
            return "{ \"foo\": 44}";
          }
          else if (methodName == nameof(IMyService.ProcessABag)) {
            return "{ \"return\": true}";
          }
          return "{ \"fault\":\"UNKNOWN-METHOD\"}";
        }
      );

      var client = DynamicClientFactory.CreateInstance<IMyService>(
        mockHttpCaller,
        () => dummyRootUrl,
        (outChannel) => {
          outChannel["Tenant"] = "NASA";
        },
        (retChannel) => {
        }
       );

      var result = client.Calculate( 1, 2);
      Assert.AreEqual(42,result);

      Assert.IsTrue(client.ProcessABag(new BeautifulPropertyBag()));

      int foo1 = 33;
      client.IntByRef(ref  foo1);
      Assert.AreEqual(43, foo1);

      client.IntOut(out int foo);
      Assert.AreEqual(44, foo);

    }

  }

}
