using Logging.SmartStandards;
using Logging.SmartStandards.CopyForUJMW;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace System.Web.UJMW {

  /// <summary>
  /// Default (in-memory) implementation of the 'IUjmwOperationStore': executes operations on dedicated
  /// background threads (limited by 'MaxConcurrentLongRunningAsyncCalls', further operations are queued),
  /// buffers callback invocations and preserves outcomes according to the retention rules
  /// (see 'doc/epics/epic-long-running-operations.md', chapter 5).
  /// </summary>
  public class UjmwInMemoryOperationStore : IUjmwOperationStore {

    private static readonly DateTime _AsyncIdEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly UjmwLongRunningOptions _Options;
    private readonly Func<DateTime> _UtcNowGetter;

    private readonly object _SyncRoot = new object();
    private readonly Dictionary<long, Operation> _Operations = new Dictionary<long, Operation>();
    private readonly Queue<Operation> _PendingQueue = new Queue<Operation>();
    private int _RunningCount = 0;
    private bool _ShutdownRequested = false;

    /// <summary>
    /// Creates a store using default options.
    /// </summary>
    public UjmwInMemoryOperationStore() : this(new UjmwLongRunningOptions()) {
    }

    /// <summary>
    /// Creates a store using the given options.
    /// </summary>
    public UjmwInMemoryOperationStore(UjmwLongRunningOptions options) : this(options, null) {
    }

    /// <summary>
    /// Creates a store using the given options and an optional clock (for testing purposes).
    /// </summary>
    public UjmwInMemoryOperationStore(UjmwLongRunningOptions options, Func<DateTime> utcNowGetter) {

      if (options == null) {
        options = new UjmwLongRunningOptions();
      }

      if (utcNowGetter == null) {
        utcNowGetter = () => DateTime.UtcNow;
      }

      _Options = options;
      _UtcNowGetter = utcNowGetter;

    }

    /// <summary>
    /// The options used by this store.
    /// </summary>
    public UjmwLongRunningOptions Options {
      get {
        return _Options;
      }
    }

    /// <summary>
    /// Number of operations which are currently queued or running.
    /// </summary>
    public int ActiveOperationCount {
      get {
        lock (_SyncRoot) {
          return _RunningCount + _PendingQueue.Count;
        }
      }
    }

    /// <summary>
    /// Number of operations (active or with a preserved outcome) which are currently known by the store.
    /// </summary>
    public int KnownOperationCount {
      get {
        lock (_SyncRoot) {
          return _Operations.Count;
        }
      }
    }

    #region " IUjmwOperationStore "

    /// <summary>
    /// Initiates a new operation and waits up to the given time for its termination.
    /// </summary>
    public UjmwOperationSnapshot Initiate(string operationScope, UjmwOperationBody body, TimeSpan wait) {

      if (body == null) {
        throw new ArgumentNullException(nameof(body));
      }

      DateTime now = _UtcNowGetter.Invoke();
      Operation operation;

      lock (_SyncRoot) {

        if (_ShutdownRequested) {
          throw new InvalidOperationException("The UJMW operation store has been shut down and does not accept new operations!");
        }

        this.CleanupOutcomes(now);

        long asyncId = this.GenerateUniqueAsyncId(now);
        string pollingSecret = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSecretLength);

        operation = new Operation(asyncId, operationScope, pollingSecret, now, body);
        _Operations.Add(asyncId, operation);
        _PendingQueue.Enqueue(operation);

        this.StartQueuedOperations();

      }

      this.WaitForTermination(operation, wait);

      return this.CreateSnapshot(operation, true);
    }

    /// <summary>
    /// Polls an existing operation (returns false on unknown 'asyncId' or wrong 'pollingHash').
    /// </summary>
    public bool TryPoll(string operationScope, string asyncId, string pollingSalt, string pollingHash, out UjmwOperationSnapshot snapshot) {

      snapshot = null;

      Operation operation;
      if (!this.TryGetAuthenticatedOperation(operationScope, asyncId, pollingSalt, pollingHash, out operation)) {
        return false;
      }

      snapshot = this.CreateSnapshot(operation, false);

      return true;
    }

    /// <summary>
    /// Requests the cancellation of an existing operation (returns false on unknown 'asyncId' or wrong 'pollingHash').
    /// </summary>
    public bool TryCancel(string operationScope, string asyncId, string pollingSalt, string pollingHash, TimeSpan wait, out UjmwOperationSnapshot snapshot) {

      snapshot = null;

      Operation operation;
      if (!this.TryGetAuthenticatedOperation(operationScope, asyncId, pollingSalt, pollingHash, out operation)) {
        return false;
      }

      this.RequestCancellation(operation);

      this.WaitForTermination(operation, wait);

      snapshot = this.CreateSnapshot(operation, false);

      return true;
    }

    /// <summary>
    /// Signals the application shutdown: all operations get their cancellation token triggered
    /// and no further operations are accepted.
    /// </summary>
    public void Shutdown() {

      Operation[] operations;

      lock (_SyncRoot) {
        _ShutdownRequested = true;
        operations = new Operation[_Operations.Count];
        _Operations.Values.CopyTo(operations, 0);
      }

      foreach (Operation operation in operations) {
        this.CancelOperation(operation, false);
      }

    }

    #endregion

    #region " Execution "

    /// <summary>
    /// Starts queued operations as long as free slots are available. Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private void StartQueuedOperations() {

      while (_PendingQueue.Count > 0 && _RunningCount < _Options.MaxConcurrentLongRunningAsyncCalls) {

        Operation operation = _PendingQueue.Dequeue();

        bool start;
        lock (operation) {
          start = (operation.ExecState == UjmwAsyncProtocol._ExecStateQueued);
          if (start) {
            operation.ExecState = UjmwAsyncProtocol._ExecStateRunning;
          }
        }

        if (start) {
          _RunningCount++;
          Task.Factory.StartNew(
            () => this.Execute(operation),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
          );
        }

      }

    }

    /// <summary>
    /// Executes the body of an operation (on a background thread) and records its termination.
    /// </summary>
    private void Execute(Operation operation) {

      UjmwOperationOutcome outcome = null;
      bool cancelled = false;

      try {
        outcome = operation.Body.Invoke(operation.CancellationSource.Token, new CallbackRecorder(this, operation));
      }
      catch (OperationCanceledException) {
        cancelled = true;
      }
      catch (Exception ex) {
        DevLogger.LogError(ex);
        outcome = new UjmwOperationOutcome(null, true);
      }

      if (outcome == null && !cancelled) {
        outcome = new UjmwOperationOutcome(null, false);
      }

      DateTime now = _UtcNowGetter.Invoke();

      lock (operation) {

        operation.Outcome = outcome;
        operation.TerminatedAt = now;

        if (cancelled || operation.CancellationRequested) {
          operation.ExecState = UjmwAsyncProtocol._ExecStateCancelled;
        }
        else if (outcome.Failed) {
          operation.ExecState = UjmwAsyncProtocol._ExecStateFailed;
        }
        else {
          operation.ExecState = UjmwAsyncProtocol._ExecStateCompleted;
        }

        Monitor.PulseAll(operation);

      }

      operation.CancellationSource.Dispose();

      lock (_SyncRoot) {
        _RunningCount--;
        this.StartQueuedOperations();
      }

    }

    /// <summary>
    /// Requests the cancellation of the given operation (explicit DELETE request).
    /// </summary>
    private void RequestCancellation(Operation operation) {
      this.CancelOperation(operation, true);
    }

    /// <summary>
    /// Triggers the cancellation of an operation. Queued operations are terminated immediately.
    /// </summary>
    private void CancelOperation(Operation operation, bool explicitRequest) {

      bool triggerToken = false;
      bool removeFromQueue = false;

      lock (operation) {

        if (UjmwAsyncProtocol.IsTerminalExecState(operation.ExecState)) {
          return;
        }

        if (explicitRequest) {
          operation.CancellationRequested = true;
        }

        if (operation.ExecState == UjmwAsyncProtocol._ExecStateQueued) {
          //it will never be started (StartQueuedOperations skips non-queued entries)
          operation.ExecState = UjmwAsyncProtocol._ExecStateCancelled;
          operation.TerminatedAt = _UtcNowGetter.Invoke();
          Monitor.PulseAll(operation);
          removeFromQueue = true;
        }
        else {
          if (explicitRequest) {
            operation.ExecState = UjmwAsyncProtocol._ExecStateCancellationRequested;
          }
          triggerToken = true;
        }

      }

      if (removeFromQueue) {
        //(locked separately to keep the lock order '_SyncRoot' -> 'operation' everywhere)
        lock (_SyncRoot) {
          Operation[] pending = _PendingQueue.ToArray();
          _PendingQueue.Clear();
          foreach (Operation pendingOperation in pending) {
            if (!object.ReferenceEquals(pendingOperation, operation)) {
              _PendingQueue.Enqueue(pendingOperation);
            }
          }
        }
      }

      if (triggerToken) {
        try {
          operation.CancellationSource.Cancel();
        }
        catch (ObjectDisposedException) {
          //the operation has terminated concurrently - nothing left to cancel
        }
      }

    }

    /// <summary>
    /// Blocks until the operation has terminated or the wait time has elapsed.
    /// </summary>
    private void WaitForTermination(Operation operation, TimeSpan wait) {

      if (wait <= TimeSpan.Zero) {
        return;
      }

      DateTime deadline = DateTime.UtcNow.Add(wait);

      lock (operation) {

        while (!UjmwAsyncProtocol.IsTerminalExecState(operation.ExecState)) {

          TimeSpan remaining = deadline - DateTime.UtcNow;
          if (remaining <= TimeSpan.Zero) {
            return;
          }

          Monitor.Wait(operation, remaining);

        }

      }

    }

    #endregion

    #region " Callbacks "

    /// <summary>
    /// Buffers a callback invocation (blocks on a full buffer, throws on a persisting overflow).
    /// </summary>
    private void RecordCallback(Operation operation, string callbackName, object[] arguments) {

      DateTime deadline = DateTime.UtcNow.Add(_Options.FreezeTimeBeforeCallbackBufferOverflow);

      lock (operation) {

        while (operation.CallbackBuffer.Count >= _Options.CallbackBufferLimit) {

          TimeSpan remaining = deadline - DateTime.UtcNow;
          if (remaining <= TimeSpan.Zero) {
            throw new UjmwCallbackBufferOverflowException(
              $"The UJMW callback buffer of operation '{operation.AsyncId}' is full ({_Options.CallbackBufferLimit} invocations) and has not been drained by a poll within {_Options.FreezeTimeBeforeCallbackBufferOverflow.TotalSeconds} seconds!"
            );
          }

          Monitor.Wait(operation, remaining);

        }

        operation.LastCallbackSequence++;
        operation.CallbackBuffer.Add(new UjmwCallbackInvocation(operation.LastCallbackSequence, callbackName, arguments));

      }

    }

    private class CallbackRecorder : IUjmwCallbackRecorder {

      private readonly UjmwInMemoryOperationStore _Store;
      private readonly Operation _Operation;

      public CallbackRecorder(UjmwInMemoryOperationStore store, Operation operation) {
        _Store = store;
        _Operation = operation;
      }

      /// <summary>
      /// Records one invocation of the callback parameter with the given name.
      /// </summary>
      public void Record(string callbackName, object[] arguments) {
        _Store.RecordCallback(_Operation, callbackName, arguments);
      }

    }

    #endregion

    #region " Snapshots & Retention "

    /// <summary>
    /// Creates a snapshot which drains the callback buffer (and the outcome, if terminated).
    /// </summary>
    private UjmwOperationSnapshot CreateSnapshot(Operation operation, bool isInitiatingRequest) {

      DateTime now = _UtcNowGetter.Invoke();
      int activeOperations = this.ActiveOperationCount;

      UjmwOperationSnapshot snapshot;
      bool terminal;

      lock (operation) {

        UjmwCallbackInvocation[] drainedInvocations = operation.CallbackBuffer.ToArray();
        operation.CallbackBuffer.Clear();
        Monitor.PulseAll(operation); //wake up callbacks which are blocked on a full buffer

        terminal = UjmwAsyncProtocol.IsTerminalExecState(operation.ExecState);

        if (terminal) {

          long exposedAsyncId = operation.AsyncId;
          if (isInitiatingRequest) {
            //completed within the wait time: there is nothing left to poll
            exposedAsyncId = 0;
          }

          snapshot = new UjmwOperationSnapshot(
            exposedAsyncId, operation.ExecState, false, DateTime.MinValue, null, operation.Outcome, drainedInvocations
          );

        }
        else {

          DateTime pollNbf = _Options.EvaluatePollNbf(operation.StartTime, now, activeOperations);
          if (pollNbf > operation.LatestSentPollNbf) {
            operation.LatestSentPollNbf = pollNbf;
          }

          bool cancellable = (
            !operation.CancellationRequested &&
            (operation.ExecState == UjmwAsyncProtocol._ExecStateQueued || operation.ExecState == UjmwAsyncProtocol._ExecStateRunning)
          );

          string pollingSecret = null;
          if (isInitiatingRequest) {
            pollingSecret = operation.PollingSecret;
          }

          snapshot = new UjmwOperationSnapshot(
            operation.AsyncId, operation.ExecState, cancellable, pollNbf, pollingSecret, null, drainedInvocations
          );

        }

      }

      if (terminal) {
        //single consumer: the outcome has been drained by this request
        lock (_SyncRoot) {
          _Operations.Remove(operation.AsyncId);
        }
      }

      return snapshot;
    }

    /// <summary>
    /// Resolves the operation and verifies the polling authentication (without draining anything).
    /// </summary>
    private bool TryGetAuthenticatedOperation(string operationScope, string asyncId, string pollingSalt, string pollingHash, out Operation operation) {

      operation = null;

      long parsedAsyncId;
      if (string.IsNullOrWhiteSpace(asyncId) || !long.TryParse(asyncId, out parsedAsyncId)) {
        return false;
      }

      lock (_SyncRoot) {
        this.CleanupOutcomes(_UtcNowGetter.Invoke());
        if (!_Operations.TryGetValue(parsedAsyncId, out operation)) {
          return false;
        }
      }

      if (!string.Equals(operation.OperationScope, operationScope, StringComparison.Ordinal)) {
        operation = null;
        return false;
      }

      if (!UjmwAsyncProtocol.VerifyPollingHash(operation.PollingSecret, pollingSalt, pollingHash)) {
        operation = null;
        return false;
      }

      return true;
    }

    /// <summary>
    /// Applies the outcome retention rules. Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private void CleanupOutcomes(DateTime now) {

      List<Operation> terminatedOperations = new List<Operation>();

      foreach (Operation operation in _Operations.Values) {
        lock (operation) {
          if (UjmwAsyncProtocol.IsTerminalExecState(operation.ExecState)) {
            terminatedOperations.Add(operation);
          }
        }
      }

      //rule 1: the maximum lifetime (extended by a sent 'pollNbf') applies independently of any limit
      List<Operation> remainingOperations = new List<Operation>();
      foreach (Operation operation in terminatedOperations) {

        DateTime expiry = operation.TerminatedAt.Add(_Options.MaxOutcomePreservTime);
        DateTime extendedExpiry = operation.LatestSentPollNbf.Add(_Options.MaxOutcomePreservTime);
        if (operation.LatestSentPollNbf > DateTime.MinValue && extendedExpiry > expiry) {
          expiry = extendedExpiry;
        }

        if (now > expiry) {
          _Operations.Remove(operation.AsyncId);
        }
        else {
          remainingOperations.Add(operation);
        }

      }

      //rule 2: above the limit, delete the oldest outcomes - but only after their minimum preservation time
      if (remainingOperations.Count <= _Options.OutcomeCleanupLimit) {
        return;
      }

      remainingOperations.Sort((a, b) => a.TerminatedAt.CompareTo(b.TerminatedAt));

      int excess = remainingOperations.Count - _Options.OutcomeCleanupLimit;
      foreach (Operation operation in remainingOperations) {

        if (excess <= 0) {
          break;
        }

        if (now > operation.TerminatedAt.Add(_Options.MinOutcomePreservTime)) {
          _Operations.Remove(operation.AsyncId);
          excess--;
        }

      }

    }

    /// <summary>
    /// Generates a 53-bit 'asyncId' (44-bit UTC millisecond time stamp + 9 random bits)
    /// which does not collide with a known operation. Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private long GenerateUniqueAsyncId(DateTime now) {

      long milliseconds = (long)(now - _AsyncIdEpoch).TotalMilliseconds;
      byte[] buffer = new byte[2];

      using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) {
        while (true) {

          rng.GetBytes(buffer);
          long random = ((buffer[0] << 8) | buffer[1]) & 0x1FF;
          long asyncId = ((milliseconds & 0xFFFFFFFFFFF) << 9) | random;

          if (asyncId != 0 && !_Operations.ContainsKey(asyncId)) {
            return asyncId;
          }

          //all 512 ids of this millisecond could be taken - so move on to the next one
          if (_Operations.Count > 256) {
            milliseconds++;
          }

        }
      }

    }

    #endregion

    private class Operation {

      public Operation(long asyncId, string operationScope, string pollingSecret, DateTime startTime, UjmwOperationBody body) {
        this.AsyncId = asyncId;
        this.OperationScope = operationScope;
        this.PollingSecret = pollingSecret;
        this.StartTime = startTime;
        this.Body = body;
      }

      public readonly long AsyncId;
      public readonly string OperationScope;
      public readonly string PollingSecret;
      public readonly DateTime StartTime;
      public readonly UjmwOperationBody Body;
      public readonly CancellationTokenSource CancellationSource = new CancellationTokenSource();
      public readonly List<UjmwCallbackInvocation> CallbackBuffer = new List<UjmwCallbackInvocation>();

      public int ExecState = UjmwAsyncProtocol._ExecStateQueued;
      public bool CancellationRequested = false;
      public long LastCallbackSequence = 0;
      public UjmwOperationOutcome Outcome = null;
      public DateTime TerminatedAt = DateTime.MinValue;
      public DateTime LatestSentPollNbf = DateTime.MinValue;

    }

  }

}
