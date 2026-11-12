#if NET5
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace System.Web.UJMW {

  /// <summary>
  /// Specification tests for the 'UjmwQueueBridgeExecutor' (dynamic client proxy -> bridge -> in-memory queue)
  /// including a simulated processor (see 'doc/epics/epic-async-queue-mode.md', 4.3, 7 and 8).
  /// </summary>
  [TestClass]
  public class UjmwQueueBridgeExecutorTests {

    public interface IBridgeProbe {

      int Calc(int a);

      string Run(CancellationToken c, int steps, Action<string, int> reportProgress);

    }

    private const string _Queue = "api/probe";

    #region " Helpers "

    private static UjmwInMemoryQueuingService CreateService() {

      UjmwInMemoryQueuingService service = new UjmwInMemoryQueuingService(new UjmwQueueOptions());
      service.RegisterQueue(typeof(IBridgeProbe), _Queue);

      return service;
    }

    private static IBridgeProbe CreateProxy(UjmwQueueBridgeExecutor executor) {
      string url = UjmwQueueBridgeExecutor.BuildBridgeUrl(typeof(IBridgeProbe));
      return DynamicClientFactory.CreateInstance<IBridgeProbe>(executor, () => url);
    }

    private static void Auth(string secret, out string salt, out string hash) {
      salt = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSaltLength);
      hash = UjmwAsyncProtocol.ComputePollingHash(secret, salt);
    }

    /// <summary>
    /// Waits (polling) until an entry can be dequeued.
    /// </summary>
    private static UjmwQueueLease WaitForLease(UjmwInMemoryQueuingService service) {

      DateTime deadline = DateTime.UtcNow.AddSeconds(10);

      while (DateTime.UtcNow < deadline) {

        UjmwQueueLease lease = service.Dequeue(typeof(IBridgeProbe), _Queue, "worker", UjmwQueueMethodFilter.All, -1);
        if (lease.HasEntry) {
          return lease;
        }

        Thread.Sleep(20);
      }

      Assert.Fail("No entry was enqueued");
      return null;
    }

    private static UjmwQueueHeartbeatResult Heartbeat(UjmwInMemoryQueuingService service, UjmwQueueLease lease, string callbacksJson) {

      string salt;
      string hash;
      Auth(lease.PollingSecret, out salt, out hash);

      return service.Heartbeat(typeof(IBridgeProbe), _Queue, "worker", lease.RequestId, salt, hash, callbacksJson);
    }

    private static void Complete(UjmwInMemoryQueuingService service, UjmwQueueLease lease, string responseJson) {

      string salt;
      string hash;
      Auth(lease.PollingSecret, out salt, out hash);

      UjmwQueueLease result = service.Complete(typeof(IBridgeProbe), _Queue, "worker", lease.RequestId, salt, hash, responseJson, null, -1);
      Assert.IsNull(result.Fault);

    }

    private static void ReturnLease(UjmwInMemoryQueuingService service, UjmwQueueLease lease) {

      string salt;
      string hash;
      Auth(lease.PollingSecret, out salt, out hash);

      UjmwQueueResult result = service.Return(typeof(IBridgeProbe), _Queue, "worker", lease.RequestId, salt, hash);
      Assert.IsNull(result.Fault);

    }

    #endregion

    [TestMethod]
    public void ParseUrl_ExtractsMethodAndQuery() {

      string methodName;
      Dictionary<string, string> query;

      UjmwQueueBridgeExecutor.ParseUrl("ujmw-queue://IBridgeProbe/Run?wait=20&asyncId=12", out methodName, out query);

      Assert.AreEqual("Run", methodName);
      Assert.AreEqual("20", query["wait"]);
      Assert.AreEqual("12", query["asyncId"]);

      UjmwQueueBridgeExecutor.ParseUrl("ujmw-queue://IBridgeProbe/", out methodName, out query);
      Assert.IsNull(methodName);

    }

    [TestMethod]
    public void SynchronousCall_BlocksUntilProcessorCompleted() {

      UjmwInMemoryQueuingService service = CreateService();
      UjmwQueueBridgeExecutor executor = new UjmwQueueBridgeExecutor(service, typeof(IBridgeProbe), _Queue);
      IBridgeProbe proxy = CreateProxy(executor);

      Task processor = Task.Run(() => {

        UjmwQueueLease lease = WaitForLease(service);

        Assert.AreEqual("Calc", lease.MethodName);
        Assert.AreEqual(21, JObject.Parse(lease.RequestJson)["a"].Value<int>());

        Assert.IsFalse(Heartbeat(service, lease, null).CancellationRequested);
        Complete(service, lease, "{ \"return\": 42 }");

      });

      int result = proxy.Calc(21);

      processor.Wait();
      Assert.AreEqual(42, result);

    }

    [TestMethod]
    public void SynchronousCall_PassesBusinessFaultThrough() {

      UjmwInMemoryQueuingService service = CreateService();
      UjmwQueueBridgeExecutor executor = new UjmwQueueBridgeExecutor(service, typeof(IBridgeProbe), _Queue);
      IBridgeProbe proxy = CreateProxy(executor);

      Task processor = Task.Run(() => {
        UjmwQueueLease lease = WaitForLease(service);
        Heartbeat(service, lease, null);
        Complete(service, lease, "{ \"fault\": \"Boom\" }");
      });

      Exception caught = null;
      try {
        proxy.Calc(1);
      }
      catch (Exception ex) {
        caught = ex;
      }

      processor.Wait();
      Assert.IsNotNull(caught);
      StringAssert.Contains(caught.Message + caught.InnerException?.Message, "Boom");

    }

    [TestMethod]
    public void SynchronousCall_TimesOutAndRemovesEntry() {

      UjmwInMemoryQueuingService service = CreateService();
      UjmwQueueBridgeExecutor executor = new UjmwQueueBridgeExecutor(service, typeof(IBridgeProbe), _Queue);
      executor.SynchronousCallTimeout = TimeSpan.FromMilliseconds(300);
      IBridgeProbe proxy = CreateProxy(executor);

      Exception caught = null;
      try {
        proxy.Calc(1);
      }
      catch (Exception ex) {
        caught = ex;
      }

      Assert.IsNotNull(caught);
      StringAssert.Contains(caught.Message + caught.InnerException?.Message, UjmwQueueProtocol.FaultProcessingTimeout);

      UjmwQueueInfo info = service.GetInfo(typeof(IBridgeProbe), _Queue);
      Assert.AreEqual(0, info.StatsTotal.Queued);

    }

    [TestMethod]
    public void LongRunningCall_ReplaysCallbacksInOrderAndReturnsResult() {

      UjmwInMemoryQueuingService service = CreateService();
      UjmwQueueBridgeExecutor executor = new UjmwQueueBridgeExecutor(service, typeof(IBridgeProbe), _Queue);
      executor.LongPollTime = TimeSpan.FromMilliseconds(100);
      IBridgeProbe proxy = CreateProxy(executor);

      Task processor = Task.Run(() => {

        UjmwQueueLease lease = WaitForLease(service);
        Assert.AreEqual("Run", lease.MethodName);

        Heartbeat(service, lease, null);
        Heartbeat(service, lease, "{ \"reportProgressInvokes\": [ { \"#\": 1, \"arg1\": \"A\", \"arg2\": 1 } ] }");
        Thread.Sleep(150);
        Heartbeat(service, lease, "{ \"reportProgressInvokes\": [ { \"#\": 2, \"arg1\": \"B\", \"arg2\": 2 } ] }");
        Complete(service, lease, "{ \"return\": \"done\", \"reportProgressInvokes\": [ { \"#\": 3, \"arg1\": \"C\", \"arg2\": 3 } ] }");

      });

      List<string> progress = new List<string>();
      string result = proxy.Run(CancellationToken.None, 3, (text, current) => {
        progress.Add(text + current);
      });

      processor.Wait();
      Assert.AreEqual("done", result);
      CollectionAssert.AreEqual(new string[] { "A1", "B2", "C3" }, progress.ToArray());

    }

    [TestMethod]
    public void LongRunningCall_CancellationIsForwardedToProcessor() {

      UjmwInMemoryQueuingService service = CreateService();
      UjmwQueueBridgeExecutor executor = new UjmwQueueBridgeExecutor(service, typeof(IBridgeProbe), _Queue);
      executor.LongPollTime = TimeSpan.FromMilliseconds(100);
      IBridgeProbe proxy = CreateProxy(executor);

      using (CancellationTokenSource cts = new CancellationTokenSource()) {

        Task processor = Task.Run(() => {

          UjmwQueueLease lease = WaitForLease(service);
          Heartbeat(service, lease, null);

          cts.Cancel();

          DateTime deadline = DateTime.UtcNow.AddSeconds(10);
          bool cancellationRequested = false;
          while (!cancellationRequested && DateTime.UtcNow < deadline) {
            Thread.Sleep(50);
            cancellationRequested = Heartbeat(service, lease, null).CancellationRequested;
          }

          Assert.IsTrue(cancellationRequested);
          ReturnLease(service, lease);

        });

        Assert.ThrowsException<OperationCanceledException>(() => {
          proxy.Run(cts.Token, 3, (text, current) => { });
        });

        processor.Wait();
      }

    }

    [TestMethod]
    public void LongRunningCall_QueueCapacityFaultIsReported() {

      UjmwQueueOptions options = new UjmwQueueOptions();
      options.MaxQueueSize = 0;

      UjmwInMemoryQueuingService service = new UjmwInMemoryQueuingService(options);
      service.RegisterQueue(typeof(IBridgeProbe), _Queue);

      UjmwQueueBridgeExecutor executor = new UjmwQueueBridgeExecutor(service, typeof(IBridgeProbe), _Queue);
      IBridgeProbe proxy = CreateProxy(executor);

      Exception caught = null;
      try {
        proxy.Run(CancellationToken.None, 1, (text, current) => { });
      }
      catch (Exception ex) {
        caught = ex;
      }

      Assert.IsNotNull(caught);
      StringAssert.Contains(caught.Message + caught.InnerException?.Message, UjmwQueueProtocol.FaultCapacityExceeded);

    }

  }

}
#endif
