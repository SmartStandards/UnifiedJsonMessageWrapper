using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// Specification tests for the long-running operation support of the .NET dynamic client
  /// (internal POST -> GET polling -> DELETE on cancellation, callback replay and fallbacks).
  /// </summary>
  [TestClass]
  public class DynamicClientLongRunningTests {

    private const string _RootUrl = "https://dummy/";

    public interface ILongRunningClientProbe {

      int Process(CancellationToken c, int owner, out int lastId);

      void Notify(CancellationToken c, Action<int> onProgress, Action<string, bool> onDone);

      int Calc(int a);

    }

    /// <summary>
    /// A recorded HTTP request.
    /// </summary>
    internal class RecordedRequest {

      private string _HttpMethod;
      private string _Url;
      private string _Body;

      public RecordedRequest(string httpMethod, string url, string body) {
        _HttpMethod = httpMethod;
        _Url = url;
        _Body = body;
      }

      public string HttpMethod {
        get {
          return _HttpMethod;
        }
      }

      public string Url {
        get {
          return _Url;
        }
      }

      public string Body {
        get {
          return _Body;
        }
      }

      /// <summary>
      /// Returns the value of a query string parameter (or null).
      /// </summary>
      public string GetQueryValue(string name) {

        int queryStart = _Url.IndexOf('?');
        if (queryStart < 0) {
          return null;
        }

        foreach (string pair in _Url.Substring(queryStart + 1).Split('&')) {
          string[] parts = pair.Split(new char[] { '=' }, 2);
          if (parts[0] == name) {
            if (parts.Length > 1) {
              return Uri.UnescapeDataString(parts[1]);
            }
            return string.Empty;
          }
        }

        return null;
      }

    }

    /// <summary>
    /// Mock transport supporting explicit HTTP verbs. Responses are produced by a script delegate.
    /// </summary>
    internal class ScriptedVerbExecutor : IHttpPostExecutor, IHttpRequestExecutor {

      private Func<RecordedRequest, int, string> _Script;
      private List<RecordedRequest> _Requests = new List<RecordedRequest>();

      public ScriptedVerbExecutor(Func<RecordedRequest, int, string> script) {
        _Script = script;
      }

      public List<RecordedRequest> Requests {
        get {
          return _Requests;
        }
      }

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

      public int ExecuteHttpRequest(
        string httpMethod, string url, string requestContent, IDictionary<string, string> requestHeaders,
        out string responseContent, out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders, out string reasonPhrase
      ) {

        RecordedRequest request = new RecordedRequest(httpMethod, url, requestContent);
        _Requests.Add(request);
        responseContent = _Script.Invoke(request, _Requests.Count);
        responseHeaders = null;
        reasonPhrase = "OK";

        return 200;
      }

    }

    /// <summary>
    /// Mock transport which only supports the legacy POST interface.
    /// </summary>
    internal class LegacyPostOnlyExecutor : IHttpPostExecutor {

      private List<string> _Urls = new List<string>();
      private string _Response;

      public LegacyPostOnlyExecutor(string response) {
        _Response = response;
      }

      public List<string> Urls {
        get {
          return _Urls;
        }
      }

      public int ExecuteHttpPost(
        string url, string requestContent, IDictionary<string, string> requestHeaders,
        out string responseContent, out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders, out string reasonPhrase
      ) {

        _Urls.Add(url);
        responseContent = _Response;
        responseHeaders = null;
        reasonPhrase = "OK";

        return 200;
      }

    }

    private Action<TimeSpan, CancellationToken> _OriginalSleeper;
    private int _OriginalWaitSeconds;
    private List<TimeSpan> _RecordedSleeps;
    private Action<TimeSpan, CancellationToken> _SleepHook;

    [TestInitialize]
    public void Setup() {

      _OriginalSleeper = UjmwClientConfiguration.PollSleeper;
      _OriginalWaitSeconds = UjmwClientConfiguration.LongRunningWaitSeconds;
      _RecordedSleeps = new List<TimeSpan>();
      _SleepHook = null;

      UjmwClientConfiguration.PollSleeper = (delay, token) => {

        _RecordedSleeps.Add(delay);
        if (_SleepHook != null) {
          _SleepHook.Invoke(delay, token);
        }

      };

    }

    [TestCleanup]
    public void Cleanup() {

      UjmwClientConfiguration.PollSleeper = _OriginalSleeper;
      UjmwClientConfiguration.LongRunningWaitSeconds = _OriginalWaitSeconds;

    }

    private static string FutureNbf(int seconds) {
      return DateTime.UtcNow.AddSeconds(seconds).ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    private static string RunningAsync(string execState, string extra) {
      return "\"async\": { \"asyncId\": \"123\", \"execState\": " + execState + ", \"pollNbf\": \"" + FutureNbf(3) + "\", \"cancellable\": true" + extra + " }";
    }

    [TestMethod]
    public void NonLongRunningMethod_IsSentWithoutWaitQuery() {

      ScriptedVerbExecutor executor = new ScriptedVerbExecutor((r, n) => "{ \"return\": 5 }");
      ILongRunningClientProbe client = DynamicClientFactory.CreateInstance<ILongRunningClientProbe>(executor, () => _RootUrl);

      int result = client.Calc(1);

      Assert.AreEqual(5, result);
      Assert.AreEqual(1, executor.Requests.Count);
      Assert.AreEqual("POST", executor.Requests[0].HttpMethod);
      Assert.AreEqual(_RootUrl + "Calc", executor.Requests[0].Url);

    }

    [TestMethod]
    public void LongRunningMethod_SendsDefaultWaitAndExcludesTokenFromRequestDto() {

      ScriptedVerbExecutor executor = new ScriptedVerbExecutor((r, n) => "{ \"return\": 5, \"lastId\": 7 }");
      ILongRunningClientProbe client = DynamicClientFactory.CreateInstance<ILongRunningClientProbe>(executor, () => _RootUrl);

      int result = client.Process(CancellationToken.None, 77, out int lastId);

      Assert.AreEqual(5, result);
      Assert.AreEqual(7, lastId);
      Assert.AreEqual(1, executor.Requests.Count);
      Assert.AreEqual("POST", executor.Requests[0].HttpMethod);
      Assert.AreEqual("10", executor.Requests[0].GetQueryValue("wait"));

      JObject body = JObject.Parse(executor.Requests[0].Body);
      Assert.AreEqual(77, body.Value<int>("owner"));
      Assert.IsNull(body.Property("c"));
      Assert.IsNull(body.Property("lastId"));

    }

    [TestMethod]
    public void LongRunningMethod_UsesConfiguredWaitSeconds() {

      UjmwClientConfiguration.LongRunningWaitSeconds = 2;

      ScriptedVerbExecutor executor = new ScriptedVerbExecutor((r, n) => "{ \"return\": 5 }");
      ILongRunningClientProbe client = DynamicClientFactory.CreateInstance<ILongRunningClientProbe>(executor, () => _RootUrl);

      client.Process(CancellationToken.None, 1, out int lastId);

      Assert.AreEqual("2", executor.Requests[0].GetQueryValue("wait"));

    }

    [TestMethod]
    public void LongRunningMethod_CompletedWithinWait_DoesNotPoll() {

      ScriptedVerbExecutor executor = new ScriptedVerbExecutor(
        (r, n) => "{ \"return\": 42, \"lastId\": 3, \"fault\": null, \"async\": { \"asyncId\": null, \"execState\": 2, \"cancellable\": false } }"
      );
      ILongRunningClientProbe client = DynamicClientFactory.CreateInstance<ILongRunningClientProbe>(executor, () => _RootUrl);

      int result = client.Process(CancellationToken.None, 1, out int lastId);

      Assert.AreEqual(42, result);
      Assert.AreEqual(3, lastId);
      Assert.AreEqual(1, executor.Requests.Count);
      Assert.AreEqual(0, _RecordedSleeps.Count);

    }

    [TestMethod]
    public void LongRunningMethod_PollsWithValidHashUntilCompleted() {

      ScriptedVerbExecutor executor = new ScriptedVerbExecutor((r, n) => {

        if (n == 1) {
          return "{ " + RunningAsync("1", ", \"pollingSecret\": \"Secret01\"") + " }";
        }
        if (n == 2) {
          return "{ " + RunningAsync("1", "") + " }";
        }

        return "{ \"return\": 42, \"lastId\": 9, \"async\": { \"asyncId\": \"123\", \"execState\": 2, \"cancellable\": false } }";
      });

      ILongRunningClientProbe client = DynamicClientFactory.CreateInstance<ILongRunningClientProbe>(executor, () => _RootUrl);

      int result = client.Process(CancellationToken.None, 1, out int lastId);

      Assert.AreEqual(42, result);
      Assert.AreEqual(9, lastId);
      Assert.AreEqual(3, executor.Requests.Count);
      Assert.AreEqual("POST", executor.Requests[0].HttpMethod);

      for (int i = 1; i < 3; i++) {

        RecordedRequest poll = executor.Requests[i];
        Assert.AreEqual("GET", poll.HttpMethod);
        Assert.IsTrue(poll.Url.StartsWith(_RootUrl + "Process?"), poll.Url);
        Assert.IsNull(poll.Body);
        Assert.AreEqual("123", poll.GetQueryValue("asyncId"));

        string salt = poll.GetQueryValue("pollingSalt");
        Assert.AreEqual(6, salt.Length);
        Assert.AreEqual(UjmwAsyncProtocol.ComputePollingHash("Secret01", salt), poll.GetQueryValue("pollingHash"));

      }

      Assert.AreNotEqual(executor.Requests[1].GetQueryValue("pollingSalt"), executor.Requests[2].GetQueryValue("pollingSalt"));

      //the client has to respect the 'pollNbf' (approx. 3 seconds in the future)
      Assert.AreEqual(2, _RecordedSleeps.Count);
      Assert.IsTrue(_RecordedSleeps[0] > TimeSpan.FromSeconds(1), _RecordedSleeps[0].ToString());
      Assert.IsTrue(_RecordedSleeps[0] <= TimeSpan.FromSeconds(4), _RecordedSleeps[0].ToString());

    }

    [TestMethod]
    public void LongRunningMethod_ReplaysCallbacksInGlobalSequenceOrder() {

      ScriptedVerbExecutor executor = new ScriptedVerbExecutor((r, n) => {

        if (n == 1) {
          return "{ " +
            "\"onProgressInvokes\": [ { \"#\": 1, \"arg1\": 10 }, { \"#\": 3, \"arg1\": 30 } ], " +
            "\"onDoneInvokes\": [ { \"#\": 2, \"arg1\": \"x\", \"arg2\": true } ], " +
            RunningAsync("1", ", \"pollingSecret\": \"Secret01\"") + " }";
        }

        return "{ \"onProgressInvokes\": [ { \"#\": 4, \"arg1\": 40 } ], \"async\": { \"asyncId\": \"123\", \"execState\": 2, \"cancellable\": false } }";
      });

      ILongRunningClientProbe client = DynamicClientFactory.CreateInstance<ILongRunningClientProbe>(executor, () => _RootUrl);

      List<string> replayed = new List<string>();
      client.Notify(
        CancellationToken.None,
        (value) => replayed.Add("p" + value.ToString()),
        (text, flag) => replayed.Add("d" + text + flag.ToString())
      );

      CollectionAssert.AreEqual(new string[] { "p10", "dxTrue", "p30", "p40" }, replayed.ToArray());

      JObject body = JObject.Parse(executor.Requests[0].Body);
      Assert.IsNull(body.Property("onProgress"));
      Assert.IsNull(body.Property("onDone"));

    }

    [TestMethod]
    public void LongRunningMethod_SendsDeleteOnCancellationAndThrowsOperationCanceled() {

      CancellationTokenSource cts = new CancellationTokenSource();
      _SleepHook = (delay, token) => cts.Cancel();

      ScriptedVerbExecutor executor = new ScriptedVerbExecutor((r, n) => {

        if (r.HttpMethod == "POST") {
          return "{ " + RunningAsync("1", ", \"pollingSecret\": \"Secret01\"") + " }";
        }
        if (r.HttpMethod == "DELETE") {
          return "{ " + RunningAsync("-1", "") + " }";
        }

        return "{ \"fault\": \"cancelled\", \"async\": { \"asyncId\": \"123\", \"execState\": -2, \"cancellable\": false } }";
      });

      ILongRunningClientProbe client = DynamicClientFactory.CreateInstance<ILongRunningClientProbe>(executor, () => _RootUrl);

      bool canceledExceptionThrown = false;
      try {
        client.Process(cts.Token, 1, out int lastId);
      } catch (OperationCanceledException) {
        canceledExceptionThrown = true;
      }

      Assert.IsTrue(canceledExceptionThrown);

      int deleteCount = 0;
      foreach (RecordedRequest request in executor.Requests) {
        if (request.HttpMethod == "DELETE") {

          deleteCount++;
          Assert.AreEqual("123", request.GetQueryValue("asyncId"));
          Assert.IsNotNull(request.GetQueryValue("wait"));
          Assert.AreEqual(
            UjmwAsyncProtocol.ComputePollingHash("Secret01", request.GetQueryValue("pollingSalt")),
            request.GetQueryValue("pollingHash")
          );

        }
      }

      Assert.AreEqual(1, deleteCount);

    }

    [TestMethod]
    public void LongRunningMethod_FailedOperation_RaisesFault() {

      ScriptedVerbExecutor executor = new ScriptedVerbExecutor((r, n) => {

        if (n == 1) {
          return "{ " + RunningAsync("1", ", \"pollingSecret\": \"Secret01\"") + " }";
        }

        return "{ \"fault\": \"BOOM\", \"async\": { \"asyncId\": \"123\", \"execState\": 3, \"cancellable\": false } }";
      });

      ILongRunningClientProbe client = DynamicClientFactory.CreateInstance<ILongRunningClientProbe>(executor, () => _RootUrl);

      UjmwFaultException fault = null;
      try {
        client.Process(CancellationToken.None, 1, out int lastId);
      } catch (UjmwFaultException ex) {
        fault = ex;
      }

      Assert.IsNotNull(fault);
      Assert.IsTrue(fault.Message.Contains("BOOM"));
      Assert.AreEqual(2, executor.Requests.Count);

    }

    [TestMethod]
    public void LongRunningMethod_RejectedPollAuthentication_RaisesFault() {

      ScriptedVerbExecutor executor = new ScriptedVerbExecutor((r, n) => {

        if (n == 1) {
          return "{ " + RunningAsync("1", ", \"pollingSecret\": \"Secret01\"") + " }";
        }

        return "{ \"fault\": \"Wrong asyncId or pollingHash\" }";
      });

      ILongRunningClientProbe client = DynamicClientFactory.CreateInstance<ILongRunningClientProbe>(executor, () => _RootUrl);

      UjmwFaultException fault = null;
      try {
        client.Process(CancellationToken.None, 1, out int lastId);
      } catch (UjmwFaultException ex) {
        fault = ex;
      }

      Assert.IsNotNull(fault);
      Assert.IsTrue(fault.Message.Contains("Wrong asyncId or pollingHash"));

    }

    [TestMethod]
    public void LongRunningMethod_LegacyTransportWithoutVerbSupport_FallsBackToSynchronousPost() {

      LegacyPostOnlyExecutor executor = new LegacyPostOnlyExecutor(
        "{ \"return\": 11, \"lastId\": 12, \"onProgressInvokes\": [ { \"#\": 1, \"arg1\": 1 } ] }"
      );
      ILongRunningClientProbe client = DynamicClientFactory.CreateInstance<ILongRunningClientProbe>(executor, () => _RootUrl);

      int result = client.Process(CancellationToken.None, 1, out int lastId);

      Assert.AreEqual(11, result);
      Assert.AreEqual(12, lastId);
      Assert.AreEqual(1, executor.Urls.Count);
      Assert.AreEqual(_RootUrl + "Process", executor.Urls[0]);

      List<int> replayed = new List<int>();
      client.Notify(CancellationToken.None, (value) => replayed.Add(value), (text, flag) => { });
      Assert.AreEqual(1, replayed.Count);

    }

    [TestMethod]
    public void LongRunningMethod_AlreadyCancelledToken_ThrowsWithoutSendingRequest() {

      ScriptedVerbExecutor executor = new ScriptedVerbExecutor((r, n) => "{ \"return\": 5 }");
      ILongRunningClientProbe client = DynamicClientFactory.CreateInstance<ILongRunningClientProbe>(executor, () => _RootUrl);

      CancellationTokenSource cts = new CancellationTokenSource();
      cts.Cancel();

      bool canceledExceptionThrown = false;
      try {
        client.Process(cts.Token, 1, out int lastId);
      } catch (OperationCanceledException) {
        canceledExceptionThrown = true;
      }

      Assert.IsTrue(canceledExceptionThrown);
      Assert.AreEqual(0, executor.Requests.Count);

    }

  }

}
