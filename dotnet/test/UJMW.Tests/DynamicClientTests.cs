using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using static System.Web.UJMW.DynamicClientFactory;

namespace System.Web.UJMW {

  [TestClass]
  public class DynamicClientTests {

    internal const string dummyRootUrl = "https://dummy/";

    internal class MockHttpPostSimulator : IHttpPostExecutor {

      public MockHttpPostSimulator() {
      }

      public int ExecuteHttpPost(
        string url,
        string requestContent,
        IDictionary<string, string> requestHeaders,
        out string responseContent,
        out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders,
        out string reasonPhrase
      ) {

        string methodName = url.Substring(dummyRootUrl.Length);
        responseHeaders = null;
        if (methodName == nameof(IMyService.ThisIsAVoid)) {
          responseContent = "{\n}";
        }
        else if (methodName == nameof(IMyService.Calculate)) {
          responseContent = "{ \"_\":{ \"lastError\":null }, \"return\": 42}";
        }
        else if (methodName == nameof(IMyService.IntByRef)) {
          responseContent = "{ \"foo\": 43}";
        }
        else if (methodName == nameof(IMyService.IntOut)) {
          responseContent = "{ \"foo\": 44}";
        }
        else if (methodName == nameof(IMyService.ProcessABag)) {
          responseContent = "{ \"return\": true}";
        }
        else {
          responseContent = "{ \"fault\":\"UNKNOWN-METHOD\"}";
        }

        reasonPhrase = "OK";
        return 200;
      }
    }

    private void CaptureMockAmbientData(IDictionary<string, string> targetSnapshot) {
      targetSnapshot["Tenant"] = "NASA";
    }

    [TestMethod]
    public void DynamicClientTest1() {

      UjmwClientConfiguration.ConfigureRequestSidechannel((t, sideChannel) => {
        sideChannel.ProvideUjmwUnderlineProperty();
        sideChannel.CaptureDataVia(this.CaptureMockAmbientData);
      });

      var client = DynamicClientFactory.CreateInstance<IMyService>(new MockHttpPostSimulator(), () => dummyRootUrl);

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
