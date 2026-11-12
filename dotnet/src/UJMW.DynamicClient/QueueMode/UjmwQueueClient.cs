using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace System.Web.UJMW {

  /// <summary>
  /// Remote implementation of 'IUjmwQueue' (processor side) which talks to the '&lt;route&gt;/_queue' endpoint of a
  /// UJMW queue host via HTTP (GET = info / dequeue, PUT = heartbeat, DELETE = return, POST = completion).
  /// The controller base URL is resolved per contract type via the 'urlGetter' ("/_queue" is appended);
  /// without explicit getters the defaults of 'UjmwClientConfiguration' are used
  /// (see 'doc/epics/epic-async-queue-mode.md', 9.3).
  /// </summary>
  public class UjmwQueueClient : IUjmwQueue {

    private readonly Func<Type, string> _UrlGetter;
    private readonly Func<Type, string> _AuthHeaderGetter;
    private readonly IHttpRequestExecutor _RequestExecutor;
    private readonly HttpClient _HttpClient;

    /// <summary>
    /// Creates a queue client.
    /// </summary>
    /// <param name="urlGetter">Contract type =&gt; controller base URL (null = 'UjmwClientConfiguration.DefaultUrlGetter').</param>
    /// <param name="authHeaderGetter">Contract type =&gt; 'Authorization' header (null = 'UjmwClientConfiguration.DefaultAuthHeaderGetter').</param>
    public UjmwQueueClient(Func<Type, string> urlGetter = null, Func<Type, string> authHeaderGetter = null) : this(urlGetter, authHeaderGetter, null) {
    }

    /// <summary>
    /// Creates a queue client using a custom transport (for example for tests or special HTTP stacks).
    /// If 'requestExecutor' is given, the auth header is passed to it as request header 'Authorization'.
    /// </summary>
    public UjmwQueueClient(Func<Type, string> urlGetter, Func<Type, string> authHeaderGetter, IHttpRequestExecutor requestExecutor) {

      if (urlGetter == null) {
        urlGetter = (t) => UjmwClientConfiguration.DefaultUrlGetter.Invoke(t);
      }

      if (authHeaderGetter == null) {
        authHeaderGetter = (t) => UjmwClientConfiguration.DefaultAuthHeaderGetter.Invoke(t);
      }

      _UrlGetter = urlGetter;
      _AuthHeaderGetter = authHeaderGetter;
      _RequestExecutor = requestExecutor;

      if (requestExecutor == null) {
        _HttpClient = UjmwClientConfiguration.HttpClientFactory.Invoke(null);
      }

    }

    #region " IUjmwQueue "

    /// <summary>
    /// GET &lt;route&gt;/_queue
    /// </summary>
    public UjmwQueueInfo GetInfo(Type contractType, string queueName) {

      string content = this.Send(contractType, "GET", string.Empty, null);

      return UjmwQueueWire.ParseInfo(content);
    }

    /// <summary>
    /// GET &lt;route&gt;/_queue?processor=..&amp;dequeue=..
    /// </summary>
    public UjmwQueueLease Dequeue(Type contractType, string queueName, string processorId, UjmwQueueMethodFilter methodFilter, int capacity) {

      if (methodFilter == null) {
        methodFilter = UjmwQueueMethodFilter.All;
      }

      string query = BuildQuery(
        UjmwQueueProtocol.ProcessorQueryParam, processorId,
        UjmwQueueProtocol.DequeueQueryParam, methodFilter.ToString(),
        UjmwQueueProtocol.CapacityQueryParam, FormatCapacity(capacity)
      );

      string content = this.Send(contractType, "GET", query, null);

      return UjmwQueueWire.ParseLease(content);
    }

    /// <summary>
    /// PUT &lt;route&gt;/_queue?processor&amp;requestId&amp;pollingSalt&amp;pollingHash
    /// </summary>
    public UjmwQueueHeartbeatResult Heartbeat(
      Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash,
      string callbackInvocationsJson
    ) {

      string query = BuildLeaseQuery(processorId, requestId, pollingSalt, pollingHash);

      string body = callbackInvocationsJson;
      if (string.IsNullOrWhiteSpace(body)) {
        body = "{}";
      }

      string content = this.Send(contractType, "PUT", query, body);

      return UjmwQueueWire.ParseHeartbeatResult(content);
    }

    /// <summary>
    /// DELETE &lt;route&gt;/_queue?processor&amp;requestId&amp;pollingSalt&amp;pollingHash
    /// </summary>
    public UjmwQueueResult Return(Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash) {

      string query = BuildLeaseQuery(processorId, requestId, pollingSalt, pollingHash);
      string content = this.Send(contractType, "DELETE", query, null);

      return UjmwQueueWire.ParseResult(content);
    }

    /// <summary>
    /// POST &lt;route&gt;/_queue?processor&amp;requestId&amp;pollingSalt&amp;pollingHash
    /// </summary>
    public UjmwQueueLease Complete(
      Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash,
      string responseJson, UjmwQueueMethodFilter dequeueNext, int capacity
    ) {

      string query = BuildLeaseQuery(processorId, requestId, pollingSalt, pollingHash);

      string capacityValue = FormatCapacity(capacity);
      if (capacityValue != null) {

        string separator = "&";
        if (query.Length == 0) {
          separator = "?";
        }

        query = query + separator + UjmwQueueProtocol.CapacityQueryParam + "=" + capacityValue;
      }

      string body = UjmwQueueWire.BuildCompletionBody(responseJson, dequeueNext);
      string content = this.Send(contractType, "POST", query, body);

      return UjmwQueueWire.ParseLease(content);
    }

    #endregion

    #region " Transport "

    /// <summary>
    /// Builds the URL of the '_queue' endpoint for the given contract type.
    /// </summary>
    public string GetQueueUrl(Type contractType) {

      string baseUrl = _UrlGetter.Invoke(contractType);
      if (string.IsNullOrWhiteSpace(baseUrl)) {
        throw new InvalidOperationException($"No URL configured for the queue of contract '{contractType.Name}'.");
      }

      return baseUrl.TrimEnd('/') + "/" + UjmwQueueProtocol.QueueRouteSegment;
    }

    private string Send(Type contractType, string httpMethod, string query, string body) {

      string url = this.GetQueueUrl(contractType) + query;
      string authHeader = _AuthHeaderGetter.Invoke(contractType);

      int statusCode;
      string responseContent;
      string reasonPhrase;

      if (_RequestExecutor != null) {

        Dictionary<string, string> headers = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(authHeader)) {
          headers["Authorization"] = authHeader;
        }

        IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders;
        statusCode = _RequestExecutor.ExecuteHttpRequest(httpMethod, url, body, headers, out responseContent, out responseHeaders, out reasonPhrase);

      }
      else {

        using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(httpMethod), url)) {

          if (body != null) {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
          }

          if (!string.IsNullOrWhiteSpace(authHeader)) {
            request.Headers.TryAddWithoutValidation("Authorization", authHeader);
          }

          using (HttpResponseMessage response = _HttpClient.SendAsync(request).GetAwaiter().GetResult()) {
            statusCode = (int)response.StatusCode;
            reasonPhrase = response.ReasonPhrase;
            responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
          }

        }

      }

      if (statusCode < 200 || statusCode > 299) {
        throw new UjmwQueueTransportException(statusCode, $"UJMW queue request '{httpMethod} {url}' failed with HTTP {statusCode} ({reasonPhrase}): {responseContent}");
      }

      return responseContent;
    }

    private static string FormatCapacity(int capacity) {

      if (capacity < 1) {
        return null;
      }

      return capacity.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string BuildLeaseQuery(string processorId, string requestId, string pollingSalt, string pollingHash) {
      return BuildQuery(
        UjmwQueueProtocol.ProcessorQueryParam, processorId,
        UjmwQueueProtocol.RequestIdQueryParam, requestId,
        UjmwQueueProtocol.PollingSaltQueryParam, pollingSalt,
        UjmwQueueProtocol.PollingHashQueryParam, pollingHash
      );
    }

    private static string BuildQuery(params string[] namesAndValues) {

      StringBuilder query = new StringBuilder();

      for (int i = 0; i + 1 < namesAndValues.Length; i += 2) {

        if (namesAndValues[i + 1] == null) {
          continue;
        }

        if (query.Length == 0) {
          query.Append('?');
        }
        else {
          query.Append('&');
        }

        query.Append(Uri.EscapeDataString(namesAndValues[i]));
        query.Append('=');
        query.Append(Uri.EscapeDataString(namesAndValues[i + 1]));
      }

      return query.ToString();
    }

    #endregion

  }

  /// <summary>
  /// Thrown by the 'UjmwQueueClient' for non-success HTTP status codes (for example 401 / 403 of the auth hooks).
  /// </summary>
  public class UjmwQueueTransportException : Exception {

    private readonly int _HttpStatusCode;

    public UjmwQueueTransportException(int httpStatusCode, string message) : base(message) {
      _HttpStatusCode = httpStatusCode;
    }

    /// <summary>
    /// The HTTP status code returned by the queue host.
    /// </summary>
    public int HttpStatusCode {
      get {
        return _HttpStatusCode;
      }
    }

  }

}
