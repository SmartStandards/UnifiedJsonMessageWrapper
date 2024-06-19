using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace System.Web.UJMW {

  public interface IHttpPostExecutor {

    int ExecuteHttpPost(
      string url,
      string requestContent,
      IDictionary<string, string> requestHeaders,
      out string responseContent,
      out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders,
      out string reasonPhrase
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
      string requestContent,
      IDictionary<string, string> requestHeaders,
      out string responseContent,
      out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders,
      out string reasonPhrase
    ) {

      string authHeaderValue = null;
      if (_AuthHeaderGetter != null) {
        //always on demand, because it could be a new one after expiration...
        authHeaderValue = _AuthHeaderGetter.Invoke(); 
      }

      HttpContent content = new StringContent(requestContent, Encoding.UTF8, "application/json");
      if (requestHeaders != null) {
        foreach (var kvp in requestHeaders) {
          if (!kvp.Key.Equals("Authorization", StringComparison.CurrentCultureIgnoreCase)) {
            content.Headers.Add(kvp.Key, kvp.Value);
          }
        }
      }

      using (var request = new HttpRequestMessage(HttpMethod.Post, url)) {

        request.Content = content;

        if (!string.IsNullOrWhiteSpace(authHeaderValue)) {
          request.Headers.Add("Authorization", authHeaderValue);
        }

        var requestTask = _HttpClient.SendAsync(request);
        requestTask.Wait();

        var contentRetrivalTask = requestTask.Result.Content.ReadAsStringAsync();
        contentRetrivalTask.Wait();
        responseContent = contentRetrivalTask.Result;

        responseHeaders = requestTask.Result.Headers;
        reasonPhrase = requestTask.Result.ReasonPhrase;

        return (int)requestTask.Result.StatusCode;
      }

    }

  }

}
