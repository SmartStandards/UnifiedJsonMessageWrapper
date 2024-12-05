using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

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

    private string _CachedAuthHeader = null;
    private DateTime _AuthHeaderCacheTime = DateTime.MinValue;

    public int ExecuteHttpPost(
      string url,
      string requestContent,
      IDictionary<string, string> requestHeaders,
      out string responseContent,
      out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders,
      out string reasonPhrase
    ) {

      if (_AuthHeaderGetter != null) {
        if (_AuthHeaderCacheTime < DateTime.Now) {
          _CachedAuthHeader = _AuthHeaderGetter.Invoke();
          _AuthHeaderCacheTime = DateTime.Now.AddSeconds(
            UjmwClientConfiguration.AuthHeaderGetterCacheSec
          );
        }
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

        if (!string.IsNullOrWhiteSpace(_CachedAuthHeader)) {
          request.Headers.Add("Authorization", _CachedAuthHeader);
        }

        try {
         
          Task<HttpResponseMessage> requestTask = _HttpClient.SendAsync(request);
          requestTask.Wait();

          Task<string> contentRetrivalTask = requestTask.Result.Content.ReadAsStringAsync();
          contentRetrivalTask.Wait();
          responseContent = contentRetrivalTask.Result;

          responseHeaders = requestTask.Result.Headers;
          reasonPhrase = requestTask.Result.ReasonPhrase;

          return (int)requestTask.Result.StatusCode;

        }
        catch (TaskCanceledException ex) {
          throw new TimeoutException($"The http call was canceled (configured timeout is {_HttpClient.Timeout.TotalSeconds}s)", ex);
        }

      }

    }

  }

}
