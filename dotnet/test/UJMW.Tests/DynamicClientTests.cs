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
    public void DynamicClientTest() {

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

    [TestMethod]
    public void DynamicClientTimeoutTest() {

      HttpClientHandler httpClientHandler = new HttpClientHandler();
      httpClientHandler.UseProxy = false;
      HttpClient httpClient = new HttpClient(httpClientHandler);
      httpClient.Timeout = TimeSpan.FromMilliseconds(200);

      var client = DynamicClientFactory.CreateInstance<IMyService>(
        new WebClientBasedHttpPostExecutor(httpClient),
        () => "http://localhost/nonExisitingEndpoint"
      );

      Exception catchedEx = null;
      try {
        client.Calculate(1, 2);
      }
      catch (Exception ex) {
        catchedEx = ex;   
      }

      Assert.IsNotNull(catchedEx);
      Assert.IsTrue(catchedEx.Message.Contains("timeout"));

    }

    //public interface IMyService2 {
    //  object Test(string windowsLogin, ref int returnCode);
    //}
    //[TestMethod, Ignore]
    //public void DynamicClientTimeoutTest2() {

    //  HttpClientHandler httpClientHandler = new HttpClientHandler();
    //  httpClientHandler.UseProxy = false;
    //  HttpClient httpClient = new HttpClient(httpClientHandler);
    //  httpClient.Timeout = TimeSpan.FromMilliseconds(200);

    //  var client = DynamicClientFactory.CreateInstance<IMyService2>(
    //    new WebClientBasedHttpPostExecutor(
    //      httpClient,
    //      () => "TEMP"
    //    ),
    //    () => "TEMP"
    //  );

    //  int returnCode = -1;
    //  Exception catchedEx = null;
    //  try {
    //    client.Test("Foo", ref returnCode);
    //  }
    //  catch (Exception ex) {
    //    catchedEx = ex;
    //  }

    //  Assert.IsNotNull(catchedEx);
    //  Assert.IsTrue(catchedEx.Message.Contains("timeout"));

    //}

  }

 }
