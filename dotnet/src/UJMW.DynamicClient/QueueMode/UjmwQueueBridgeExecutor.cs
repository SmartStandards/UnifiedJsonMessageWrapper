using Logging.SmartStandards.CopyForUJMW;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace System.Web.UJMW {

  /// <summary>
  /// In-process transport of the UJMW async queue mode: implements 'IHttpPostExecutor' and 'IHttpRequestExecutor'
  /// for a dynamic client proxy, so that the regular UJMW client protocol (POST / POST ?wait / GET poll / DELETE cancel)
  /// is mapped onto the clear method calls of an 'IUjmwQueuingService'
  /// (see 'doc/epics/epic-async-queue-mode.md', 4.3).
  /// The proxy must be created with a synthetic URL (see 'BuildBridgeUrl').
  /// </summary>
  public class UjmwQueueBridgeExecutor : IHttpPostExecutor, IHttpRequestExecutor {

    /// <summary>
    /// Scheme of the synthetic URLs used by the bridge (diagnostic hooks may see these URLs).
    /// </summary>
    public const string BridgeUrlScheme = "ujmw-queue";

    private static readonly IEnumerable<KeyValuePair<string, IEnumerable<string>>> _NoHeaders = new KeyValuePair<string, IEnumerable<string>>[0];

    private readonly IUjmwQueuingService _QueuingService;
    private readonly Type _ContractType;
    private readonly string _QueueName;
    private TimeSpan _SynchronousCallTimeout = TimeSpan.FromSeconds(100);
    private TimeSpan _LongPollTime = TimeSpan.FromSeconds(1);
    private TimeSpan _MaxCancelWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Creates a bridge executor for the queue of the given contract type and queue name (= resolved controller route).
    /// </summary>
    public UjmwQueueBridgeExecutor(IUjmwQueuingService queuingService, Type contractType, string queueName) {

      if (queuingService == null) {
        throw new ArgumentNullException(nameof(queuingService));
      }

      if (contractType == null) {
        throw new ArgumentNullException(nameof(contractType));
      }

      _QueuingService = queuingService;
      _ContractType = contractType;
      _QueueName = queueName;

    }

    /// <summary>
    /// Maximum time a synchronous (non-'CancellationToken') call waits for its queued execution (default: 100 seconds).
    /// After this time the entry is cancelled and the caller receives the fault 'Queue processing timeout'.
    /// </summary>
    public TimeSpan SynchronousCallTimeout {
      get {
        return _SynchronousCallTimeout;
      }
      set {
        _SynchronousCallTimeout = value;
      }
    }

    /// <summary>
    /// In-process long-poll duration of a single GET (default: 1 second).
    /// </summary>
    public TimeSpan LongPollTime {
      get {
        return _LongPollTime;
      }
      set {
        _LongPollTime = value;
      }
    }

    /// <summary>
    /// Builds the synthetic root URL for a dynamic client proxy which uses this executor.
    /// </summary>
    public static string BuildBridgeUrl(Type contractType) {
      return BridgeUrlScheme + "://" + contractType.Name;
    }

    #region " IHttpPostExecutor / IHttpRequestExecutor "

    /// <summary>
    /// Executes a POST (or a GET if 'requestContent' is null).
    /// </summary>
    public int ExecuteHttpPost(
      string url, string requestContent, IDictionary<string, string> requestHeaders,
      out string responseContent, out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders, out string reasonPhrase
    ) {

      string httpMethod = "POST";
      if (requestContent == null) {
        httpMethod = "GET";
      }

      return this.ExecuteHttpRequest(httpMethod, url, requestContent, requestHeaders, out responseContent, out responseHeaders, out reasonPhrase);
    }

    /// <summary>
    /// Maps the UJMW client protocol onto the queuing service.
    /// </summary>
    public int ExecuteHttpRequest(
      string httpMethod, string url, string requestContent, IDictionary<string, string> requestHeaders,
      out string responseContent, out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders, out string reasonPhrase
    ) {

      responseHeaders = _NoHeaders;
      reasonPhrase = "OK";

      string methodName;
      Dictionary<string, string> query;
      ParseUrl(url, out methodName, out query);

      if (string.IsNullOrEmpty(methodName)) {
        //the info endpoint is answered by the regular controller itself
        responseContent = "{}";
        return 200;
      }

      string verb = httpMethod.ToUpperInvariant();

      if (verb == "POST") {

        string requestJson = MergeHeaderSideChannel(requestContent, requestHeaders);

        if (query.ContainsKey(UjmwAsyncProtocol._WaitQueryParam)) {
          responseContent = this.InitiateLongRunning(methodName, requestJson);
        }
        else {
          responseContent = this.ExecuteSynchronously(methodName, requestJson);
        }

        return 200;
      }

      string asyncId = GetQueryValue(query, UjmwAsyncProtocol._AsyncIdQueryParam);
      string pollingSalt = GetQueryValue(query, UjmwAsyncProtocol._PollingSaltQueryParam);
      string pollingHash = GetQueryValue(query, UjmwAsyncProtocol._PollingHashQueryParam);

      UjmwQueueEntryState state;
      bool authenticated;

      if (verb == "DELETE") {

        TimeSpan cancelWait = this.ParseWait(GetQueryValue(query, UjmwAsyncProtocol._WaitQueryParam));
        authenticated = _QueuingService.TryRequestCancel(_ContractType, _QueueName, asyncId, pollingSalt, pollingHash, cancelWait, out state);

      }
      else if (verb == "GET") {

        authenticated = _QueuingService.TryGetState(_ContractType, _QueueName, asyncId, pollingSalt, pollingHash, _LongPollTime, out state);

      }
      else {
        responseContent = string.Empty;
        reasonPhrase = "Method Not Allowed";
        return 405;
      }

      if (!authenticated) {
        responseContent = CreateFaultResponse(UjmwAsyncProtocol._WrongPollingAuthFault, null);
        return 200;
      }

      responseContent = CreateResponse(state, asyncId, null);
      return 200;
    }

    #endregion

    #region " Mapping "

    /// <summary>
    /// Enqueues a long-running call and returns the (non-terminal) async state.
    /// </summary>
    private string InitiateLongRunning(string methodName, string requestJson) {

      UjmwQueueTicket ticket = _QueuingService.Enqueue(_ContractType, _QueueName, methodName, requestJson, true);

      if (ticket.Fault != null) {
        return CreateFaultResponse(ticket.Fault, UjmwAsyncProtocol._ExecStateFailed);
      }

      UjmwQueueEntryState initialState = new UjmwQueueEntryState();
      initialState.ExecState = UjmwAsyncProtocol._ExecStateQueued;

      return CreateResponse(initialState, ticket.EntryId, ticket.PollingSecret);
    }

    /// <summary>
    /// Enqueues a synchronous call and blocks until the entry has terminated (or the timeout elapsed).
    /// </summary>
    private string ExecuteSynchronously(string methodName, string requestJson) {

      UjmwQueueTicket ticket = _QueuingService.Enqueue(_ContractType, _QueueName, methodName, requestJson, false);

      if (ticket.Fault != null) {
        return CreateFaultResponse(ticket.Fault, null);
      }

      DateTime deadline = DateTime.UtcNow.Add(_SynchronousCallTimeout);
      List<string> drainedCallbacks = new List<string>();

      while (true) {

        TimeSpan remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) {
          break;
        }

        if (remaining > _LongPollTime) {
          remaining = _LongPollTime;
        }

        string salt;
        string hash;
        CreateAuth(ticket.PollingSecret, out salt, out hash);

        UjmwQueueEntryState state;
        if (!_QueuingService.TryGetState(_ContractType, _QueueName, ticket.EntryId, salt, hash, remaining, out state)) {
          return CreateFaultResponse(UjmwAsyncProtocol._WrongPollingAuthFault, null);
        }

        if (state.CallbackInvocationsJson != null) {
          drainedCallbacks.Add(state.CallbackInvocationsJson);
        }

        if (UjmwAsyncProtocol.IsTerminalExecState(state.ExecState)) {
          state.CallbackInvocationsJson = CombineCallbackJson(drainedCallbacks);
          return CreateSynchronousResponse(state);
        }

      }

      string cancelSalt;
      string cancelHash;
      CreateAuth(ticket.PollingSecret, out cancelSalt, out cancelHash);

      UjmwQueueEntryState ignored;
      _QueuingService.TryRequestCancel(_ContractType, _QueueName, ticket.EntryId, cancelSalt, cancelHash, TimeSpan.Zero, out ignored);

      DevLogger.LogWarning(2091845400102, 72401, $"UJMW queue call '{methodName}' exceeded the synchronous call timeout.");

      return CreateFaultResponse(UjmwQueueProtocol.FaultProcessingTimeout, null);
    }

    /// <summary>
    /// Creates the response of a synchronous call (without 'async' object).
    /// </summary>
    private static string CreateSynchronousResponse(UjmwQueueEntryState state) {

      if (state.ExecState == UjmwAsyncProtocol._ExecStateCancelled) {
        return CreateFaultResponse("The queued operation has been cancelled", null);
      }

      JObject response = ParseResponseOrEmpty(state.ResponseJson);

      if (state.Fault != null) {
        response["fault"] = state.Fault;
      }

      MergeCallbacks(response, state.CallbackInvocationsJson);

      return response.ToString(Formatting.None);
    }

    /// <summary>
    /// Creates a UJMW response with 'async' state, drained callbacks and (once terminated) the final response.
    /// </summary>
    internal static string CreateResponse(UjmwQueueEntryState state, string asyncId, string pollingSecret) {

      JObject response;
      bool terminal = UjmwAsyncProtocol.IsTerminalExecState(state.ExecState);

      if (terminal) {
        response = ParseResponseOrEmpty(state.ResponseJson);
      }
      else {
        response = new JObject();
      }

      if (state.Fault != null) {
        response["fault"] = state.Fault;
      }

      JObject asyncState = new JObject();
      asyncState[UjmwAsyncProtocol._AsyncIdPropertyName] = asyncId;
      asyncState[UjmwAsyncProtocol._ExecStatePropertyName] = state.ExecState;
      asyncState[UjmwAsyncProtocol._CancellablePropertyName] = state.Cancellable;

      if (pollingSecret != null) {
        asyncState[UjmwAsyncProtocol._PollingSecretPropertyName] = pollingSecret;
      }

      if (!terminal) {
        //the bridge long-polls in-process, so the next poll may follow immediately
        asyncState[UjmwAsyncProtocol._PollNbfPropertyName] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
      }

      response[UjmwAsyncProtocol._AsyncPropertyName] = asyncState;

      MergeCallbacks(response, state.CallbackInvocationsJson);

      return response.ToString(Formatting.None);
    }

    /// <summary>
    /// Creates a response which only carries a fault (optionally with a terminal async state).
    /// </summary>
    private static string CreateFaultResponse(string fault, int? execState) {

      JObject response = new JObject();
      response["fault"] = fault;

      if (execState.HasValue) {
        JObject asyncState = new JObject();
        asyncState[UjmwAsyncProtocol._ExecStatePropertyName] = execState.Value;
        response[UjmwAsyncProtocol._AsyncPropertyName] = asyncState;
      }

      return response.ToString(Formatting.None);
    }

    /// <summary>
    /// Parses a UJMW response JSON (returns an empty object for null / invalid content).
    /// </summary>
    private static JObject ParseResponseOrEmpty(string responseJson) {

      if (string.IsNullOrWhiteSpace(responseJson)) {
        return new JObject();
      }

      try {
        JObject parsed = JObject.Parse(responseJson);
        parsed.Remove(UjmwAsyncProtocol._AsyncPropertyName);
        return parsed;
      }
      catch (JsonException ex) {
        DevLogger.LogError(ex);
        JObject faulted = new JObject();
        faulted["fault"] = "Invalid response JSON delivered by the queue processor";
        return faulted;
      }

    }

    /// <summary>
    /// Merges drained callback invocations ('{ "xInvokes": [...] }') into the response (in front of already contained ones).
    /// </summary>
    private static void MergeCallbacks(JObject response, string callbackInvocationsJson) {

      if (string.IsNullOrWhiteSpace(callbackInvocationsJson)) {
        return;
      }

      JObject callbacks = JObject.Parse(callbackInvocationsJson);

      foreach (JProperty property in callbacks.Properties()) {

        JArray drained = property.Value as JArray;
        if (drained == null) {
          continue;
        }

        JArray existing = response[property.Name] as JArray;
        JArray merged = new JArray();

        foreach (JToken item in drained) {
          merged.Add(item);
        }

        if (existing != null) {
          foreach (JToken item in existing) {
            merged.Add(item);
          }
        }

        response[property.Name] = merged;
      }

    }

    /// <summary>
    /// Combines several drained callback JSON objects into one.
    /// </summary>
    private static string CombineCallbackJson(List<string> drainedCallbacks) {

      if (drainedCallbacks.Count == 0) {
        return null;
      }

      JObject combined = new JObject();

      foreach (string json in drainedCallbacks) {
        MergeCallbacks(combined, json);
      }

      return combined.ToString(Formatting.None);
    }

    /// <summary>
    /// Sidechannel values which the client configuration transports as HTTP headers are moved into the
    /// '_' property, because the queue only transports the request JSON.
    /// </summary>
    private static string MergeHeaderSideChannel(string requestContent, IDictionary<string, string> requestHeaders) {

      //the original inbound snapshot of a queue-mode controller call is forwarded unchanged (no re-capturing)
      IDictionary<string, string> originalSnapshot = UjmwQueueSideChannelContext.Current;
      if (originalSnapshot != null && !string.IsNullOrWhiteSpace(requestContent)) {

        JObject requestWithSnapshot = JObject.Parse(requestContent);
        requestWithSnapshot["_"] = JObject.FromObject(originalSnapshot);

        return requestWithSnapshot.ToString(Formatting.None);
      }

      if (requestHeaders == null || requestHeaders.Count == 0 || string.IsNullOrWhiteSpace(requestContent)) {
        return requestContent;
      }

      JObject request = JObject.Parse(requestContent);
      if (request["_"] != null && request["_"].Type == JTokenType.Object) {
        return requestContent;
      }

      foreach (KeyValuePair<string, string> header in requestHeaders) {

        if (string.IsNullOrWhiteSpace(header.Value) || !header.Value.TrimStart().StartsWith("{")) {
          continue;
        }

        try {
          request["_"] = JObject.Parse(header.Value);
          return request.ToString(Formatting.None);
        }
        catch (JsonException ex) {
          DevLogger.LogError(ex);
        }

      }

      return requestContent;
    }

    #endregion

    #region " Helpers "

    private TimeSpan ParseWait(string wait) {

      double seconds;
      if (string.IsNullOrWhiteSpace(wait) || !double.TryParse(wait, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds <= 0) {
        return TimeSpan.Zero;
      }

      TimeSpan waitTime = TimeSpan.FromSeconds(seconds);
      if (waitTime > _MaxCancelWait) {
        waitTime = _MaxCancelWait;
      }

      return waitTime;
    }

    private static void CreateAuth(string secret, out string salt, out string hash) {
      salt = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSaltLength);
      hash = UjmwAsyncProtocol.ComputePollingHash(secret, salt);
    }

    private static string GetQueryValue(Dictionary<string, string> query, string name) {

      string value;
      if (query.TryGetValue(name, out value)) {
        return value;
      }

      return null;
    }

    /// <summary>
    /// Extracts the transport-level method name (last path segment) and the query parameters of a synthetic URL.
    /// </summary>
    internal static void ParseUrl(string url, out string methodName, out Dictionary<string, string> query) {

      query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      string path = url;

      int queryStart = url.IndexOf('?');
      if (queryStart >= 0) {

        path = url.Substring(0, queryStart);
        string[] pairs = url.Substring(queryStart + 1).Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string pair in pairs) {

          int separator = pair.IndexOf('=');
          if (separator < 0) {
            query[Uri.UnescapeDataString(pair)] = string.Empty;
          }
          else {
            query[Uri.UnescapeDataString(pair.Substring(0, separator))] = Uri.UnescapeDataString(pair.Substring(separator + 1));
          }

        }

      }

      int schemeEnd = path.IndexOf("://", StringComparison.Ordinal);
      if (schemeEnd >= 0) {
        path = path.Substring(schemeEnd + 3);
      }

      string[] segments = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

      if (segments.Length < 2) {
        //only the host segment => info endpoint
        methodName = null;
        return;
      }

      methodName = segments.Last();
    }

    #endregion

  }

}
