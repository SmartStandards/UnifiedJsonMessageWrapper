using Logging.SmartStandards;
using Logging.SmartStandards.CopyForUJMW;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// Default (in-memory, short-term, non-durable) implementation of the 'IUjmwQueuingService'.
  /// Manages one FIFO queue per contract type + resolved controller route, two-phase allocations
  /// (dequeue + confirming heartbeat), leases, callback buffering, cancellation, releases and statistics
  /// (see 'doc/epics/epic-async-queue-mode.md', chapters 4 and 5).
  /// </summary>
  public class UjmwInMemoryQueuingService : IUjmwQueuingService {

    private static readonly DateTime _IdEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan _StatsWindow = TimeSpan.FromHours(10);
    private static readonly TimeSpan _MaxWaitSlice = TimeSpan.FromMilliseconds(250);

    private readonly UjmwQueueOptions _Options;
    private readonly Func<DateTime> _UtcNowGetter;

    private readonly object _SyncRoot = new object();
    private readonly Dictionary<string, QueueState> _Queues = new Dictionary<string, QueueState>();
    private readonly Dictionary<long, Entry> _Entries = new Dictionary<long, Entry>();
    private bool _ShutdownRequested = false;

    /// <summary>
    /// Creates a queuing service using default options.
    /// </summary>
    public UjmwInMemoryQueuingService() : this(new UjmwQueueOptions()) {
    }

    /// <summary>
    /// Creates a queuing service using the given options.
    /// </summary>
    public UjmwInMemoryQueuingService(UjmwQueueOptions options) : this(options, null) {
    }

    /// <summary>
    /// Creates a queuing service using the given options and an optional clock (for testing purposes).
    /// </summary>
    public UjmwInMemoryQueuingService(UjmwQueueOptions options, Func<DateTime> utcNowGetter) {

      if (options == null) {
        options = new UjmwQueueOptions();
      }

      if (utcNowGetter == null) {
        utcNowGetter = () => DateTime.UtcNow;
      }

      _Options = options;
      _UtcNowGetter = utcNowGetter;

    }

    /// <summary>
    /// The options used by this service.
    /// </summary>
    public UjmwQueueOptions Options {
      get {
        return _Options;
      }
    }

    #region " Queue Registration "

    /// <summary>
    /// Registers a queue (contract type + resolved controller route). Queues are also created on demand,
    /// but an explicit registration is required to detect ambiguous 'queueName == null' accesses.
    /// </summary>
    public void RegisterQueue(Type contractType, string queueName) {

      if (contractType == null) {
        throw new ArgumentNullException(nameof(contractType));
      }

      lock (_SyncRoot) {
        this.GetOrCreateQueue(contractType, queueName);
      }

    }

    /// <summary>
    /// Signals the host shutdown: new enqueues are rejected and cancellation requests are no longer
    /// forwarded to leased entries (running processor jobs are never cancelled because of a host shutdown).
    /// </summary>
    public void Shutdown() {

      lock (_SyncRoot) {
        _ShutdownRequested = true;
        Monitor.PulseAll(_SyncRoot);
      }

    }

    #endregion

    #region " IUjmwQueuingService (Caller Side) "

    /// <summary>
    /// Enqueues a UJMW request (wire format JSON incl. sidechannel) for the given transport-level method name.
    /// </summary>
    public UjmwQueueTicket Enqueue(Type contractType, string queueName, string methodName, string requestJson, bool isLongRunning) {

      if (contractType == null) {
        throw new ArgumentNullException(nameof(contractType));
      }

      if (string.IsNullOrWhiteSpace(methodName)) {
        throw new ArgumentNullException(nameof(methodName));
      }

      UjmwQueueTicket ticket = new UjmwQueueTicket();
      DateTime now = _UtcNowGetter.Invoke();

      lock (_SyncRoot) {

        if (_ShutdownRequested) {
          ticket.Fault = UjmwQueueProtocol.FaultHostShuttingDown;
          return ticket;
        }

        string fault;
        QueueState queue = this.ResolveQueue(contractType, queueName, true, out fault);
        if (queue == null) {
          ticket.Fault = fault;
          return ticket;
        }

        this.Housekeeping(now);

        if (queue.Waiting.Count >= _Options.MaxQueueSize) {
          ticket.Fault = UjmwQueueProtocol.FaultCapacityExceeded;
          return ticket;
        }

        long entryId = this.GenerateUniqueId(now);
        string callerSecret = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSecretLength);

        Entry entry = new Entry(entryId, queue, methodName, requestJson, isLongRunning, callerSecret, now);
        _Entries.Add(entryId, entry);
        queue.Waiting.Add(entry);

        ticket.EntryId = entryId.ToString();
        ticket.PollingSecret = callerSecret;

        Monitor.PulseAll(_SyncRoot);
      }

      return ticket;
    }

    /// <summary>
    /// Returns the current state of an entry (drains the buffered callback invocations).
    /// Waits up to 'maxWait' for the termination of the entry or for new callback invocations.
    /// </summary>
    public bool TryGetState(
      Type contractType, string queueName, string entryId, string pollingSalt, string pollingHash, TimeSpan maxWait,
      out UjmwQueueEntryState state
    ) {

      state = null;

      lock (_SyncRoot) {

        Entry entry = this.FindCallerAuthenticatedEntry(contractType, entryId, pollingSalt, pollingHash);
        if (entry == null) {
          return false;
        }

        this.WaitWhile(maxWait, () => (entry.Phase != EntryPhase.Terminated && entry.CallbackBuffer.Count == 0));

        state = this.CreateCallerState(entry);
      }

      return true;
    }

    /// <summary>
    /// Requests the cancellation of an entry. A queued (or not yet confirmed) entry is cancelled immediately,
    /// a leased entry is flagged ('cancellationRequested' on the next heartbeat). Waits up to 'maxWait' for the termination.
    /// </summary>
    public bool TryRequestCancel(
      Type contractType, string queueName, string entryId, string pollingSalt, string pollingHash, TimeSpan maxWait,
      out UjmwQueueEntryState state
    ) {

      state = null;

      lock (_SyncRoot) {

        Entry entry = this.FindCallerAuthenticatedEntry(contractType, entryId, pollingSalt, pollingHash);
        if (entry == null) {
          return false;
        }

        DateTime now = _UtcNowGetter.Invoke();

        if (entry.Phase == EntryPhase.Queued || entry.Phase == EntryPhase.Allocated) {

          entry.Queue.Waiting.Remove(entry);
          this.Terminate(entry, UjmwAsyncProtocol._ExecStateCancelled, null, null, now);

        }
        else if (entry.Phase == EntryPhase.Leased && !_ShutdownRequested) {

          entry.CancellationRequested = true;
          Monitor.PulseAll(_SyncRoot);

        }

        this.WaitWhile(maxWait, () => (entry.Phase != EntryPhase.Terminated));

        state = this.CreateCallerState(entry);
      }

      return true;
    }

    #endregion

    #region " IUjmwQueue (Processor Side) "

    /// <summary>
    /// Returns statistics about the queue of the given contract (no side effects).
    /// </summary>
    public UjmwQueueInfo GetInfo(Type contractType, string queueName) {

      UjmwQueueInfo info = new UjmwQueueInfo();
      DateTime now = _UtcNowGetter.Invoke();

      lock (_SyncRoot) {

        string fault;
        QueueState queue = this.ResolveQueue(contractType, queueName, true, out fault);
        if (queue == null) {
          info.Fault = fault;
          return info;
        }

        this.Housekeeping(now);

        foreach (Entry waitingEntry in queue.Waiting) {
          this.GetStats(info, waitingEntry.MethodName).Queued++;
          info.StatsTotal.Queued++;
        }

        foreach (Entry activeEntry in _Entries.Values) {

          bool isActive = (activeEntry.Phase == EntryPhase.Allocated || activeEntry.Phase == EntryPhase.Leased);
          if (activeEntry.Queue != queue || !isActive) {
            continue;
          }

          this.GetStats(info, activeEntry.MethodName).Active++;
          info.StatsTotal.Active++;

          UjmwQueueProcessorInfo processorInfo;
          if (activeEntry.ProcessorId != null && info.Processors.TryGetValue(activeEntry.ProcessorId, out processorInfo)) {
            processorInfo.Active++;
          }
          else if (activeEntry.ProcessorId != null) {
            processorInfo = new UjmwQueueProcessorInfo();
            processorInfo.Active = 1;
            info.Processors[activeEntry.ProcessorId] = processorInfo;
          }

        }

        foreach (KeyValuePair<string, DateTime> processor in queue.ProcessorActivity) {

          UjmwQueueProcessorInfo processorInfo;
          if (!info.Processors.TryGetValue(processor.Key, out processorInfo)) {
            processorInfo = new UjmwQueueProcessorInfo();
            info.Processors[processor.Key] = processorInfo;
          }

          processorInfo.LastActivity = DateTime.SpecifyKind(processor.Value, DateTimeKind.Utc);

        }

        foreach (KeyValuePair<string, int> capacity in queue.ProcessorCapacity) {

          UjmwQueueProcessorInfo processorInfo;
          if (info.Processors.TryGetValue(capacity.Key, out processorInfo)) {
            processorInfo.Capacity = capacity.Value;
          }

        }

        foreach (Termination termination in queue.Terminations) {

          TimeSpan age = now - termination.Time;
          UjmwQueueStats methodStats = this.GetStats(info, termination.MethodName);

          this.CountTermination(methodStats, age);
          this.CountTermination(info.StatsTotal, age);

        }

      }

      return info;
    }

    /// <summary>
    /// Allocates the next matching entry for the given processor (must be confirmed by a first heartbeat).
    /// </summary>
    public UjmwQueueLease Dequeue(Type contractType, string queueName, string processorId, UjmwQueueMethodFilter methodFilter, int capacity) {

      DateTime now = _UtcNowGetter.Invoke();

      lock (_SyncRoot) {

        string fault;
        QueueState queue = this.ResolveQueue(contractType, queueName, true, out fault);
        if (queue == null) {
          UjmwQueueLease faultLease = new UjmwQueueLease();
          faultLease.Fault = fault;
          return faultLease;
        }

        this.Housekeeping(now);

        this.StoreProcessorCapacity(queue, processorId, capacity);

        return this.AllocateNext(queue, processorId, methodFilter, now);
      }

    }

    /// <summary>
    /// Heartbeat of an allocated / leased entry (the first one confirms the allocation).
    /// </summary>
    public UjmwQueueHeartbeatResult Heartbeat(
      Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash,
      string callbackInvocationsJson
    ) {

      UjmwQueueHeartbeatResult result = new UjmwQueueHeartbeatResult();
      DateTime now = _UtcNowGetter.Invoke();

      lock (_SyncRoot) {

        this.Housekeeping(now);

        Entry entry;
        string lateFault;
        if (!this.TryGetLeaseAuthenticatedEntry(contractType, requestId, pollingSalt, pollingHash, out entry, out lateFault)) {

          if (lateFault != null) {
            result.CancellationRequested = true;
          }
          else {
            lateFault = UjmwQueueProtocol.FaultWrongRequestIdOrHash;
          }

          result.Fault = lateFault;
          return result;
        }

        this.TouchProcessor(entry.Queue, processorId, now);
        entry.ProcessorId = processorId;

        if (entry.Phase == EntryPhase.Allocated) {
          entry.Phase = EntryPhase.Leased;
        }

        entry.LastHeartbeat = now;

        if (!string.IsNullOrWhiteSpace(callbackInvocationsJson)) {

          string bufferFault = this.AppendCallbackInvocations(entry, callbackInvocationsJson);
          if (bufferFault != null) {
            result.Fault = bufferFault;
          }

        }

        result.CancellationRequested = entry.CancellationRequested;

        Monitor.PulseAll(_SyncRoot);
      }

      return result;
    }

    /// <summary>
    /// Returns an allocated / leased entry to the queue (or confirms a requested cancellation).
    /// </summary>
    public UjmwQueueResult Return(Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash) {

      UjmwQueueResult result = new UjmwQueueResult();
      DateTime now = _UtcNowGetter.Invoke();

      lock (_SyncRoot) {

        this.Housekeeping(now);

        Entry entry;
        string lateFault;
        if (!this.TryGetLeaseAuthenticatedEntry(contractType, requestId, pollingSalt, pollingHash, out entry, out lateFault)) {

          if (lateFault == null) {
            lateFault = UjmwQueueProtocol.FaultWrongRequestIdOrHash;
          }

          result.Fault = lateFault;
          return result;
        }

        this.TouchProcessor(entry.Queue, processorId, now);

        if (entry.CancellationRequested) {
          this.Terminate(entry, UjmwAsyncProtocol._ExecStateCancelled, null, null, now);
          return result;
        }

        entry.ReleaseCount++;

        if (entry.ReleaseCount > UjmwQueueProtocol.MaxReleases) {
          this.Terminate(entry, UjmwAsyncProtocol._ExecStateFailed, null, UjmwQueueProtocol.FaultReleaseLimit, now);
          return result;
        }

        this.Requeue(entry, UjmwQueueProtocol.GetReinsertionIndex(entry.ReleaseCount));

        Monitor.PulseAll(_SyncRoot);
      }

      return result;
    }

    /// <summary>
    /// Delivers the final UJMW response and optionally allocates the next matching entry ('dequeueNext').
    /// </summary>
    public UjmwQueueLease Complete(
      Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash,
      string responseJson, UjmwQueueMethodFilter dequeueNext, int capacity
    ) {

      DateTime now = _UtcNowGetter.Invoke();

      lock (_SyncRoot) {

        this.Housekeeping(now);

        Entry entry;
        string lateFault;
        if (!this.TryGetLeaseAuthenticatedEntry(contractType, requestId, pollingSalt, pollingHash, out entry, out lateFault)) {

          if (lateFault == null) {
            lateFault = UjmwQueueProtocol.FaultWrongRequestIdOrHash;
          }

          UjmwQueueLease rejected = new UjmwQueueLease();
          rejected.Fault = lateFault;
          return rejected;
        }

        this.TouchProcessor(entry.Queue, processorId, now);
        this.StoreProcessorCapacity(entry.Queue, processorId, capacity);

        int execState = UjmwAsyncProtocol._ExecStateCompleted;
        if (HasBusinessFault(responseJson)) {
          execState = UjmwAsyncProtocol._ExecStateFailed;
        }

        this.Terminate(entry, execState, responseJson, null, now);

        if (dequeueNext == null) {
          return new UjmwQueueLease();
        }

        return this.AllocateNext(entry.Queue, processorId, dequeueNext, now);
      }

    }

    #endregion

    #region " Internals "

    /// <summary>
    /// Allocates the front-most entry matching the filter. Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private UjmwQueueLease AllocateNext(QueueState queue, string processorId, UjmwQueueMethodFilter methodFilter, DateTime now) {

      UjmwQueueLease lease = new UjmwQueueLease();

      if (methodFilter == null) {
        methodFilter = UjmwQueueMethodFilter.All;
      }

      this.TouchProcessor(queue, processorId, now);

      Entry selected = null;
      foreach (Entry candidate in queue.Waiting) {
        if (methodFilter.IsMatch(candidate.MethodName)) {
          selected = candidate;
          break;
        }
      }

      if (selected == null) {
        lease.TotalInQueue = queue.Waiting.Count;
        return lease;
      }

      queue.Waiting.Remove(selected);

      selected.Phase = EntryPhase.Allocated;
      selected.ProcessorId = processorId;
      selected.AllocatedAt = now;
      selected.LastHeartbeat = now;
      selected.LeaseSecret = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSecretLength);

      lease.TotalInQueue = queue.Waiting.Count;
      lease.MethodName = selected.MethodName;
      lease.RequestId = selected.Id.ToString();
      lease.RequestJson = selected.RequestJson;
      lease.PollingSecret = selected.LeaseSecret;
      lease.HeartbeatSec = _Options.HeartbeatSec;

      Monitor.PulseAll(_SyncRoot);

      return lease;
    }

    /// <summary>
    /// Puts an allocated / leased entry back into the waiting list. Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private void Requeue(Entry entry, int index) {

      if (entry.LeaseSecret != null) {
        entry.FormerLeaseSecrets.Add(entry.LeaseSecret);
      }

      entry.LeaseSecret = null;
      entry.Phase = EntryPhase.Queued;
      entry.ProcessorId = null;

      List<Entry> waiting = entry.Queue.Waiting;
      if (index > waiting.Count) {
        index = waiting.Count;
      }

      waiting.Insert(index, entry);

    }

    /// <summary>
    /// Terminates an entry. Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private void Terminate(Entry entry, int execState, string responseJson, string fault, DateTime now) {

      entry.Phase = EntryPhase.Terminated;
      entry.ExecState = execState;
      entry.ResponseJson = responseJson;
      entry.Fault = fault;
      entry.TerminatedAt = now;

      entry.Queue.Terminations.Add(new Termination(entry.MethodName, now));

      Monitor.PulseAll(_SyncRoot);

    }

    /// <summary>
    /// Withdraws unconfirmed allocations, handles lost leases and evicts old outcomes / statistics.
    /// Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private void Housekeeping(DateTime now) {

      UjmwLongRunningOptions retention = _Options.LongRunningOptions;
      List<long> evictions = new List<long>();

      foreach (Entry entry in _Entries.Values.ToArray()) {

        if (entry.Phase == EntryPhase.Allocated) {

          if ((now - entry.AllocatedAt).TotalSeconds > _Options.DequeueConfirmationTimeoutSec) {
            //guaranteed never executed - always redelivered with priority (does not count as attempt)
            this.Requeue(entry, 0);
          }

        }
        else if (entry.Phase == EntryPhase.Leased) {

          if ((now - entry.LastHeartbeat).TotalSeconds > _Options.LeaseLossTimeoutSec) {
            this.HandleLeaseLoss(entry, now);
          }

        }
        else if (entry.Phase == EntryPhase.Terminated) {

          TimeSpan age = now - entry.TerminatedAt;
          bool expired = (age > retention.MaxOutcomePreservTime);
          bool deliveredAndOld = (entry.TerminalStateDelivered && age > retention.MinOutcomePreservTime);

          if (expired || deliveredAndOld) {
            evictions.Add(entry.Id);
          }

        }

      }

      foreach (long id in evictions) {
        _Entries.Remove(id);
      }

      foreach (QueueState queue in _Queues.Values) {
        queue.Terminations.RemoveAll((termination) => (now - termination.Time) > _StatsWindow);
        this.RemoveStaleProcessors(queue, now);
      }

    }

    /// <summary>
    /// Removes processor instances without activity for 'StaleProcessorRetentionSec', but only if another instance
    /// with the same agent name (part before the first '#') is alive and the stale instance holds no entries.
    /// Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private void RemoveStaleProcessors(QueueState queue, DateTime now) {

      if (queue.ProcessorActivity.Count < 2) {
        return;
      }

      HashSet<string> aliveAgents = new HashSet<string>(StringComparer.Ordinal);
      List<string> staleProcessors = new List<string>();

      foreach (KeyValuePair<string, DateTime> processor in queue.ProcessorActivity) {

        double idleSeconds = (now - processor.Value).TotalSeconds;

        if (idleSeconds <= _Options.ProcessorAliveSec) {
          aliveAgents.Add(UjmwQueueProtocol.GetAgentName(processor.Key));
        }
        else if (idleSeconds > _Options.StaleProcessorRetentionSec) {
          staleProcessors.Add(processor.Key);
        }

      }

      foreach (string processorId in staleProcessors) {

        if (!aliveAgents.Contains(UjmwQueueProtocol.GetAgentName(processorId))) {
          continue;
        }

        bool holdsEntries = _Entries.Values.Any((entry) => (
          entry.Queue == queue && entry.ProcessorId == processorId &&
          (entry.Phase == EntryPhase.Allocated || entry.Phase == EntryPhase.Leased)
        ));
        if (holdsEntries) {
          continue;
        }

        queue.ProcessorActivity.Remove(processorId);
        queue.ProcessorCapacity.Remove(processorId);
      }

    }

    /// <summary>
    /// Applies the redelivery policy to an entry whose confirmed lease was lost. Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private void HandleLeaseLoss(Entry entry, DateTime now) {

      entry.LeaseLossCount++;

      DevLogger.LogWarning(
        2091845400101, 72400,
        $"UJMW queue entry {entry.Id} ('{entry.MethodName}') lost its lease (processor '{entry.ProcessorId}', attempt {entry.LeaseLossCount})."
      );

      bool redeliver = false;
      try {
        redeliver = _Options.EvaluateRedelivery(
          entry.Queue.ContractType, entry.Queue.QueueName, entry.MethodName, entry.LeaseLossCount, UjmwQueueLeaseLossReason.HeartbeatTimeout
        );
      }
      catch (Exception ex) {
        DevLogger.LogError(ex);
      }

      if (redeliver && !entry.CancellationRequested) {
        this.Requeue(entry, 0);
        return;
      }

      if (entry.LeaseSecret != null) {
        entry.FormerLeaseSecrets.Add(entry.LeaseSecret);
        entry.LeaseSecret = null;
      }

      this.Terminate(entry, UjmwAsyncProtocol._ExecStateFailed, null, UjmwQueueProtocol.FaultProcessorLost, now);

    }

    /// <summary>
    /// Appends the callback invocations of a heartbeat to the entry-scoped buffer (all or nothing).
    /// Returns a fault if the buffer is full or the JSON is invalid. Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private string AppendCallbackInvocations(Entry entry, string callbackInvocationsJson) {

      JObject invocations;
      try {
        invocations = JObject.Parse(callbackInvocationsJson);
      }
      catch (JsonException ex) {
        DevLogger.LogError(ex);
        return "Invalid callback invocations JSON";
      }

      List<BufferedInvocation> incoming = new List<BufferedInvocation>();

      foreach (JProperty property in invocations.Properties()) {

        JArray items = property.Value as JArray;
        if (items == null) {
          continue;
        }

        foreach (JToken item in items) {

          JObject invocation = item as JObject;
          if (invocation == null) {
            continue;
          }

          long sequence = 0;
          JToken sequenceToken = invocation[UjmwAsyncProtocol._CallbackSequencePropertyName];
          if (sequenceToken != null && sequenceToken.Type == JTokenType.Integer) {
            sequence = sequenceToken.Value<long>();
          }

          incoming.Add(new BufferedInvocation(property.Name, sequence, invocation));
        }

      }

      int limit = _Options.LongRunningOptions.CallbackBufferLimit;
      if (entry.CallbackBuffer.Count > 0 && entry.CallbackBuffer.Count + incoming.Count > limit) {
        return UjmwQueueProtocol.FaultCallbackBufferFull;
      }

      entry.CallbackBuffer.AddRange(incoming.OrderBy((invocation) => invocation.Sequence));

      return null;
    }

    /// <summary>
    /// Creates the caller-side state and drains the callback buffer. Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private UjmwQueueEntryState CreateCallerState(Entry entry) {

      UjmwQueueEntryState state = new UjmwQueueEntryState();

      if (entry.Phase == EntryPhase.Terminated) {
        state.ExecState = entry.ExecState;
        state.ResponseJson = entry.ResponseJson;
        state.Fault = entry.Fault;
        entry.TerminalStateDelivered = true;
      }
      else if (entry.Phase == EntryPhase.Leased && entry.CancellationRequested) {
        state.ExecState = UjmwAsyncProtocol._ExecStateCancellationRequested;
      }
      else if (entry.Phase == EntryPhase.Leased) {
        state.ExecState = UjmwAsyncProtocol._ExecStateRunning;
      }
      else {
        state.ExecState = UjmwAsyncProtocol._ExecStateQueued;
      }

      if (entry.CallbackBuffer.Count > 0) {

        JObject drained = new JObject();
        foreach (BufferedInvocation invocation in entry.CallbackBuffer) {

          JArray items = drained[invocation.PropertyName] as JArray;
          if (items == null) {
            items = new JArray();
            drained[invocation.PropertyName] = items;
          }

          items.Add(invocation.Invocation);
        }

        entry.CallbackBuffer.Clear();
        state.CallbackInvocationsJson = drained.ToString(Formatting.None);

      }

      return state;
    }

    /// <summary>
    /// Waits (releasing the lock) while the condition is true, but at most the given time.
    /// Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private void WaitWhile(TimeSpan maxWait, Func<bool> condition) {

      if (maxWait <= TimeSpan.Zero) {
        return;
      }

      DateTime deadline = DateTime.UtcNow.Add(maxWait);

      while (condition.Invoke()) {

        TimeSpan remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) {
          return;
        }

        if (remaining > _MaxWaitSlice) {
          remaining = _MaxWaitSlice;
        }

        Monitor.Wait(_SyncRoot, remaining);

        this.Housekeeping(_UtcNowGetter.Invoke());

      }

    }

    /// <summary>
    /// Returns the entry if the caller-side polling hash is valid (otherwise null). Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private Entry FindCallerAuthenticatedEntry(Type contractType, string entryId, string pollingSalt, string pollingHash) {

      this.Housekeeping(_UtcNowGetter.Invoke());

      Entry entry = this.FindEntry(contractType, entryId);
      if (entry == null) {
        return null;
      }

      if (!UjmwAsyncProtocol.VerifyPollingHash(entry.CallerSecret, pollingSalt, pollingHash)) {
        return null;
      }

      return entry;
    }

    /// <summary>
    /// Validates the lease (processor side). For allocations which were withdrawn or leases which were lost,
    /// 'lateFault' describes the reason (the processor must discard the job). Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private bool TryGetLeaseAuthenticatedEntry(
      Type contractType, string requestId, string pollingSalt, string pollingHash, out Entry entry, out string lateFault
    ) {

      lateFault = null;
      entry = this.FindEntry(contractType, requestId);

      if (entry == null) {
        return false;
      }

      bool isActive = (entry.Phase == EntryPhase.Allocated || entry.Phase == EntryPhase.Leased);
      if (isActive && UjmwAsyncProtocol.VerifyPollingHash(entry.LeaseSecret, pollingSalt, pollingHash)) {
        return true;
      }

      foreach (string formerSecret in entry.FormerLeaseSecrets) {
        if (UjmwAsyncProtocol.VerifyPollingHash(formerSecret, pollingSalt, pollingHash)) {

          if (entry.Phase == EntryPhase.Terminated && entry.Fault == UjmwQueueProtocol.FaultProcessorLost) {
            lateFault = UjmwQueueProtocol.FaultProcessorLost;
          }
          else {
            lateFault = UjmwQueueProtocol.FaultConfirmationMissed;
          }

          entry = null;
          return false;
        }
      }

      if (entry.Phase == EntryPhase.Terminated && entry.ExecState == UjmwAsyncProtocol._ExecStateCancelled) {
        //cancelled by the caller before the allocation was confirmed
        if (entry.LeaseSecret != null && UjmwAsyncProtocol.VerifyPollingHash(entry.LeaseSecret, pollingSalt, pollingHash)) {
          lateFault = "Cancelled";
        }
      }

      entry = null;
      return false;
    }

    private Entry FindEntry(Type contractType, string entryId) {

      long id;
      if (!long.TryParse(entryId, out id)) {
        return null;
      }

      Entry entry;
      if (!_Entries.TryGetValue(id, out entry)) {
        return null;
      }

      if (contractType != null && entry.Queue.ContractType != contractType) {
        return null;
      }

      return entry;
    }

    /// <summary>
    /// Resolves a queue by contract type and name ('null' = the only queue of the contract type).
    /// Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private QueueState ResolveQueue(Type contractType, string queueName, bool createOnDemand, out string fault) {

      fault = null;

      if (contractType == null) {
        fault = UjmwQueueProtocol.FaultUnknownQueue;
        return null;
      }

      if (queueName != null) {

        QueueState namedQueue;
        if (_Queues.TryGetValue(BuildQueueKey(contractType, queueName), out namedQueue)) {
          return namedQueue;
        }

        if (!createOnDemand) {
          fault = UjmwQueueProtocol.FaultUnknownQueue;
          return null;
        }

        return this.GetOrCreateQueue(contractType, queueName);
      }

      QueueState[] candidates = _Queues.Values.Where((queue) => queue.ContractType == contractType).ToArray();

      if (candidates.Length > 1) {
        fault = UjmwQueueProtocol.FaultAmbiguousQueue;
        return null;
      }

      if (candidates.Length == 1) {
        return candidates[0];
      }

      if (!createOnDemand) {
        fault = UjmwQueueProtocol.FaultUnknownQueue;
        return null;
      }

      return this.GetOrCreateQueue(contractType, null);
    }

    private QueueState GetOrCreateQueue(Type contractType, string queueName) {

      string key = BuildQueueKey(contractType, queueName);

      QueueState queue;
      if (!_Queues.TryGetValue(key, out queue)) {
        queue = new QueueState(contractType, queueName);
        _Queues[key] = queue;
      }

      return queue;
    }

    private static string BuildQueueKey(Type contractType, string queueName) {

      string normalizedName = string.Empty;
      if (queueName != null) {
        normalizedName = queueName.Trim('/').ToLowerInvariant();
      }

      return contractType.AssemblyQualifiedName + "|" + normalizedName;
    }

    private void TouchProcessor(QueueState queue, string processorId, DateTime now) {

      if (string.IsNullOrWhiteSpace(processorId)) {
        return;
      }

      queue.ProcessorActivity[processorId] = now;

    }

    /// <summary>
    /// Stores the capacity reported with the latest dequeue / completion (-1 if it was not supplied).
    /// Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private void StoreProcessorCapacity(QueueState queue, string processorId, int capacity) {

      if (string.IsNullOrWhiteSpace(processorId)) {
        return;
      }

      if (capacity < 1) {
        capacity = UjmwQueueProtocol.UnknownCapacity;
      }

      queue.ProcessorCapacity[processorId] = capacity;

    }

    private UjmwQueueStats GetStats(UjmwQueueInfo info, string methodName) {

      UjmwQueueStats stats;
      if (!info.StatsPerMethod.TryGetValue(methodName, out stats)) {
        stats = new UjmwQueueStats();
        info.StatsPerMethod[methodName] = stats;
      }

      return stats;
    }

    private void CountTermination(UjmwQueueStats stats, TimeSpan age) {

      if (age <= TimeSpan.FromMinutes(1)) {
        stats.Past1m++;
      }

      if (age <= TimeSpan.FromMinutes(10)) {
        stats.Past10m++;
      }

      if (age <= TimeSpan.FromHours(1)) {
        stats.Past1h++;
      }

      if (age <= TimeSpan.FromHours(10)) {
        stats.Past10h++;
      }

    }

    /// <summary>
    /// Returns true if the given UJMW response JSON carries a (non-empty) 'fault'.
    /// </summary>
    private static bool HasBusinessFault(string responseJson) {

      if (string.IsNullOrWhiteSpace(responseJson)) {
        return false;
      }

      try {

        JObject response = JObject.Parse(responseJson);
        JToken fault = response["fault"];

        if (fault == null || fault.Type == JTokenType.Null) {
          return false;
        }

        return !string.IsNullOrEmpty(fault.ToString());
      }
      catch (JsonException ex) {
        DevLogger.LogError(ex);
        return false;
      }

    }

    /// <summary>
    /// Generates a 53-bit id (44-bit UTC millisecond time stamp + 9 random bits). Must be called within a lock on '_SyncRoot'.
    /// </summary>
    private long GenerateUniqueId(DateTime now) {

      long milliseconds = (long)(now - _IdEpoch).TotalMilliseconds;
      byte[] buffer = new byte[2];

      using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) {
        while (true) {

          rng.GetBytes(buffer);
          long random = ((buffer[0] << 8) | buffer[1]) & 0x1FF;
          long id = ((milliseconds & 0xFFFFFFFFFFF) << 9) | random;

          if (id != 0 && !_Entries.ContainsKey(id)) {
            return id;
          }

          if (_Entries.Count > 256) {
            milliseconds++;
          }

        }
      }

    }

    #endregion

    #region " State Classes "

    private enum EntryPhase {
      Queued = 0,
      Allocated = 1,
      Leased = 2,
      Terminated = 3
    }

    private class QueueState {

      public QueueState(Type contractType, string queueName) {
        this.ContractType = contractType;
        this.QueueName = queueName;
      }

      public readonly Type ContractType;
      public readonly string QueueName;
      public readonly List<Entry> Waiting = new List<Entry>();
      public readonly List<Termination> Terminations = new List<Termination>();
      public readonly Dictionary<string, DateTime> ProcessorActivity = new Dictionary<string, DateTime>();
      public readonly Dictionary<string, int> ProcessorCapacity = new Dictionary<string, int>();

    }

    private class Entry {

      public Entry(long id, QueueState queue, string methodName, string requestJson, bool isLongRunning, string callerSecret, DateTime enqueuedAt) {
        this.Id = id;
        this.Queue = queue;
        this.MethodName = methodName;
        this.RequestJson = requestJson;
        this.IsLongRunning = isLongRunning;
        this.CallerSecret = callerSecret;
        this.EnqueuedAt = enqueuedAt;
      }

      public readonly long Id;
      public readonly QueueState Queue;
      public readonly string MethodName;
      public readonly string RequestJson;
      public readonly bool IsLongRunning;
      public readonly string CallerSecret;
      public readonly DateTime EnqueuedAt;
      public readonly List<string> FormerLeaseSecrets = new List<string>();
      public readonly List<BufferedInvocation> CallbackBuffer = new List<BufferedInvocation>();

      public EntryPhase Phase = EntryPhase.Queued;
      public string LeaseSecret = null;
      public string ProcessorId = null;
      public DateTime AllocatedAt = DateTime.MinValue;
      public DateTime LastHeartbeat = DateTime.MinValue;
      public bool CancellationRequested = false;
      public int ReleaseCount = 0;
      public int LeaseLossCount = 0;
      public int ExecState = UjmwAsyncProtocol._ExecStateQueued;
      public string ResponseJson = null;
      public string Fault = null;
      public DateTime TerminatedAt = DateTime.MinValue;
      public bool TerminalStateDelivered = false;

    }

    private class BufferedInvocation {

      public BufferedInvocation(string propertyName, long sequence, JObject invocation) {
        this.PropertyName = propertyName;
        this.Sequence = sequence;
        this.Invocation = invocation;
      }

      public readonly string PropertyName;
      public readonly long Sequence;
      public readonly JObject Invocation;

    }

    private class Termination {

      public Termination(string methodName, DateTime time) {
        this.MethodName = methodName;
        this.Time = time;
      }

      public readonly string MethodName;
      public readonly DateTime Time;

    }

    #endregion

  }

}
