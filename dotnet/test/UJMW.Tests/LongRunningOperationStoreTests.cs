#if NET5
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// Specification tests for the default in-memory operation store of the ASP.NET Core dynamic controller
  /// (see 'doc/epics/epic-long-running-operations.md', chapters 4.5, 5 and 6).
  /// </summary>
  [TestClass]
  public class LongRunningOperationStoreTests {

    private const string _Scope = "Test/Operation";

    private static readonly TimeSpan _Generous = TimeSpan.FromSeconds(10);

    #region " Helpers "

    /// <summary>
    /// Creates a fresh polling salt/hash pair for the given secret.
    /// </summary>
    private static void CreatePollingAuth(string pollingSecret, out string pollingSalt, out string pollingHash) {

      pollingSalt = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSaltLength);
      pollingHash = UjmwAsyncProtocol.ComputePollingHash(pollingSecret, pollingSalt);

    }

    /// <summary>
    /// Polls (with valid authentication) until the operation has terminated.
    /// </summary>
    private static UjmwOperationSnapshot PollUntilTerminal(IUjmwOperationStore store, UjmwOperationSnapshot initial) {

      DateTime deadline = DateTime.UtcNow.Add(_Generous);
      UjmwOperationSnapshot snapshot = initial;

      while (!snapshot.IsTerminal) {

        Assert.IsTrue(DateTime.UtcNow < deadline, "Operation did not terminate in time.");
        Thread.Sleep(20);

        string salt;
        string hash;
        CreatePollingAuth(initial.PollingSecret, out salt, out hash);

        Assert.IsTrue(store.TryPoll(_Scope, initial.AsyncId.ToString(), salt, hash, out snapshot));

      }

      return snapshot;
    }

    #endregion

    [TestMethod]
    public void Initiate_CompletedWithinWait_ReturnsOutcomeWithoutAsyncId() {

      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore();

      UjmwOperationSnapshot snapshot = store.Initiate(
        _Scope, (ct, recorder) => new UjmwOperationOutcome("done", false), _Generous
      );

      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCompleted, snapshot.ExecState);
      Assert.AreEqual(0L, snapshot.AsyncId);
      Assert.IsNull(snapshot.PollingSecret);
      Assert.IsFalse(snapshot.Cancellable);
      Assert.AreEqual("done", snapshot.Outcome.Payload);
      Assert.AreEqual(0, store.KnownOperationCount);

    }

    [TestMethod]
    public void Initiate_FailingBody_ReturnsFailedState() {

      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore();

      UjmwOperationSnapshot snapshot = store.Initiate(
        _Scope, (ct, recorder) => new UjmwOperationOutcome("err", true), _Generous
      );

      Assert.AreEqual(UjmwAsyncProtocol._ExecStateFailed, snapshot.ExecState);
      Assert.AreEqual("err", snapshot.Outcome.Payload);

    }

    [TestMethod]
    public void Initiate_ThrowingBody_ReturnsFailedState() {

      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore();

      UjmwOperationSnapshot snapshot = store.Initiate(
        _Scope, (ct, recorder) => { throw new InvalidOperationException("boom"); }, _Generous
      );

      Assert.AreEqual(UjmwAsyncProtocol._ExecStateFailed, snapshot.ExecState);

    }

    [TestMethod]
    public void Initiate_NotCompletedWithinWait_ReturnsRunningStateWithSecretAndPollNbf() {

      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore();
      ManualResetEventSlim release = new ManualResetEventSlim(false);

      UjmwOperationSnapshot snapshot = store.Initiate(
        _Scope, (ct, recorder) => { release.Wait(); return new UjmwOperationOutcome(42, false); }, TimeSpan.FromMilliseconds(50)
      );

      Assert.AreEqual(UjmwAsyncProtocol._ExecStateRunning, snapshot.ExecState);
      Assert.IsTrue(snapshot.AsyncId > 0);
      Assert.IsTrue(snapshot.AsyncId < (1L << 53), "asyncId must fit into 53 bits");
      Assert.AreEqual(UjmwAsyncProtocol._PollingSecretLength, snapshot.PollingSecret.Length);
      Assert.IsTrue(snapshot.Cancellable);
      Assert.IsTrue(snapshot.PollNbf > DateTime.UtcNow.AddSeconds(-1));
      Assert.IsNull(snapshot.Outcome);
      Assert.AreEqual(1, store.ActiveOperationCount);

      release.Set();

      UjmwOperationSnapshot final = PollUntilTerminal(store, snapshot);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCompleted, final.ExecState);
      Assert.AreEqual(snapshot.AsyncId, final.AsyncId);
      Assert.IsNull(final.PollingSecret);
      Assert.AreEqual(42, final.Outcome.Payload);

    }

    [TestMethod]
    public void TryPoll_OutcomeIsDrainedOnlyOnce() {

      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore();
      ManualResetEventSlim release = new ManualResetEventSlim(false);

      UjmwOperationSnapshot initial = store.Initiate(
        _Scope, (ct, recorder) => { release.Wait(); return new UjmwOperationOutcome(1, false); }, TimeSpan.Zero
      );
      release.Set();
      PollUntilTerminal(store, initial);

      string salt;
      string hash;
      CreatePollingAuth(initial.PollingSecret, out salt, out hash);
      UjmwOperationSnapshot second;

      Assert.IsFalse(store.TryPoll(_Scope, initial.AsyncId.ToString(), salt, hash, out second));
      Assert.IsNull(second);

    }

    [TestMethod]
    public void TryPoll_WrongHashOrScopeOrId_IsRejectedWithoutDraining() {

      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore();
      ManualResetEventSlim recorded = new ManualResetEventSlim(false);
      ManualResetEventSlim release = new ManualResetEventSlim(false);

      UjmwOperationSnapshot initial = store.Initiate(
        _Scope,
        (ct, recorder) => {
          recorder.Record("onProgress", new object[] { 7 });
          recorded.Set();
          release.Wait();
          return new UjmwOperationOutcome(null, false);
        },
        TimeSpan.Zero
      );
      Assert.IsTrue(recorded.Wait(_Generous));

      string salt;
      string hash;
      CreatePollingAuth(initial.PollingSecret, out salt, out hash);
      UjmwOperationSnapshot snapshot;

      Assert.IsFalse(store.TryPoll(_Scope, initial.AsyncId.ToString(), salt, hash + "x", out snapshot));
      Assert.IsFalse(store.TryPoll(_Scope, initial.AsyncId.ToString(), salt, UjmwAsyncProtocol.ComputePollingHash("wrong", salt), out snapshot));
      Assert.IsFalse(store.TryPoll("Other/Operation", initial.AsyncId.ToString(), salt, hash, out snapshot));
      Assert.IsFalse(store.TryPoll(_Scope, (initial.AsyncId + 1).ToString(), salt, hash, out snapshot));
      Assert.IsFalse(store.TryPoll(_Scope, "not-a-number", salt, hash, out snapshot));
      Assert.IsFalse(store.TryCancel(_Scope, initial.AsyncId.ToString(), salt, "wrong", TimeSpan.Zero, out snapshot));

      //the valid poll still receives the (not drained) invocation
      Assert.IsTrue(store.TryPoll(_Scope, initial.AsyncId.ToString(), salt, hash, out snapshot));
      Assert.AreEqual(1, snapshot.CallbackInvocations.Length);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateRunning, snapshot.ExecState);

      release.Set();
      PollUntilTerminal(store, initial);

    }

    [TestMethod]
    public void Callbacks_AreDeliveredInGlobalSequenceOrder_AndDrainedPerPoll() {

      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore();
      ManualResetEventSlim firstBatchDone = new ManualResetEventSlim(false);
      ManualResetEventSlim continueSignal = new ManualResetEventSlim(false);

      UjmwOperationSnapshot initial = store.Initiate(
        _Scope,
        (ct, recorder) => {
          recorder.Record("a", new object[] { 1 });
          recorder.Record("b", new object[0]);
          recorder.Record("a", new object[] { 2 });
          firstBatchDone.Set();
          continueSignal.Wait();
          recorder.Record("b", new object[0]);
          return new UjmwOperationOutcome(null, false);
        },
        TimeSpan.Zero
      );

      Assert.IsTrue(firstBatchDone.Wait(_Generous));

      string salt;
      string hash;
      CreatePollingAuth(initial.PollingSecret, out salt, out hash);
      UjmwOperationSnapshot snapshot;
      Assert.IsTrue(store.TryPoll(_Scope, initial.AsyncId.ToString(), salt, hash, out snapshot));

      UjmwCallbackInvocation[] firstBatch = snapshot.CallbackInvocations;
      Assert.AreEqual(3, firstBatch.Length);
      Assert.AreEqual(1L, firstBatch[0].Sequence);
      Assert.AreEqual("a", firstBatch[0].CallbackName);
      Assert.AreEqual(1, firstBatch[0].Arguments[0]);
      Assert.AreEqual(2L, firstBatch[1].Sequence);
      Assert.AreEqual("b", firstBatch[1].CallbackName);
      Assert.AreEqual(3L, firstBatch[2].Sequence);

      continueSignal.Set();
      UjmwOperationSnapshot final = PollUntilTerminal(store, initial);

      Assert.AreEqual(1, final.CallbackInvocations.Length);
      Assert.AreEqual(4L, final.CallbackInvocations[0].Sequence);

    }

    [TestMethod]
    public void Callbacks_OnPersistingFullBuffer_ThrowOverflowExceptionToServiceImplementation() {

      UjmwLongRunningOptions options = new UjmwLongRunningOptions();
      options.CallbackBufferLimit = 2;
      options.FreezeTimeBeforeCallbackBufferOverflow = TimeSpan.FromMilliseconds(100);
      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore(options);

      Exception caught = null;

      UjmwOperationSnapshot snapshot = store.Initiate(
        _Scope,
        (ct, recorder) => {
          recorder.Record("a", new object[0]);
          recorder.Record("a", new object[0]);
          try {
            recorder.Record("a", new object[0]);
          }
          catch (UjmwCallbackBufferOverflowException ex) {
            caught = ex;
          }
          return new UjmwOperationOutcome(null, false);
        },
        _Generous
      );

      Assert.IsNotNull(caught);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCompleted, snapshot.ExecState);
      Assert.AreEqual(2, snapshot.CallbackInvocations.Length);

    }

    [TestMethod]
    public void Callbacks_OnFullBuffer_AreUnblockedByPoll() {

      UjmwLongRunningOptions options = new UjmwLongRunningOptions();
      options.CallbackBufferLimit = 1;
      options.FreezeTimeBeforeCallbackBufferOverflow = _Generous;
      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore(options);

      UjmwOperationSnapshot initial = store.Initiate(
        _Scope,
        (ct, recorder) => {
          recorder.Record("a", new object[] { 1 });
          recorder.Record("a", new object[] { 2 }); //blocks until the first one was drained
          return new UjmwOperationOutcome(null, false);
        },
        TimeSpan.FromMilliseconds(100)
      );

      Assert.AreEqual(UjmwAsyncProtocol._ExecStateRunning, initial.ExecState);
      Assert.AreEqual(1, initial.CallbackInvocations.Length);

      UjmwOperationSnapshot final = PollUntilTerminal(store, initial);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCompleted, final.ExecState);

    }

    [TestMethod]
    public void TryCancel_RunningOperation_TriggersTokenAndEndsCancelled() {

      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore();
      ManualResetEventSlim started = new ManualResetEventSlim(false);

      UjmwOperationSnapshot initial = store.Initiate(
        _Scope,
        (ct, recorder) => {
          started.Set();
          ct.WaitHandle.WaitOne();
          ct.ThrowIfCancellationRequested();
          return new UjmwOperationOutcome(null, false);
        },
        TimeSpan.Zero
      );
      Assert.IsTrue(started.Wait(_Generous));

      string salt;
      string hash;
      CreatePollingAuth(initial.PollingSecret, out salt, out hash);
      UjmwOperationSnapshot snapshot;

      Assert.IsTrue(store.TryCancel(_Scope, initial.AsyncId.ToString(), salt, hash, _Generous, out snapshot));
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCancelled, snapshot.ExecState);
      Assert.IsFalse(snapshot.Cancellable);

    }

    [TestMethod]
    public void TryCancel_WithoutWait_ReportsCancellationRequested() {

      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore();
      ManualResetEventSlim started = new ManualResetEventSlim(false);
      ManualResetEventSlim release = new ManualResetEventSlim(false);

      UjmwOperationSnapshot initial = store.Initiate(
        _Scope,
        (ct, recorder) => {
          started.Set();
          release.Wait(); //ignores the token for a while
          return new UjmwOperationOutcome("partial", false);
        },
        TimeSpan.Zero
      );
      Assert.IsTrue(started.Wait(_Generous));

      string salt;
      string hash;
      CreatePollingAuth(initial.PollingSecret, out salt, out hash);
      UjmwOperationSnapshot snapshot;

      Assert.IsTrue(store.TryCancel(_Scope, initial.AsyncId.ToString(), salt, hash, TimeSpan.Zero, out snapshot));
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCancellationRequested, snapshot.ExecState);
      Assert.IsFalse(snapshot.Cancellable);

      release.Set();

      //a requested cancellation always ends with -2 (negative values indicate a cancellation request)
      UjmwOperationSnapshot final = PollUntilTerminal(store, initial);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCancelled, final.ExecState);

    }

    [TestMethod]
    public void Concurrency_ExceedingOperationsAreQueued_AndCancellableWhileQueued() {

      UjmwLongRunningOptions options = new UjmwLongRunningOptions();
      options.MaxConcurrentLongRunningAsyncCalls = 1;
      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore(options);
      ManualResetEventSlim release = new ManualResetEventSlim(false);
      bool secondExecuted = false;

      UjmwOperationSnapshot first = store.Initiate(
        _Scope, (ct, recorder) => { release.Wait(); return new UjmwOperationOutcome(null, false); }, TimeSpan.Zero
      );
      UjmwOperationSnapshot second = store.Initiate(
        _Scope, (ct, recorder) => { secondExecuted = true; return new UjmwOperationOutcome(null, false); }, TimeSpan.Zero
      );

      Assert.AreEqual(UjmwAsyncProtocol._ExecStateRunning, first.ExecState);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateQueued, second.ExecState);
      Assert.IsTrue(second.Cancellable);
      Assert.AreEqual(2, store.ActiveOperationCount);

      string salt;
      string hash;
      CreatePollingAuth(second.PollingSecret, out salt, out hash);
      UjmwOperationSnapshot cancelled;
      Assert.IsTrue(store.TryCancel(_Scope, second.AsyncId.ToString(), salt, hash, TimeSpan.Zero, out cancelled));
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCancelled, cancelled.ExecState);

      release.Set();
      PollUntilTerminal(store, first);

      Assert.IsFalse(secondExecuted);
      Assert.AreEqual(0, store.ActiveOperationCount);

    }

    [TestMethod]
    public void Concurrency_QueuedOperationStartsWhenSlotIsFree() {

      UjmwLongRunningOptions options = new UjmwLongRunningOptions();
      options.MaxConcurrentLongRunningAsyncCalls = 1;
      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore(options);
      ManualResetEventSlim release = new ManualResetEventSlim(false);

      UjmwOperationSnapshot first = store.Initiate(
        _Scope, (ct, recorder) => { release.Wait(); return new UjmwOperationOutcome(1, false); }, TimeSpan.Zero
      );
      UjmwOperationSnapshot second = store.Initiate(
        _Scope, (ct, recorder) => new UjmwOperationOutcome(2, false), TimeSpan.Zero
      );
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateQueued, second.ExecState);

      release.Set();

      Assert.AreEqual(1, PollUntilTerminal(store, first).Outcome.Payload);
      Assert.AreEqual(2, PollUntilTerminal(store, second).Outcome.Payload);

    }

    [TestMethod]
    public void Shutdown_CancelsRunningOperations_AndRejectsNewOnes() {

      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore();
      ManualResetEventSlim started = new ManualResetEventSlim(false);

      UjmwOperationSnapshot initial = store.Initiate(
        _Scope,
        (ct, recorder) => {
          started.Set();
          ct.WaitHandle.WaitOne();
          ct.ThrowIfCancellationRequested();
          return new UjmwOperationOutcome(null, false);
        },
        TimeSpan.Zero
      );
      Assert.IsTrue(started.Wait(_Generous));

      store.Shutdown();

      UjmwOperationSnapshot final = PollUntilTerminal(store, initial);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCancelled, final.ExecState);

      Assert.ThrowsException<InvalidOperationException>(() => {
        store.Initiate(_Scope, (ct, recorder) => new UjmwOperationOutcome(null, false), TimeSpan.Zero);
      });

    }

    [TestMethod]
    public void Retention_OutcomeExpiresAfterMaxPreservTime() {

      DateTime now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
      UjmwLongRunningOptions options = new UjmwLongRunningOptions();
      options.MaxOutcomePreservTime = TimeSpan.FromSeconds(300);
      options.CalculateNbf = (start, last, active) => last.AddSeconds(1);
      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore(options, () => now);
      ManualResetEventSlim release = new ManualResetEventSlim(false);

      UjmwOperationSnapshot initial = store.Initiate(
        _Scope, (ct, recorder) => { release.Wait(); return new UjmwOperationOutcome(null, false); }, TimeSpan.Zero
      );
      release.Set();
      WaitUntilTerminated(store, 1);

      now = now.AddSeconds(302); //beyond terminatedAt + max (pollNbf = start + 1s, +300s)

      string salt;
      string hash;
      CreatePollingAuth(initial.PollingSecret, out salt, out hash);
      UjmwOperationSnapshot snapshot;
      Assert.IsFalse(store.TryPoll(_Scope, initial.AsyncId.ToString(), salt, hash, out snapshot));

    }

    [TestMethod]
    public void Retention_MaxPreservTimeIsExtendedBySentPollNbf() {

      DateTime now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
      UjmwLongRunningOptions options = new UjmwLongRunningOptions();
      options.MaxOutcomePreservTime = TimeSpan.FromSeconds(300);
      options.CalculateNbf = (start, last, active) => last.AddSeconds(1000);
      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore(options, () => now);
      ManualResetEventSlim release = new ManualResetEventSlim(false);

      UjmwOperationSnapshot initial = store.Initiate(
        _Scope, (ct, recorder) => { release.Wait(); return new UjmwOperationOutcome("late", false); }, TimeSpan.Zero
      );
      Assert.AreEqual(now.AddSeconds(1000), initial.PollNbf);
      release.Set();
      WaitUntilTerminated(store, 1);

      now = now.AddSeconds(1200); //beyond terminatedAt + max, but within pollNbf + max

      string salt;
      string hash;
      CreatePollingAuth(initial.PollingSecret, out salt, out hash);
      UjmwOperationSnapshot snapshot;
      Assert.IsTrue(store.TryPoll(_Scope, initial.AsyncId.ToString(), salt, hash, out snapshot));
      Assert.AreEqual("late", snapshot.Outcome.Payload);

    }

    [TestMethod]
    public void Retention_AboveCleanupLimit_OldestOutcomesAreDeletedAfterMinPreservTime() {

      DateTime now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
      UjmwLongRunningOptions options = new UjmwLongRunningOptions();
      options.OutcomeCleanupLimit = 2;
      options.MinOutcomePreservTime = TimeSpan.FromSeconds(10);
      UjmwInMemoryOperationStore store = new UjmwInMemoryOperationStore(options, () => now);

      UjmwOperationSnapshot[] initials = new UjmwOperationSnapshot[4];
      for (int i = 0; i < initials.Length; i++) {

        ManualResetEventSlim release = new ManualResetEventSlim(false);
        initials[i] = store.Initiate(
          _Scope, (ct, recorder) => { release.Wait(); return new UjmwOperationOutcome(null, false); }, TimeSpan.Zero
        );
        release.Set();
        WaitUntilTerminated(store, i + 1);
        now = now.AddSeconds(1);

      }

      //min preservation time not yet expired: the buffer may grow beyond the limit
      Assert.AreEqual(4, store.KnownOperationCount);

      now = now.AddSeconds(10);

      string salt;
      string hash;
      CreatePollingAuth(initials[3].PollingSecret, out salt, out hash);
      UjmwOperationSnapshot snapshot;
      Assert.IsTrue(store.TryPoll(_Scope, initials[3].AsyncId.ToString(), salt, hash, out snapshot));

      CreatePollingAuth(initials[0].PollingSecret, out salt, out hash);
      Assert.IsFalse(store.TryPoll(_Scope, initials[0].AsyncId.ToString(), salt, hash, out snapshot));

    }

    [TestMethod]
    public void PollNbf_DefaultCalculation_UsesFibonacciScheduleAndThrottling() {

      UjmwLongRunningOptions options = new UjmwLongRunningOptions();
      DateTime start = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

      Assert.AreEqual(start.AddSeconds(9 + 5), options.EvaluatePollNbf(start, start.AddSeconds(9), 0));
      Assert.AreEqual(start.AddSeconds(9 + 10), options.EvaluatePollNbf(start, start.AddSeconds(9), 100));

      options.ThrottlingLevel = 2;
      Assert.AreEqual(start.AddSeconds(9 + 10), options.EvaluatePollNbf(start, start.AddSeconds(9), 50));

    }

    /// <summary>
    /// Waits until the given number of operations is known and none of them is active anymore.
    /// </summary>
    private static void WaitUntilTerminated(UjmwInMemoryOperationStore store, int expectedKnown) {

      DateTime deadline = DateTime.UtcNow.Add(_Generous);

      while (store.ActiveOperationCount > 0 || store.KnownOperationCount < expectedKnown) {
        Assert.IsTrue(DateTime.UtcNow < deadline, "Operation did not terminate in time.");
        Thread.Sleep(10);
      }

    }

  }

}
#endif
