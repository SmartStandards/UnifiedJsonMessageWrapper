using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace UJMW.TestClient.Invocation {

  /// <summary>
  /// UJMW long-running operations (wire protocol according to 'doc/epics/epic-long-running-operations.md'):
  /// 'POST ?wait' initiates, 'GET' polls (respecting 'pollNbf') and 'DELETE' requests the cancellation.
  /// Servers which do not know the protocol simply ignore 'wait' and answer synchronously (no 'async' property).
  /// </summary>
  internal static partial class UjmwInvoker {

    /// <summary>
    /// The 'wait' wish (seconds) for the initiating POST and the cancelling DELETE.
    /// </summary>
    private const int _WaitSeconds = 2;

    private const string _AsyncPropertyName = "async";
    private const string _CallbackInvokesSuffix = "Invokes";
    private const string _CallbackSequencePropertyName = "#";
    private const string _CallbackArgumentPrefix = "arg";
    private const string _AlphanumericChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int _PollingSaltLength = 6;
    private const int _DefaultPollDelayMilliseconds = 1000;

    /// <summary>
    /// Invokes a long-running operation and keeps polling until it terminated. Intermediate states are reported
    /// via the given execution handle, the final state is part of the returned result ('AsyncUpdate').
    /// Never throws for expected failures - they are reported as result of kind 'Error'.
    /// </summary>
    public static InvocationResult InvokeLongRunning(
      string baseUrl, string methodName, string authHeader, string sideChannelText, string zone,
      string requestText, int timeoutSeconds, LongRunningExecution execution
    ) {

      if (timeoutSeconds < 1) {
        timeoutSeconds = 1;
      }

      string requestBody;
      string preparationError;
      string resolvedSideChannel = ReplaceZonePlaceholder(sideChannelText, zone);
      if (!TryPrepareRequestBody(requestText, resolvedSideChannel, out requestBody, out preparationError)) {
        return InvocationResult.Create(InvocationResult._KindError, preparationError);
      }

      string url = CombineUrl(baseUrl, methodName);
      TimeSpan requestTimeout = TimeSpan.FromSeconds(timeoutSeconds + _WaitSeconds);
      StringBuilder transcript = new StringBuilder();
      List<string> callbackLog = new List<string>();
      Stopwatch overallStopwatch = Stopwatch.StartNew();
      int requestCount = 1;

      WireResponse response = SendWireRequest(HttpMethod.Post, url + "?wait=" + _WaitSeconds.ToString(CultureInfo.InvariantCulture), requestBody, authHeader, requestTimeout);

      string asyncId = null;
      string pollingSecret = null;
      bool cancellationSent = false;

      while (true) {

        transcript.AppendLine(response.Describe());

        if (response.TransportError != null) {
          return BuildErrorResult(transcript, callbackLog, response.TransportError, response.StatusCode);
        }

        if (!response.IsSuccess) {
          return BuildErrorResult(transcript, callbackLog, response.Body, response.StatusCode);
        }

        if (response.Json == null) {
          return BuildErrorResult(transcript, callbackLog, "The response is no valid UJMW JSON (" + response.ParseError + "):\r\n\r\n" + response.Body, response.StatusCode);
        }

        JArray invocations = ExtractCallbackInvocations(response.Json);
        AppendCallbackLog(callbackLog, invocations);

        JObject asyncState = response.Json.GetValue(_AsyncPropertyName, StringComparison.OrdinalIgnoreCase) as JObject;
        if (asyncState == null) {

          //the server does not support long-running operations (it ignored 'wait' and answered synchronously)
          transcript.AppendLine("(no 'async' state - the server answered synchronously)");
          return BuildFinalResult(transcript, callbackLog, response, null);
        }

        int execState = ReadInt(asyncState, "execState", 1);
        bool cancellable = ReadBool(asyncState, "cancellable");

        string currentAsyncId = ReadString(asyncState, "asyncId");
        if (!string.IsNullOrEmpty(currentAsyncId)) {
          asyncId = currentAsyncId;
        }

        string currentPollingSecret = ReadString(asyncState, "pollingSecret");
        if (!string.IsNullOrEmpty(currentPollingSecret)) {
          pollingSecret = currentPollingSecret;
        }

        if (IsTerminalExecState(execState)) {

          JObject finalUpdate = BuildUpdate(execState, false, -1, cancellationSent, requestCount, overallStopwatch, invocations);
          return BuildFinalResult(transcript, callbackLog, response, finalUpdate);
        }

        if (string.IsNullOrEmpty(asyncId) || string.IsNullOrEmpty(pollingSecret)) {
          return BuildErrorResult(transcript, callbackLog, "The operation is still active, but the server did not provide 'asyncId' and 'pollingSecret' - polling is impossible.", response.StatusCode);
        }

        int delayMilliseconds = GetPollDelayMilliseconds(asyncState);
        if (execution.FixedPollIntervalMs >= 0) {
          delayMilliseconds = execution.FixedPollIntervalMs;
        }

        JObject intermediateUpdate = BuildUpdate(execState, cancellable, delayMilliseconds, cancellationSent, requestCount, overallStopwatch, invocations);
        intermediateUpdate["log"] = ComposeText(transcript, callbackLog, "(operation still active - polling...)");
        execution.ReportUpdate(intermediateUpdate);

        bool sendCancellation = (execution.CancellationRequested && !cancellationSent);
        if (!sendCancellation) {

          execution.WaitForNextPoll(TimeSpan.FromMilliseconds(delayMilliseconds));
          sendCancellation = (execution.CancellationRequested && !cancellationSent);
        }

        string pollingSalt = RandomNumberGenerator.GetString(_AlphanumericChars, _PollingSaltLength);
        string query = (
          "?asyncId=" + Uri.EscapeDataString(asyncId) +
          "&pollingSalt=" + pollingSalt +
          "&pollingHash=" + ComputePollingHash(pollingSecret, pollingSalt)
        );

        requestCount++;
        if (sendCancellation) {

          cancellationSent = true;
          execution.MarkCancellationSent();
          response = SendWireRequest(HttpMethod.Delete, url + query + "&wait=" + _WaitSeconds.ToString(CultureInfo.InvariantCulture), null, authHeader, requestTimeout);
        } else {
          response = SendWireRequest(HttpMethod.Get, url + query, null, authHeader, requestTimeout);
        }

      }

    }

    /// <summary>
    /// Returns true for the terminal execution states completed (2), failed (3) and cancelled (-2).
    /// </summary>
    private static bool IsTerminalExecState(int execState) {
      return (execState == 2 || execState == 3 || execState == -2);
    }

    /// <summary>
    /// Computes 'Base64Url(SHA-256(UTF-8(pollingSecret + pollingSalt)))' without padding.
    /// </summary>
    private static string ComputePollingHash(string pollingSecret, string pollingSalt) {

      byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(pollingSecret + pollingSalt));

      return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Returns the delay until 'pollNbf' (the default delay, if the server did not provide it).
    /// </summary>
    private static int GetPollDelayMilliseconds(JObject asyncState) {

      JToken pollNbfToken = asyncState.GetValue("pollNbf", StringComparison.OrdinalIgnoreCase);
      if (pollNbfToken == null) {
        return _DefaultPollDelayMilliseconds;
      }

      DateTime pollNbf;
      if (pollNbfToken.Type == JTokenType.Date) {
        pollNbf = pollNbfToken.Value<DateTime>().ToUniversalTime();
      } else if (pollNbfToken.Type == JTokenType.String) {
        if (!DateTime.TryParse(pollNbfToken.Value<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out pollNbf)) {
          return _DefaultPollDelayMilliseconds;
        }
      } else {
        return _DefaultPollDelayMilliseconds;
      }

      double delay = (pollNbf - DateTime.UtcNow).TotalMilliseconds;
      if (delay < 0) {
        return 0;
      }

      return (int)Math.Ceiling(delay);
    }

    /// <summary>
    /// Collects all callback invocations ('&lt;name&gt;Invokes' arrays) of a response in global '#' order.
    /// Each entry is '{ callback, seq, args: [...] }'.
    /// </summary>
    private static JArray ExtractCallbackInvocations(JObject responseObject) {

      List<JObject> invocations = new List<JObject>();

      foreach (JProperty property in responseObject.Properties()) {

        JArray entries = property.Value as JArray;
        if (entries == null || property.Name.Length <= _CallbackInvokesSuffix.Length || !property.Name.EndsWith(_CallbackInvokesSuffix, StringComparison.Ordinal)) {
          continue;
        }

        string callbackName = property.Name.Substring(0, property.Name.Length - _CallbackInvokesSuffix.Length);
        foreach (JToken entryToken in entries) {

          JObject entry = entryToken as JObject;
          if (entry == null) {
            continue;
          }

          //null arguments may be omitted by the server's serializer (e.g. 'NullValueHandling.Ignore'),
          //so the highest present position determines the length and gaps are filled with null
          int argumentCount = 0;
          foreach (JProperty argumentProperty in entry.Properties()) {

            int argumentPosition;
            if (
              argumentProperty.Name.StartsWith(_CallbackArgumentPrefix, StringComparison.Ordinal) &&
              int.TryParse(argumentProperty.Name.Substring(_CallbackArgumentPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out argumentPosition) &&
              argumentPosition > argumentCount
            ) {
              argumentCount = argumentPosition;
            }

          }

          JArray arguments = new JArray();
          for (int position = 1; position <= argumentCount; position++) {

            JToken argument = entry[_CallbackArgumentPrefix + position.ToString(CultureInfo.InvariantCulture)];
            if (argument == null) {
              argument = JValue.CreateNull();
            }

            arguments.Add(argument);
          }

          JObject invocation = new JObject();
          invocation["callback"] = callbackName;
          invocation["seq"] = ReadLong(entry, _CallbackSequencePropertyName);
          invocation["args"] = arguments;
          invocations.Add(invocation);
        }

      }

      return new JArray(invocations.OrderBy((i) => i.Value<long>("seq")).ToArray());
    }

    /// <summary>
    /// Appends a readable line per callback invocation to the log (shown within the result text).
    /// </summary>
    private static void AppendCallbackLog(List<string> callbackLog, JArray invocations) {

      foreach (JToken invocation in invocations) {

        string[] arguments = invocation["args"].Select((a) => a.ToString(Formatting.None)).ToArray();
        callbackLog.Add("#" + invocation.Value<long>("seq").ToString(CultureInfo.InvariantCulture) + " " + invocation.Value<string>("callback") + "(" + string.Join(", ", arguments) + ")");
      }

    }

    /// <summary>
    /// Builds the state update which is delivered to the UI (camelCase JSON).
    /// </summary>
    private static JObject BuildUpdate(int execState, bool cancellable, int nextPollInMs, bool cancellationSent, int requestCount, Stopwatch overallStopwatch, JArray invocations) {

      JObject update = new JObject();
      update["execState"] = execState;
      update["cancellable"] = cancellable;
      update["nextPollInMs"] = nextPollInMs;
      update["cancellationSent"] = cancellationSent;
      update["requestCount"] = requestCount;
      update["elapsedMs"] = overallStopwatch.ElapsedMilliseconds;
      update["invocations"] = invocations;

      return update;
    }

    /// <summary>
    /// Builds the final result: request transcript, all callback invocations and the formatted last response.
    /// </summary>
    private static InvocationResult BuildFinalResult(StringBuilder transcript, List<string> callbackLog, WireResponse response, JObject finalUpdate) {

      string kind = InvocationResult._KindSuccess;
      if (HasFault(response.Json)) {
        kind = InvocationResult._KindFault;
      }

      InvocationResult result = InvocationResult.Create(kind, ComposeText(transcript, callbackLog, response.Json.ToString(Formatting.Indented)));
      result.StatusCode = response.StatusCode;
      result.AsyncUpdate = finalUpdate;

      return result;
    }

    /// <summary>
    /// Builds an error result (the transcript shows which request failed).
    /// </summary>
    private static InvocationResult BuildErrorResult(StringBuilder transcript, List<string> callbackLog, string message, int statusCode) {

      InvocationResult result = InvocationResult.Create(InvocationResult._KindError, ComposeText(transcript, callbackLog, message));
      result.StatusCode = statusCode;

      return result;
    }

    /// <summary>
    /// Joins transcript, callback log and body to the result text.
    /// </summary>
    private static string ComposeText(StringBuilder transcript, List<string> callbackLog, string body) {

      StringBuilder text = new StringBuilder();
      text.Append(transcript.ToString());
      text.AppendLine();

      if (callbackLog.Count > 0) {
        text.AppendLine("Callback invocations:");
        foreach (string line in callbackLog) {
          text.AppendLine("  " + line);
        }
        text.AppendLine();
      }

      text.Append(body);

      return text.ToString();
    }

    private static string ReadString(JObject source, string propertyName) {

      JToken token = source.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
      if (token == null || token.Type == JTokenType.Null) {
        return null;
      }

      return token.ToString();
    }

    private static int ReadInt(JObject source, string propertyName, int defaultValue) {

      JToken token = source.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
      if (token == null || token.Type != JTokenType.Integer) {
        return defaultValue;
      }

      return token.Value<int>();
    }

    private static long ReadLong(JObject source, string propertyName) {

      JToken token = source.GetValue(propertyName, StringComparison.Ordinal);
      if (token == null || token.Type != JTokenType.Integer) {
        return 0;
      }

      return token.Value<long>();
    }

    private static bool ReadBool(JObject source, string propertyName) {

      JToken token = source.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
      if (token == null || token.Type != JTokenType.Boolean) {
        return false;
      }

      return token.Value<bool>();
    }

    /// <summary>
    /// Sends a single wire request. Never throws for expected failures (see 'TransportError').
    /// </summary>
    private static WireResponse SendWireRequest(HttpMethod verb, string url, string body, string authHeader, TimeSpan timeout) {

      WireResponse wire = new WireResponse(verb.Method, url);
      Stopwatch stopwatch = Stopwatch.StartNew();

      try {
        using (CancellationTokenSource timeoutSource = new CancellationTokenSource(timeout))
        using (HttpRequestMessage request = new HttpRequestMessage(verb, url)) {

          if (body != null) {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
          }

          if (!string.IsNullOrWhiteSpace(authHeader)) {
            request.Headers.TryAddWithoutValidation("Authorization", authHeader.Trim());
          }

          using (HttpResponseMessage response = _HttpClient.Send(request, timeoutSource.Token)) {
            wire.StatusCode = (int)response.StatusCode;
            wire.ReasonPhrase = response.ReasonPhrase;
            wire.IsSuccess = response.IsSuccessStatusCode;
            wire.Body = ReadBody(response);
          }

        }
      } catch (HttpRequestException ex) {
        wire.TransportError = "Transport failure: " + ex.Message;
      } catch (OperationCanceledException) {
        wire.TransportError = "Timeout after " + timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " seconds.";
      } catch (UriFormatException ex) {
        wire.TransportError = "Invalid URL '" + url + "': " + ex.Message;
      } catch (InvalidOperationException ex) {
        wire.TransportError = "Invalid URL '" + url + "': " + ex.Message;
      }

      stopwatch.Stop();
      wire.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

      if (wire.TransportError == null && wire.IsSuccess) {
        try {
          wire.Json = JToken.Parse(wire.Body) as JObject;
          if (wire.Json == null) {
            wire.ParseError = "no JSON object";
          }
        } catch (JsonReaderException ex) {
          wire.ParseError = ex.Message;
        }
      }

      return wire;
    }

    /// <summary>
    /// Raw outcome of a single wire request.
    /// </summary>
    private sealed class WireResponse {

      private readonly string _Verb;
      private readonly string _Url;
      private int _StatusCode = 0;
      private string _ReasonPhrase = string.Empty;
      private bool _IsSuccess = false;
      private string _Body = string.Empty;
      private string _TransportError = null;
      private string _ParseError = null;
      private JObject _Json = null;
      private long _ElapsedMilliseconds = 0;

      public WireResponse(string verb, string url) {

        _Verb = verb;
        _Url = url;

      }

      public int StatusCode {
        get {
          return _StatusCode;
        }
        set {
          _StatusCode = value;
        }
      }

      public string ReasonPhrase {
        get {
          return _ReasonPhrase;
        }
        set {
          _ReasonPhrase = value;
        }
      }

      public bool IsSuccess {
        get {
          return _IsSuccess;
        }
        set {
          _IsSuccess = value;
        }
      }

      public string Body {
        get {
          return _Body;
        }
        set {
          _Body = value;
        }
      }

      /// <summary>
      /// Transport failure (no response received), otherwise null.
      /// </summary>
      public string TransportError {
        get {
          return _TransportError;
        }
        set {
          _TransportError = value;
        }
      }

      public string ParseError {
        get {
          return _ParseError;
        }
        set {
          _ParseError = value;
        }
      }

      /// <summary>
      /// The parsed response (only for successful responses with valid JSON), otherwise null.
      /// </summary>
      public JObject Json {
        get {
          return _Json;
        }
        set {
          _Json = value;
        }
      }

      public long ElapsedMilliseconds {
        get {
          return _ElapsedMilliseconds;
        }
        set {
          _ElapsedMilliseconds = value;
        }
      }

      /// <summary>
      /// One transcript line, e.g. 'GET http://... -> HTTP 200 OK (12 ms), execState 1'.
      /// </summary>
      public string Describe() {

        string line = _Verb + " " + _Url + " -> ";
        if (_TransportError != null) {
          return line + "no response (" + _ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms)";
        }

        line = line + "HTTP " + _StatusCode.ToString(CultureInfo.InvariantCulture) + " " + _ReasonPhrase + " (" + _ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms)";

        if (_Json != null) {
          JObject asyncState = _Json.GetValue(_AsyncPropertyName, StringComparison.OrdinalIgnoreCase) as JObject;
          if (asyncState != null) {
            line = line + ", execState " + ReadInt(asyncState, "execState", 1).ToString(CultureInfo.InvariantCulture);
          }
        }

        return line;
      }

    }

  }

}
