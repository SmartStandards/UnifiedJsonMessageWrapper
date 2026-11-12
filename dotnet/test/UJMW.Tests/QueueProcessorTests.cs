#if NET5
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// End-to-end tests (in-process): dynamic client proxy -> bridge -> in-memory queue -> 'QueueProcessor' -> implementation
  /// (see 'doc/epics/epic-async-queue-mode.md', 7 and 9.1).
  /// </summary>
  [TestClass]
  public class QueueProcessorTests {

    public interface IWorker {

      int Square(int value);

      string Work(CancellationToken cancellationToken, int steps, Action<string, int?, int?> reportProgress);

    }

    public class Worker : IWorker {

      public int Square(int value) {
        return value * value;
      }

      public string Work(CancellationToken cancellationToken, int steps, Action<string, int?, int?> reportProgress) {

        for (int i = 1; i <= steps; i++) {
          cancellationToken.ThrowIfCancellationRequested();
          reportProgress.Invoke("Step", i, steps);
          Thread.Sleep(50);
        }

        return "worked " + steps;
      }

    }

    private const string _Queue = "api/worker";

    private static UjmwInMemoryQueuingService CreateService() {

      UjmwInMemoryQueuingService service = new UjmwInMemoryQueuingService(new UjmwQueueOptions());
      service.RegisterQueue(typeof(IWorker), _Queue);

      return service;
    }

    private static IWorker CreateProxy(UjmwInMemoryQueuingService service) {

      UjmwQueueBridgeExecutor executor = new UjmwQueueBridgeExecutor(service, typeof(IWorker), _Queue);
      executor.LongPollTime = TimeSpan.FromMilliseconds(100);

      string url = UjmwQueueBridgeExecutor.BuildBridgeUrl(typeof(IWorker));
      return DynamicClientFactory.CreateInstance<IWorker>(executor, () => url);
    }

    [TestMethod]
    public void SynchronousCall_IsProcessed() {

      UjmwInMemoryQueuingService service = CreateService();
      IWorker proxy = CreateProxy(service);

      using (QueueProcessor<IWorker> processor = new QueueProcessor<IWorker>(new Worker(), service, 1, null, null, default(CancellationToken), _Queue)) {

        processor.Start(0);

        Assert.AreEqual(49, proxy.Square(7));
        Assert.AreEqual(16, proxy.Square(4));

      }

    }

    [TestMethod]
    public void LongRunningCall_DeliversProgressAndResult() {

      UjmwInMemoryQueuingService service = CreateService();
      IWorker proxy = CreateProxy(service);

      using (QueueProcessor<IWorker> processor = new QueueProcessor<IWorker>(new Worker(), service, 2, null, null, default(CancellationToken), _Queue)) {

        processor.Start(0);

        List<int> currents = new List<int>();
        string result = proxy.Work(CancellationToken.None, 5, (text, current, total) => {
          currents.Add(current.Value);
          Assert.AreEqual(5, total.Value);
        });

        Assert.AreEqual("worked 5", result);
        CollectionAssert.AreEqual(new int[] { 1, 2, 3, 4, 5 }, currents.ToArray());

      }

    }

    [TestMethod]
    public void LongRunningCall_CancellationReachesImplementation() {

      UjmwInMemoryQueuingService service = CreateService();
      IWorker proxy = CreateProxy(service);

      using (QueueProcessor<IWorker> processor = new QueueProcessor<IWorker>(new Worker(), service, 1, null, null, default(CancellationToken), _Queue)) {

        processor.Start(0);

        using (CancellationTokenSource cts = new CancellationTokenSource()) {

          Assert.ThrowsException<OperationCanceledException>(() => {
            proxy.Work(cts.Token, 400, (text, current, total) => {
              if (current == 3) {
                cts.Cancel();
              }
            });
          });

        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (processor.CurrentTasks > 0 && DateTime.UtcNow < deadline) {
          Thread.Sleep(20);
        }

        Assert.AreEqual(0, processor.CurrentTasks);

      }

    }

    [TestMethod]
    public void MethodWhitelist_OnlyProcessesListedMethods() {

      UjmwInMemoryQueuingService service = CreateService();
      service.Enqueue(typeof(IWorker), _Queue, "Square", "{ \"value\": 3 }", false);

      using (QueueProcessor<IWorker> processor = new QueueProcessor<IWorker>(
        new Worker(), service, 1, new string[] { nameof(IWorker.Work) }, null, default(CancellationToken), _Queue
      )) {

        Assert.IsFalse(processor.Next());
        Assert.AreEqual(1, service.GetInfo(typeof(IWorker), _Queue).StatsTotal.Queued);

      }

    }

    [TestMethod]
    public void UnknownMethodName_FailsFast() {

      UjmwInMemoryQueuingService service = CreateService();

      Assert.ThrowsException<ArgumentException>(() => {
        new QueueProcessor<IWorker>(new Worker(), service, 1, new string[] { "Nope" });
      });

    }

    [TestMethod]
    public void Kill_ReturnsRunningEntry() {

      UjmwInMemoryQueuingService service = CreateService();
      service.Enqueue(typeof(IWorker), _Queue, "Work", "{ \"steps\": 400 }", true);

      QueueProcessor<IWorker> processor = new QueueProcessor<IWorker>(new Worker(), service, 1, null, null, default(CancellationToken), _Queue);

      Assert.IsTrue(processor.Next());
      Thread.Sleep(150);

      processor.Kill(true);

      Assert.AreEqual(0, processor.CurrentTasks);
      Assert.AreEqual(1, service.GetInfo(typeof(IWorker), _Queue).StatsTotal.Queued);

    }

    /// <summary>
    /// Delegating queue which counts the processor-side round-trips.
    /// </summary>
    private class CountingQueue : IUjmwQueue {

      private readonly IUjmwQueue _Inner;
      private int _DequeueCount = 0;
      private int _CompleteWithDequeueNextCount = 0;

      public CountingQueue(IUjmwQueue inner) {
        _Inner = inner;
      }

      public int DequeueCount {
        get {
          return Volatile.Read(ref _DequeueCount);
        }
      }

      public int CompleteWithDequeueNextCount {
        get {
          return Volatile.Read(ref _CompleteWithDequeueNextCount);
        }
      }

      public UjmwQueueInfo GetInfo(Type contractType, string queueName) {
        return _Inner.GetInfo(contractType, queueName);
      }

      public UjmwQueueLease Dequeue(Type contractType, string queueName, string processorId, UjmwQueueMethodFilter methodFilter, int capacity) {
        Interlocked.Increment(ref _DequeueCount);
        return _Inner.Dequeue(contractType, queueName, processorId, methodFilter, capacity);
      }

      public UjmwQueueHeartbeatResult Heartbeat(
        Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash,
        string callbackInvocationsJson
      ) {
        return _Inner.Heartbeat(contractType, queueName, processorId, requestId, pollingSalt, pollingHash, callbackInvocationsJson);
      }

      public UjmwQueueResult Return(Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash) {
        return _Inner.Return(contractType, queueName, processorId, requestId, pollingSalt, pollingHash);
      }

      public UjmwQueueLease Complete(
        Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash,
        string responseJson, UjmwQueueMethodFilter dequeueNext, int capacity
      ) {

        if (dequeueNext != null) {
          Interlocked.Increment(ref _CompleteWithDequeueNextCount);
        }

        return _Inner.Complete(contractType, queueName, processorId, requestId, pollingSalt, pollingHash, responseJson, dequeueNext, capacity);
      }

    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout) {

      DateTime deadline = DateTime.UtcNow.Add(timeout);

      while (!condition.Invoke()) {

        if (DateTime.UtcNow > deadline) {
          return false;
        }

        Thread.Sleep(20);
      }

      return true;
    }

    [TestMethod]
    public void Poller_BurstsWhileSlotsAreFree() {

      UjmwInMemoryQueuingService service = CreateService();
      for (int i = 0; i < 3; i++) {
        service.Enqueue(typeof(IWorker), _Queue, "Work", "{ \"steps\": 20 }", true);
      }

      CountingQueue queue = new CountingQueue(service);

      using (QueueProcessor<IWorker> processor = new QueueProcessor<IWorker>(new Worker(), queue, 3, null, null, default(CancellationToken), _Queue)) {

        //the regular interval (60 s) would never be reached within the test
        processor.Start(60);

        Assert.IsTrue(WaitUntil(() => processor.CurrentTasks == 3, TimeSpan.FromSeconds(2)));
        Assert.AreEqual(3, queue.DequeueCount);

      }

    }

    [TestMethod]
    public void Poller_SendsNoDequeuesWhileAllSlotsAreBusy() {

      UjmwInMemoryQueuingService service = CreateService();
      service.Enqueue(typeof(IWorker), _Queue, "Work", "{ \"steps\": 20 }", true);

      CountingQueue queue = new CountingQueue(service);

      using (QueueProcessor<IWorker> processor = new QueueProcessor<IWorker>(new Worker(), queue, 1, null, null, default(CancellationToken), _Queue)) {

        processor.Start(0);

        Assert.IsTrue(WaitUntil(() => processor.CurrentTasks == 1, TimeSpan.FromSeconds(2)));
        int dequeuesAtStart = queue.DequeueCount;

        Thread.Sleep(500);

        Assert.AreEqual(1, processor.CurrentTasks);
        Assert.AreEqual(dequeuesAtStart, queue.DequeueCount);

      }

    }

    [TestMethod]
    public void Completion_RequestsNextEntryAndIncreasesPollRate() {

      UjmwInMemoryQueuingService service = CreateService();
      service.Enqueue(typeof(IWorker), _Queue, "Square", "{ \"value\": 2 }", false);
      service.Enqueue(typeof(IWorker), _Queue, "Square", "{ \"value\": 3 }", false);

      CountingQueue queue = new CountingQueue(service);

      using (QueueProcessor<IWorker> processor = new QueueProcessor<IWorker>(new Worker(), queue, 2, null, null, default(CancellationToken), _Queue)) {

        processor.IncreasedPollRateMs = 200;
        processor.IncreasePollRateForSecs = 20;
        processor.Start(60);

        Assert.IsTrue(WaitUntil(() => service.GetInfo(typeof(IWorker), _Queue).StatsTotal.Queued == 0 && processor.CurrentTasks == 0, TimeSpan.FromSeconds(3)));
        Assert.IsTrue(queue.CompleteWithDequeueNextCount >= 1);

        //the increased poll rate (200 ms) is active - a new entry is picked up long before the regular 60 s interval
        service.Enqueue(typeof(IWorker), _Queue, "Square", "{ \"value\": 4 }", false);

        Assert.IsTrue(WaitUntil(() => service.GetInfo(typeof(IWorker), _Queue).StatsTotal.Queued == 0, TimeSpan.FromSeconds(3)));

      }

    }

  }

}
#endif
