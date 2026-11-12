#if NET5
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace System.Web.UJMW {

  /// <summary>
  /// Tests of the remote 'UjmwQueueClient' (URL, verbs, query names, auth header, bodies, parsing)
  /// and of the shared wire mapping 'UjmwQueueWire' (see 'doc/epics/epic-async-queue-mode.md', 5 and 9.3).
  /// </summary>
  [TestClass]
  public class UjmwQueueClientTests {

    public interface IRemoteWorker {
      int Square(int value);
    }

    private class RecordingExecutor : IHttpRequestExecutor {

      private readonly List<string> _Requests = new List<string>();
      private string _NextResponse = "{ \"fault\": null }";
      private int _NextStatusCode = 200;

      public string LastMethod = null;
      public string LastUrl = null;
      public string LastBody = null;
      public IDictionary<string, string> LastHeaders = null;

      public string NextResponse {
        get {
          return _NextResponse;
        }
        set {
          _NextResponse = value;
        }
      }

      public int NextStatusCode {
        get {
          return _NextStatusCode;
        }
        set {
          _NextStatusCode = value;
        }
      }

      public int ExecuteHttpRequest(
        string httpMethod, string url, string requestContent, IDictionary<string, string> requestHeaders,
        out string responseContent, out IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders, out string reasonPhrase
      ) {

        this.LastMethod = httpMethod;
        this.LastUrl = url;
        this.LastBody = requestContent;
        this.LastHeaders = requestHeaders;
        _Requests.Add(httpMethod + " " + url);

        responseContent = _NextResponse;
        responseHeaders = new KeyValuePair<string, IEnumerable<string>>[0];
        reasonPhrase = "X";

        return _NextStatusCode;
      }

    }

    private static UjmwQueueClient CreateClient(RecordingExecutor executor) {
      return new UjmwQueueClient((t) => "http://host/api/worker/", (t) => "Bearer abc", executor);
    }

    [TestMethod]
    public void GetInfo_UsesGetOnQueueRoute_AndParsesStats() {

      RecordingExecutor executor = new RecordingExecutor();
      executor.NextResponse =
        "{ \"statsTotal\": { \"queued\": 3, \"active\": 1, \"past1m\": 2, \"past10m\": 2, \"past1h\": 2, \"past10h\": 2 }," +
        "  \"statsPerMethod\": { \"Square\": { \"queued\": 3, \"active\": 1 } }," +
        "  \"processors\": { \"p1\": { \"active\": 1, \"capacity\": 4, \"lastActivity\": \"2025-01-01T12:00:00Z\" }, \"p2\": { \"active\": 0 } }, \"fault\": null }";

      UjmwQueueInfo info = CreateClient(executor).GetInfo(typeof(IRemoteWorker), null);

      Assert.AreEqual("GET", executor.LastMethod);
      Assert.AreEqual("http://host/api/worker/_queue", executor.LastUrl);
      Assert.AreEqual("Bearer abc", executor.LastHeaders["Authorization"]);
      Assert.AreEqual(3, info.StatsTotal.Queued);
      Assert.AreEqual(2, info.StatsTotal.Past10h);
      Assert.AreEqual(1, info.StatsPerMethod["Square"].Active);
      Assert.AreEqual(1, info.Processors["p1"].Active);
      Assert.AreEqual(4, info.Processors["p1"].Capacity);
      Assert.AreEqual(-1, info.Processors["p2"].Capacity);
      Assert.AreEqual(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc), info.Processors["p1"].LastActivity);
      Assert.IsNull(info.Fault);

    }

    [TestMethod]
    public void Dequeue_SendsProcessorAndFilter_AndParsesLease() {

      RecordingExecutor executor = new RecordingExecutor();
      executor.NextResponse =
        "{ \"totalInQueue\": 5, \"method\": \"Square\", \"requestId\": \"123\", \"request\": { \"value\": 7 }," +
        "  \"pollingSecret\": \"Zt8kQ2xW\", \"heartbeatSec\": 3, \"fault\": null }";

      UjmwQueueLease lease = CreateClient(executor).Dequeue(
        typeof(IRemoteWorker), null, "worker 1", UjmwQueueMethodFilter.Blacklist(new string[] { "A", "B" }), 4
      );

      Assert.AreEqual("GET", executor.LastMethod);
      Assert.AreEqual("http://host/api/worker/_queue?processor=worker%201&dequeue=%21A%2C%21B&capacity=4", executor.LastUrl);
      Assert.IsTrue(lease.HasEntry);
      Assert.AreEqual(5, lease.TotalInQueue);
      Assert.AreEqual("Square", lease.MethodName);
      Assert.AreEqual("123", lease.RequestId);
      Assert.AreEqual(7, JObject.Parse(lease.RequestJson)["value"].Value<int>());
      Assert.AreEqual("Zt8kQ2xW", lease.PollingSecret);
      Assert.AreEqual(3, lease.HeartbeatSec);

    }

    [TestMethod]
    public void Dequeue_EmptyQueue_ReturnsEmptyLease() {

      RecordingExecutor executor = new RecordingExecutor();
      executor.NextResponse = "{ \"totalInQueue\": 0, \"method\": null, \"requestId\": null, \"request\": null, \"pollingSecret\": null, \"heartbeatSec\": 0, \"fault\": null }";

      UjmwQueueLease lease = CreateClient(executor).Dequeue(typeof(IRemoteWorker), null, "p", null, -1);

      Assert.IsFalse(lease.HasEntry);
      Assert.IsNull(lease.RequestJson);
      Assert.IsTrue(executor.LastUrl.EndsWith("dequeue=%2A"));
      Assert.IsFalse(executor.LastUrl.Contains("capacity"));

    }

    [TestMethod]
    public void Heartbeat_UsesPutWithLeaseQuery_AndCallbackBody() {

      RecordingExecutor executor = new RecordingExecutor();
      executor.NextResponse = "{ \"cancellationRequested\": true, \"fault\": null }";

      string callbacks = "{\"reportProgressInvokes\":[{\"#\":1,\"arg1\":\"x\"}]}";
      UjmwQueueHeartbeatResult result = CreateClient(executor).Heartbeat(typeof(IRemoteWorker), null, "p", "123", "salt01", "hash", callbacks);

      Assert.AreEqual("PUT", executor.LastMethod);
      Assert.AreEqual("http://host/api/worker/_queue?processor=p&requestId=123&pollingSalt=salt01&pollingHash=hash", executor.LastUrl);
      Assert.AreEqual(callbacks, executor.LastBody);
      Assert.IsTrue(result.CancellationRequested);

    }

    [TestMethod]
    public void Heartbeat_WithoutCallbacks_SendsEmptyObject() {

      RecordingExecutor executor = new RecordingExecutor();
      executor.NextResponse = "{ \"cancellationRequested\": false, \"fault\": \"Withdrawn - dequeue confirmation missed\" }";

      UjmwQueueHeartbeatResult result = CreateClient(executor).Heartbeat(typeof(IRemoteWorker), null, "p", "1", "s", "h", null);

      Assert.AreEqual("{}", executor.LastBody);
      Assert.AreEqual(UjmwQueueProtocol.FaultConfirmationMissed, result.Fault);

    }

    [TestMethod]
    public void Return_UsesDelete() {

      RecordingExecutor executor = new RecordingExecutor();

      UjmwQueueResult result = CreateClient(executor).Return(typeof(IRemoteWorker), null, "p", "1", "s", "h");

      Assert.AreEqual("DELETE", executor.LastMethod);
      Assert.IsNull(executor.LastBody);
      Assert.IsNull(result.Fault);

    }

    [TestMethod]
    public void Complete_UsesPostWithResponseAndDequeueNext() {

      RecordingExecutor executor = new RecordingExecutor();
      executor.NextResponse = "{ \"totalInQueue\": 0, \"method\": \"Square\", \"requestId\": \"77\", \"request\": { \"value\": 2 }, \"pollingSecret\": \"x\", \"heartbeatSec\": 3, \"fault\": null }";

      UjmwQueueLease next = CreateClient(executor).Complete(
        typeof(IRemoteWorker), null, "p", "1", "s", "h", "{\"return\":49,\"fault\":null}", UjmwQueueMethodFilter.All, 2
      );

      Assert.AreEqual("POST", executor.LastMethod);
      Assert.AreEqual("http://host/api/worker/_queue?processor=p&requestId=1&pollingSalt=s&pollingHash=h&capacity=2", executor.LastUrl);
      JObject body = JObject.Parse(executor.LastBody);
      Assert.AreEqual(49, body["response"]["return"].Value<int>());
      Assert.AreEqual("*", body["dequeueNext"].Value<string>());
      Assert.AreEqual("77", next.RequestId);

    }

    [TestMethod]
    public void Complete_WithoutDequeueNext_ParsesFaultOnlyResponse() {

      RecordingExecutor executor = new RecordingExecutor();
      executor.NextResponse = "{ \"fault\": \"processor lost\" }";

      UjmwQueueLease next = CreateClient(executor).Complete(typeof(IRemoteWorker), null, "p", "1", "s", "h", "{\"return\":1}", null, -1);

      Assert.AreEqual(JTokenType.Null, JObject.Parse(executor.LastBody)["dequeueNext"].Type);
      Assert.IsFalse(next.HasEntry);
      Assert.AreEqual(UjmwQueueProtocol.FaultProcessorLost, next.Fault);

    }

    [TestMethod]
    public void NonSuccessStatus_ThrowsTransportException() {

      RecordingExecutor executor = new RecordingExecutor();
      executor.NextStatusCode = 403;
      executor.NextResponse = "Forbidden";

      UjmwQueueTransportException ex = Assert.ThrowsException<UjmwQueueTransportException>(() => {
        CreateClient(executor).GetInfo(typeof(IRemoteWorker), null);
      });

      Assert.AreEqual(403, ex.HttpStatusCode);

    }

    [TestMethod]
    public void Wire_RoundTrip_LeaseInfoAndCompletion() {

      UjmwQueueLease lease = new UjmwQueueLease();
      lease.TotalInQueue = 2;
      lease.MethodName = "Square";
      lease.RequestId = "9";
      lease.RequestJson = "{\"value\":3,\"_\":{\"tenant\":\"t1\"}}";
      lease.PollingSecret = "sec";
      lease.HeartbeatSec = 3;

      UjmwQueueLease parsedLease = UjmwQueueWire.ParseLease(UjmwQueueWire.SerializeLease(lease));
      Assert.AreEqual("9", parsedLease.RequestId);
      Assert.AreEqual("t1", JObject.Parse(parsedLease.RequestJson)["_"]["tenant"].Value<string>());

      UjmwQueueInfo info = new UjmwQueueInfo();
      info.StatsTotal.Queued = 4;
      info.StatsPerMethod["Square"] = new UjmwQueueStats();
      info.StatsPerMethod["Square"].Active = 2;

      UjmwQueueInfo parsedInfo = UjmwQueueWire.ParseInfo(UjmwQueueWire.SerializeInfo(info));
      Assert.AreEqual(4, parsedInfo.StatsTotal.Queued);
      Assert.AreEqual(2, parsedInfo.StatsPerMethod["Square"].Active);

      string responseJson;
      UjmwQueueMethodFilter dequeueNext;
      string error;

      string body = UjmwQueueWire.BuildCompletionBody("{\"return\":9}", UjmwQueueMethodFilter.Whitelist(new string[] { "Square" }));
      Assert.IsTrue(UjmwQueueWire.TryParseCompletionBody(body, out responseJson, out dequeueNext, out error));
      Assert.AreEqual(9, JObject.Parse(responseJson)["return"].Value<int>());
      Assert.IsTrue(dequeueNext.IsMatch("Square"));
      Assert.IsFalse(dequeueNext.IsMatch("Other"));

      Assert.IsFalse(UjmwQueueWire.TryParseCompletionBody("{\"dequeueNext\":\"*\"}", out responseJson, out dequeueNext, out error));
      Assert.IsFalse(UjmwQueueWire.TryParseCompletionBody("{\"response\":{},\"dequeueNext\":\"a,!b\"}", out responseJson, out dequeueNext, out error));

    }

  }

}
#endif
