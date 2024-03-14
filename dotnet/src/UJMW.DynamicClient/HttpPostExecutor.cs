using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace System.Web.UJMW {

  public interface IHttpPostExecutor {

    int ExecuteHttpPost(
      string url,
      string requestContent, IDictionary<string, string> requestHeaders,
      out string responseContent, out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders
    );

  }

  internal class WebClientBasedHttpPostExecutor : IHttpPostExecutor {

    private HttpClient _HttpClient;
    private Func<string> _AuthHeaderGetter;
    public WebClientBasedHttpPostExecutor(HttpClient httpClient, Func<string> authHeaderGetter = null) {
      _HttpClient = httpClient;
      _AuthHeaderGetter = authHeaderGetter;
    }

    public int ExecuteHttpPost(
      string url,
      string requestContent, IDictionary<string, string> requestHeaders,
      out string responseContent, out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders
    ) {

      HttpContent content = new StringContent(requestContent, Encoding.UTF8, "application/json");
      if (requestHeaders != null) {
        foreach (var kvp in requestHeaders) {
          content.Headers.Add(kvp.Key, kvp.Value);
        }
      }
      if(_AuthHeaderGetter != null) {
        //always on demand, because it could be a new one after expiration...
        string authHeaderValue = _AuthHeaderGetter.Invoke();
        if (!string.IsNullOrWhiteSpace(authHeaderValue)) {
          _HttpClient.DefaultRequestHeaders.Remove("Authorization");
          _HttpClient.DefaultRequestHeaders.Add("Authorization", authHeaderValue);
        }
      }
      var task = _HttpClient.PostAsync(url, content);
      task.Wait();
      var task2 = task.Result.Content.ReadAsStringAsync();
      task2.Wait();
      responseContent = task2.Result;
      responseHeaders = task.Result.Headers;
      return (int)task.Result.StatusCode;
    }

  }

}
