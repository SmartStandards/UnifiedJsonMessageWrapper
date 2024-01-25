using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace System.Web.UJMW {

  public interface HttpPostExecutor {

    int ExecuteHttpPost(
      string url,
      string requestContent, IDictionary<string, string> requestHeaders,
      out string responseContent, out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders
    );

  }

  internal class WebClientBasedHttpPostExecutor : HttpPostExecutor {

    private HttpClient _HttpClient;

    public WebClientBasedHttpPostExecutor(HttpClient httpClient) {
      _HttpClient = httpClient;
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
