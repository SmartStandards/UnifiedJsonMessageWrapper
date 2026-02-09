using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Net.Http;

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
    [TestMethod]
    public void JsonSerializerTest() {


      MockPapa mock1 = new MockPapa();
      mock1.MyDict.Add("chamelKey", "value1");
      mock1.MyDict.Add("PascalKey", "value2");
      mock1.MyDict.Add("Int32", 123);
      mock1.MyDict.Add("Int64", 123L);
      mock1.MyDict.Add("Date", DateTime.Now);
      mock1.Nested.MyDict.Add("chamelKey", "value1");
      mock1.Nested.MyDict.Add("PascalKey", "value2");
      mock1.Nested.MyDict.Add("Object", new MockKind());


      var jss = new JsonSerializerSettings();

      jss.TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple;
      jss.TypeNameHandling = TypeNameHandling.Auto;


      jss.Formatting = Formatting.Indented;
      jss.DateFormatHandling = DateFormatHandling.IsoDateFormat;

      //var x = new PascalCasePropertyNamesContractResolver();

      CamelCasePropertyNamesContractResolver resolver = new CamelCasePropertyNamesContractResolver();
      resolver.NamingStrategy.ProcessDictionaryKeys = false;

      jss.ContractResolver = resolver;

      string rawJson = JsonConvert.SerializeObject(mock1, jss);
      MockPapa deserialized = JsonConvert.DeserializeObject<MockPapa>(rawJson, jss);

      deserialized.MyDict.TryGetValue("Int32", out object chamelValue);
      string falschInt64 = chamelValue.GetType().FullName;

      Assert.IsTrue(rawJson.Contains("PascalKey"));

    }

    private class MockPapa : MockBasis {

      public int Bar { get; set; } = 123;
      public MockKind Nested { get; set; } = new MockKind();
      public Dictionary<string,object > MyDict { get; set; } = new Dictionary<string, object>();

      public MockBasis[] EinArray { get; set; }= new MockBasis[] {
        new MockBasis(),
        new MockKind()
      };

  }
    private class MockKind : MockBasis {
      public bool Baz { get; set; } = true;

      public Dictionary<string, object> MyDict { get; set; } = new Dictionary<string, object>();
    }

    private class MockBasis {

      public string Foo { get; set; } = "Huhu";

    }


  }

 }
