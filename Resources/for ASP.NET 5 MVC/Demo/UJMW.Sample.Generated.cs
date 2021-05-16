using Newtonsoft.Json;
using System;
using System.Net;

namespace MyBusinessNamespace.WebApi {
  
  public partial class  {
    
    public (string url, string apiToken) {
      
      if (!url.EndsWith("/")) {
        url = url + "/";
      }
      
      _FooClient = new FooClient(url + "foo/", apiToken);
      
    }
    
    private FooClient _FooClient = null;
    public IFoo Foo {
      get {
        return _FooClient;
      }
    }
    
  }
  
  internal partial class FooClient : IFoo {
    
    private string _Url;
    private string _ApiToken;
    private WebClient _WebClient;
    
    public FooClient(string url, string apiToken) {
      _Url = url;
      _ApiToken = apiToken;
      _WebClient = new WebClient();
      _WebClient.Headers.Set("X-API-Key", apiToken);
      _WebClient.Headers.Set("Content-Type", "application/json");
    }
    
    /// <summary> Foooo </summary>
    public Boolean Foooo(String a, out Int32 b) {
      string url = _Url + "foooo";
      var args = new FooooRequest {
        a = a,
      };
      string rawRequest = JsonConvert.SerializeObject(args);
      string rawResponse = _WebClient.UploadString(url, rawRequest);
      var result = JsonConvert.DeserializeObject<FooooResponse>(rawResponse);
      b = result.b;
      return result.@return;
    }
    
    /// <summary> Kkkkkk </summary>
    public TestModel Kkkkkk(Int32 optParamA = 0, String optParamB = "f") {
      string url = _Url + "kkkkkk";
      var args = new KkkkkkRequest {
        optParamA = optParamA,
        optParamB = optParamB
      };
      string rawRequest = JsonConvert.SerializeObject(args);
      string rawResponse = _WebClient.UploadString(url, rawRequest);
      var result = JsonConvert.DeserializeObject<KkkkkkResponse>(rawResponse);
      return result.@return;
    }
    
    /// <summary> Meth </summary>
    /// <param name="errorCode"> Bbbbbb </param>
    public void AVoid(TestModel errorCode) {
      string url = _Url + "aVoid";
      var args = new AVoidRequest {
        errorCode = errorCode
      };
      string rawRequest = JsonConvert.SerializeObject(args);
      string rawResponse = _WebClient.UploadString(url, rawRequest);
      var result = JsonConvert.DeserializeObject<AVoidResponse>(rawResponse);
      return;
    }
    
  }
  
}
