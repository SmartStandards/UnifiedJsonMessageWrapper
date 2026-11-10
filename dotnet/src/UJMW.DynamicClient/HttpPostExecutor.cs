using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace System.Web.UJMW {

  public interface IHttpPostExecutor {

    /// <summary></summary>
    /// <param name="url"></param>
    /// <param name="requestContent">if requestContent==null, then a http-GET should be done!!! </param>
    /// <param name="requestHeaders"></param>
    /// <param name="responseContent"></param>
    /// <param name="responseHeaders"></param>
    /// <param name="reasonPhrase"></param>
    /// <returns></returns>
    int ExecuteHttpPost(
      string url,
      string requestContent,
      IDictionary<string, string> requestHeaders,
      out string responseContent,
      out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders,
      out string reasonPhrase
    );

  }

  /// <summary>
  /// Optional extension of 'IHttpPostExecutor' which supports explicit HTTP verbs.
  /// It is required for the long-running operation protocol (POST to initiate, GET to poll, DELETE to cancel).
  /// If a transport does not implement this interface, long-running operations fall back to a synchronous POST.
  /// </summary>
  public interface IHttpRequestExecutor {

    /// <summary>
    /// Executes a HTTP request using the given verb ('POST', 'GET' or 'DELETE').
    /// </summary>
    /// <param name="httpMethod">the HTTP verb</param>
    /// <param name="url"></param>
    /// <param name="requestContent">the request body (null for requests without content)</param>
    /// <param name="requestHeaders"></param>
    /// <param name="responseContent"></param>
    /// <param name="responseHeaders"></param>
    /// <param name="reasonPhrase"></param>
    /// <returns>the HTTP status code</returns>
    int ExecuteHttpRequest(
      string httpMethod,
      string url,
      string requestContent,
      IDictionary<string, string> requestHeaders,
      out string responseContent,
      out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders,
      out string reasonPhrase
    );

  }

  internal class WebClientBasedHttpPostExecutor : IHttpPostExecutor, IHttpRequestExecutor {

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

      string httpMethod = "POST";
      if (requestContent == null) {
        httpMethod = "GET";
      }

      return this.ExecuteHttpRequest(httpMethod, url, requestContent, requestHeaders, out responseContent, out responseHeaders, out reasonPhrase);
    }

    /// <summary>
    /// Executes a HTTP request using the given verb ('POST', 'GET' or 'DELETE').
    /// </summary>
    public int ExecuteHttpRequest(
      string httpMethod,
      string url,
      string requestContent,
      IDictionary<string, string> requestHeaders,
      out string responseContent,
      out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders,
      out string reasonPhrase
    ) {

      HttpMethod httpM = new HttpMethod(httpMethod.ToUpperInvariant());

      if (_AuthHeaderGetter != null) {
        if (_AuthHeaderCacheTime < DateTime.Now) {
          _CachedAuthHeader = _AuthHeaderGetter.Invoke();
          _AuthHeaderCacheTime = DateTime.Now.AddSeconds(
            UjmwClientConfiguration.AuthHeaderGetterCacheSec
          );
        }
      }

      HttpContent content = null;
      if (requestContent != null) {
        content = new StringContent(requestContent, Encoding.UTF8, "application/json");
        if (requestHeaders != null) {
          foreach (var kvp in requestHeaders) {
            if (!kvp.Key.Equals("Authorization", StringComparison.CurrentCultureIgnoreCase)) {
              content.Headers.Add(kvp.Key, kvp.Value);
            }
          }
        }
      }
   
      using (HttpRequestMessage request = new HttpRequestMessage(httpM, url)) {

        request.Content = content;

        if (!string.IsNullOrWhiteSpace(_CachedAuthHeader)) {
          request.Headers.Add("Authorization", _CachedAuthHeader);
        }

        try {
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
          catch (AggregateException ex) {
            if (ex.InnerExceptions.Count == 1) {
              throw ex.InnerExceptions[0];
            }
            throw;
          }
        }
        catch (TaskCanceledException ex) {
          throw new TimeoutException($"The http call to '{url}' was canceled (configured timeout is {_HttpClient.Timeout.TotalSeconds}s)", ex);
        }
        catch (Exception ex) {
          throw new Exception($"The http call to '{url}' failed: {ex.Message}" , ex);
        }

      }

    }

  }

}
