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
        else if (methodName == nameof(IHasMethodWithDataTimeAsOutParam.Foo)) {
          responseContent = "{ \"dt\": \"2000-01-01T00:00:00Z\" }";
        }
        else if (methodName == nameof(IHasMethodWithDataTimeAsOutParam.Bar)) {
          responseContent = "{ \"count\": 123 }";;
        }
        else if (methodName == nameof(IHasMethodWithDataTimeAsOutParam.GetStructViaOut)) {
          responseContent = "{ \"res\": { \"Id\": 111, \"Name\": \"StructName\" } }";
        }
        else if (methodName == nameof(IHasMethodWithDataTimeAsOutParam.GetClassViaOut)) {
          responseContent = "{ \"res\": { \"Id\": 222, \"Name\": \"ClassName\" } }";
        }
        else if (methodName == nameof(IHasMethodWithDataTimeAsOutParam.GetStructViaRef)) {
          responseContent = "{ \"res\": { \"Id\": 333, \"Name\": \"StructName\" } }";
        }
        else if (methodName == nameof(IHasMethodWithDataTimeAsOutParam.GetNullStructViaOut)) {
          responseContent = "{ \"res\": null }";
        }
        else if (methodName == nameof(IHasMethodWithDataTimeAsOutParam.GetNullClassViaOut)) {
          responseContent = "{ \"res\": null }";
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

    [TestMethod]
    public void Proxy_ShouldSupportStrcutsAsOutParam() {

      IHasMethodWithDataTimeAsOutParam proxy = DynamicClientFactory.CreateInstance<IHasMethodWithDataTimeAsOutParam>(
        new MockHttpPostSimulator(), () => dummyRootUrl
      );

      DateTime dt = new DateTime(2021,1,1);
      int count = -1;

      proxy.Bar(out count);

      proxy.GetClassViaOut(out BeautifulClass classResult);

      proxy.GetStructViaOut(out BeautifulStruct structResult);
      
      proxy.Foo(out dt);

      Assert.AreEqual(123, count);

      Assert.AreEqual(222, classResult.Id);
      Assert.AreEqual("ClassName", classResult.Name);

      Assert.AreEqual(111, structResult.Id);
      Assert.AreEqual("StructName", structResult.Name);

      Assert.AreEqual(new DateTime(2000, 1, 1), dt);

      proxy.GetStructViaRef(ref structResult);
      Assert.AreEqual(333, structResult.Id);
      Assert.AreEqual("StructName", structResult.Name);

      proxy.GetNullClassViaOut(out BeautifulClass nullClassResult);

      //EX in JSON deserializer
      //proxy.GetNullStructViaOut(out BeautifulStruct nullStructResult);

    }
    public interface IHasMethodWithDataTimeAsOutParam {  
      void Foo(out DateTime dt);
      void Bar(out int count);

      void GetStructViaOut(out BeautifulStruct res);
      void GetNullStructViaOut(out BeautifulStruct res);


      void GetClassViaOut(out BeautifulClass res);
      void GetNullClassViaOut(out BeautifulClass res);

      void GetStructViaRef(ref BeautifulStruct res);
    }
    public struct BeautifulStruct {
      public int Id { get; set; }
      public string Name { get; set; }
    }
    public class BeautifulClass {
      public int Id { get; set; }
      public string Name { get; set; }
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
