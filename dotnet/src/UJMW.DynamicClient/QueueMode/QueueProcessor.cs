using Logging.SmartStandards.CopyForUJMW;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace System.Web.UJMW {

  /// <summary>
  /// Processor side of the UJMW async queue mode: dequeues jobs for the contract 'TService' from an 'IUjmwQueue'
  /// (remote via 'UjmwQueueClient' or in-process), confirms them, executes them on the inner implementation
  /// (every job in its own task = isolated 'AsyncLocal' sandbox), sends heartbeats incl. buffered callback
  /// invocations and delivers the final response (see 'doc/epics/epic-async-queue-mode.md', 9.1).
  /// </summary>
  public class QueueProcessor<TService> : IDisposable where TService : class {

    private readonly TService _InnerService;
    private readonly IUjmwQueue _Queue;
    private readonly string _QueueName;
    private readonly int _MaxParallelTasks;
    private readonly UjmwQueueMethodFilter _MethodFilter;
    private readonly UjmwQueueJsonDispatcher _Dispatcher;
    private readonly string _ProcessorId;

    private readonly object _SyncRoot = new object();
    private readonly List<RunningJob> _RunningJobs = new List<RunningJob>();
    private readonly CancellationTokenRegistration _HardShutdownRegistration;

    private CancellationTokenSource _PollerCancellation = null;
    private Task _PollerTask = null;
    private int _PollingIntervalSec = 2;
    private int _IncreasePollRateForSecs = 20;
    private int _IncreasedPollRateMs = 500;
    private int _ReservedSlots = 0;
    private bool _EntryFoundSinceLastEmptyDequeue = false;
    private DateTime _NextPollDueUtc = DateTime.MinValue;
    private DateTime _IncreasedPollRateUntilUtc = DateTime.MinValue;

    //signals the poller that its state changed (slot freed, next job allocated via 'dequeueNext', ...);
    //intentionally not disposed, because job tasks may still signal it after 'Dispose'
    private readonly AutoResetEvent _PollWakeup = new AutoResetEvent(false);
    private int _CallbackBufferLimit = 1000;
    private TimeSpan _CallbackFreezeTime = TimeSpan.FromSeconds(60);
    private TimeSpan _MinHeartbeatInterval = TimeSpan.FromMilliseconds(200);
    private TimeSpan _LeaseLossTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Creates a processor.
    /// </summary>
    /// <param name="innerService">The real implementation of the contract.</param>
    /// <param name="queue">The queue (remote 'UjmwQueueClient' or the in-process 'IUjmwQueuingService').</param>
    /// <param name="maxParallelTasks">Maximum number of concurrently executed jobs.</param>
    /// <param name="methodWhitelist">null = all methods, otherwise only the listed C# method names (incl. all overloads).</param>
    /// <param name="methodBlacklist">null = nothing excluded, otherwise the listed C# method names are excluded.</param>
    /// <param name="hardShutdown">Triggers 'Kill(false)' when cancelled.</param>
    /// <param name="queueName">The resolved controller route (only needed for in-process queues with several routes per contract).</param>
    public QueueProcessor(
      TService innerService, IUjmwQueue queue, int maxParallelTasks = 1,
      string[] methodWhitelist = null, string[] methodBlacklist = null,
      CancellationToken hardShutdown = default(CancellationToken), string queueName = null
    ) {

      if (innerService == null) {
        throw new ArgumentNullException(nameof(innerService));
      }

      if (queue == null) {
        throw new ArgumentNullException(nameof(queue));
      }

      if (maxParallelTasks < 1) {
        throw new ArgumentOutOfRangeException(nameof(maxParallelTasks));
      }

      List<PropertyInfo> subServices = typeof(TService).GetProperties().Where((p) => p.CanRead && !p.PropertyType.IsValueType).ToList();
      if (subServices.Count > 0) {
        throw new NotSupportedException($"The queue mode does not support sub-services (contract '{typeof(TService).Name}') in phase 1.");
      }

      _InnerService = innerService;
      _Queue = queue;
      _QueueName = queueName;
      _MaxParallelTasks = maxParallelTasks;
      _Dispatcher = new UjmwQueueJsonDispatcher(typeof(TService));
      _MethodFilter = this.BuildFilter(methodWhitelist, methodBlacklist);
      _ProcessorId = Environment.MachineName + "/" + typeof(TService).Name + "/" + Guid.NewGuid().ToString("N").Substring(0, 6);

      if (hardShutdown.CanBeCanceled) {
        _HardShutdownRegistration = hardShutdown.Register(() => this.Kill(false));
      }

    }

    #region " Properties "

    /// <summary>
    /// The real implementation of the contract.
    /// </summary>
    public TService InnerService {
      get {
        return _InnerService;
      }
    }

    /// <summary>
    /// Informational name of this processor instance.
    /// </summary>
    public string ProcessorId {
      get {
        return _ProcessorId;
      }
    }

    /// <summary>
    /// Number of currently running jobs.
    /// </summary>
    public int CurrentTasks {
      get {
        lock (_SyncRoot) {
          return _RunningJobs.Count;
        }
      }
    }

    /// <summary>
    /// The JSON dispatcher (allows configuring the sidechannel restore / backchannel capture).
    /// </summary>
    public UjmwQueueJsonDispatcher Dispatcher {
      get {
        return _Dispatcher;
      }
    }

    /// <summary>
    /// Limit of the processor-side callback buffer per job (default: 1000).
    /// </summary>
    public int CallbackBufferLimit {
      get {
        return _CallbackBufferLimit;
      }
      set {
        _CallbackBufferLimit = value;
      }
    }

    /// <summary>
    /// Maximum time between two successful heartbeats before the job is cancelled because the lease is lost (default: 60 seconds).
    /// </summary>
    public TimeSpan LeaseLossTimeout {
      get {
        return _LeaseLossTimeout;
      }
      set {
        _LeaseLossTimeout = value;
      }
    }

    /// <summary>
    /// Duration (seconds) of the increased poll rate, which is activated when the queue runs empty after
    /// entries were found or after a completion did not deliver a next entry (default: 20).
    /// </summary>
    public int IncreasePollRateForSecs {
      get {
        return _IncreasePollRateForSecs;
      }
      set {
        _IncreasePollRateForSecs = Math.Max(0, value);
      }
    }

    /// <summary>
    /// Poll interval (milliseconds) while the increased poll rate is active (default: 500).
    /// </summary>
    public int IncreasedPollRateMs {
      get {
        return _IncreasedPollRateMs;
      }
      set {
        _IncreasedPollRateMs = Math.Max(50, value);
      }
    }

    #endregion

    #region " Control "

    /// <summary>
    /// Starts the internal poller (dequeues whenever a slot is free).
    /// </summary>
    public void Start(int pollingIntervalSec = 2) {

      lock (_SyncRoot) {

        if (_PollerTask != null) {
          return;
        }

        _PollingIntervalSec = Math.Max(0, pollingIntervalSec);
        _NextPollDueUtc = DateTime.MinValue;
        _IncreasedPollRateUntilUtc = DateTime.MinValue;
        _EntryFoundSinceLastEmptyDequeue = false;
        _PollerCancellation = new CancellationTokenSource();

        CancellationToken token = _PollerCancellation.Token;
        _PollerTask = Task.Run(() => this.PollLoop(token));

      }

    }

    /// <summary>
    /// Dequeues and executes the next job (if a slot is free). Returns true if a job was started.
    /// </summary>
    public bool Next() {

      if (!this.TryReserveSlot()) {
        return false;
      }

      try {

        UjmwQueueLease lease;
        try {
          lease = _Queue.Dequeue(typeof(TService), _QueueName, _ProcessorId, _MethodFilter);
        }
        catch (Exception ex) {
          DevLogger.LogError(ex);
          this.ScheduleRegularPoll();
          return false;
        }

        if (lease == null || !lease.HasEntry) {
          this.OnDequeueAttempted(false, false);
          return false;
        }

        //the job is registered as running before the reservation is released (no gap for concurrent dequeues)
        bool started = this.StartJob(lease);
        this.OnDequeueAttempted(true, false);

        return started;
      }
      finally {
        lock (_SyncRoot) {
          _ReservedSlots--;
        }
      }

    }

    /// <summary>
    /// Stops the internal poller; running jobs continue (optionally waits for them).
    /// </summary>
    public void Stop(bool wait = false) {

      Task pollerTask;

      lock (_SyncRoot) {

        pollerTask = _PollerTask;

        if (_PollerCancellation != null) {
          _PollerCancellation.Cancel();
        }

        _PollerTask = null;
        _PollerCancellation = null;

      }

      if (pollerTask != null) {
        pollerTask.Wait();
      }

      if (wait) {
        this.WaitForRunningJobs();
      }

    }

    /// <summary>
    /// Stops the poller, cancels the running implementations and returns their entries to the queue.
    /// </summary>
    public void Kill(bool wait = true) {

      this.Stop(false);

      RunningJob[] jobs;
      lock (_SyncRoot) {
        jobs = _RunningJobs.ToArray();
      }

      foreach (RunningJob job in jobs) {
        job.Killed = true;
        job.Cancellation.Cancel();
      }

      if (wait) {
        this.WaitForRunningJobs();
      }

    }

    /// <summary>
    /// Kills the processor (waiting for the returns).
    /// </summary>
    public void Dispose() {
      this.Kill(true);
      _HardShutdownRegistration.Dispose();
    }

    #endregion

    #region " Internals "

    /// <summary>
    /// Poll loop:
    /// (1) as long as slots are free, a found entry is followed by an immediate next dequeue (burst),
    /// (1a) while all slots are busy, no dequeue GETs are sent at all (the heartbeat PUTs keep the leases alive),
    /// (1b) when nothing more is found, the poll interval is reduced to 'IncreasedPollRateMs' for 'IncreasePollRateForSecs',
    /// (2) completions request the next entry via 'dequeueNext' (see 'CompleteEntry').
    /// </summary>
    private void PollLoop(CancellationToken token) {

      WaitHandle[] waitHandles = new WaitHandle[] { token.WaitHandle, _PollWakeup };

      while (!token.IsCancellationRequested) {

        if (this.IsAtCapacity()) {
          WaitHandle.WaitAny(waitHandles);
          continue;
        }

        TimeSpan wait;
        lock (_SyncRoot) {
          wait = _NextPollDueUtc - DateTime.UtcNow;
        }

        if (wait > TimeSpan.Zero) {
          WaitHandle.WaitAny(waitHandles, wait);
          continue;
        }

        this.Next();

      }

    }

    private bool IsAtCapacity() {

      lock (_SyncRoot) {
        return (_RunningJobs.Count + _ReservedSlots >= _MaxParallelTasks);
      }

    }

    private bool TryReserveSlot() {

      lock (_SyncRoot) {

        if (_RunningJobs.Count + _ReservedSlots >= _MaxParallelTasks) {
          return false;
        }

        _ReservedSlots++;
        return true;
      }

    }

    /// <summary>
    /// Updates the poll timing after a dequeue attempt (regular GET or 'dequeueNext' of a completion).
    /// </summary>
    private void OnDequeueAttempted(bool entryFound, bool viaCompletion) {

      lock (_SyncRoot) {

        DateTime now = DateTime.UtcNow;

        if (entryFound) {

          //rule 1: poll again immediately (as long as slots are free)
          _EntryFoundSinceLastEmptyDequeue = true;
          _NextPollDueUtc = now;

        }
        else {

          //rule 1b: nothing more available after activity - increase the poll rate for a while
          if (_EntryFoundSinceLastEmptyDequeue || viaCompletion) {
            _IncreasedPollRateUntilUtc = now.AddSeconds(_IncreasePollRateForSecs);
            _EntryFoundSinceLastEmptyDequeue = false;
          }

          _NextPollDueUtc = now.Add(this.GetCurrentPollInterval(now));
        }

      }

      _PollWakeup.Set();
    }

    private void ScheduleRegularPoll() {

      lock (_SyncRoot) {
        DateTime now = DateTime.UtcNow;
        _NextPollDueUtc = now.Add(this.GetCurrentPollInterval(now));
      }

    }

    private TimeSpan GetCurrentPollInterval(DateTime now) {

      TimeSpan regularInterval = TimeSpan.FromSeconds(Math.Max(0.05, _PollingIntervalSec));

      if (now >= _IncreasedPollRateUntilUtc) {
        return regularInterval;
      }

      TimeSpan increasedInterval = TimeSpan.FromMilliseconds(_IncreasedPollRateMs);
      if (increasedInterval < regularInterval) {
        return increasedInterval;
      }

      return regularInterval;
    }

    private void WaitForRunningJobs() {

      Task[] tasks;
      lock (_SyncRoot) {
        tasks = _RunningJobs.Select((job) => job.Task).Where((task) => task != null).ToArray();
      }

      Task.WaitAll(tasks);

    }

    /// <summary>
    /// Confirms the allocation (first heartbeat) and starts the job in its own task.
    /// </summary>
    private bool StartJob(UjmwQueueLease lease) {

      UjmwQueueHeartbeatResult confirmation = this.SendHeartbeat(lease, null);

      if (confirmation == null || confirmation.CancellationRequested || confirmation.Fault != null) {

        string reason = "no response";
        if (confirmation != null && confirmation.Fault != null) {
          reason = confirmation.Fault;
        }

        DevLogger.LogTrace(2091845400103, 72402, $"UJMW queue job {lease.RequestId} discarded after confirmation: {reason}");
        return false;
      }

      RunningJob job = new RunningJob(lease, new UjmwQueueCallbackBuffer(_CallbackBufferLimit, _CallbackFreezeTime));

      lock (_SyncRoot) {
        _RunningJobs.Add(job);
      }

      //own task = cloned 'AsyncLocal' sandbox (sidechannel restore can not leak into the poller or other jobs)
      job.Task = Task.Run(() => this.ExecuteJob(job));

      return true;
    }

    private void ExecuteJob(RunningJob job) {

      try {

        using (Timer heartbeatTimer = new Timer((state) => this.HeartbeatTick(job), null, this.GetHeartbeatInterval(job), this.GetHeartbeatInterval(job))) {

          UjmwQueueDispatchResult result = _Dispatcher.Dispatch(
            _InnerService, job.Lease.MethodName, job.Lease.RequestJson, job.Cancellation.Token, job.Callbacks
          );

          heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);

          lock (job) {

            if (job.LeaseLost) {
              return;
            }

            if (result.Cancelled || job.Killed) {
              this.ReturnEntry(job);
            }
            else {
              this.CompleteEntry(job, result.ResponseJson);
            }

          }

        }

      }
      catch (Exception ex) {
        DevLogger.LogError(ex);
      }
      finally {

        lock (_SyncRoot) {
          _RunningJobs.Remove(job);
        }

        job.Cancellation.Dispose();

        //a slot became free (rule 1a ends)
        _PollWakeup.Set();
      }

    }

    private TimeSpan GetHeartbeatInterval(RunningJob job) {

      int seconds = job.Lease.HeartbeatSec;
      if (seconds <= 0) {
        seconds = 3;
      }

      //early heartbeats for pending callbacks are triggered within the tick
      TimeSpan interval = TimeSpan.FromSeconds(seconds);
      if (interval > _MinHeartbeatInterval) {
        return _MinHeartbeatInterval;
      }

      return interval;
    }

    /// <summary>
    /// Sends a heartbeat when callbacks are pending (bounded by the minimal interval) or the heartbeat interval elapsed.
    /// </summary>
    private void HeartbeatTick(RunningJob job) {

      if (!Monitor.TryEnter(job)) {
        return;
      }

      try {

        if (job.LeaseLost || job.Cancellation.IsCancellationRequested && job.CancellationSignaled) {
          return;
        }

        int heartbeatSec = job.Lease.HeartbeatSec;
        if (heartbeatSec <= 0) {
          heartbeatSec = 3;
        }

        bool due = (DateTime.UtcNow - job.LastHeartbeat).TotalSeconds >= heartbeatSec;
        if (!due && job.Callbacks.PendingCount == 0) {
          return;
        }

        int taken;
        string callbacks = job.Callbacks.Take(out taken);

        UjmwQueueHeartbeatResult result = this.SendHeartbeat(job.Lease, callbacks);

        if (result == null) {

          job.Callbacks.Restore(callbacks);

          if (DateTime.UtcNow - job.LastHeartbeat > _LeaseLossTimeout) {
            job.LeaseLost = true;
            job.Cancellation.Cancel();
          }

          return;
        }

        job.LastHeartbeat = DateTime.UtcNow;

        if (result.Fault == UjmwQueueProtocol.FaultCallbackBufferFull) {
          job.Callbacks.Restore(callbacks);
        }
        else if (result.Fault != null) {
          //lease lost / withdrawn - the work must be discarded
          job.LeaseLost = true;
          job.Cancellation.Cancel();
          return;
        }

        if (result.CancellationRequested) {
          job.CancellationSignaled = true;
          job.Cancellation.Cancel();
        }

      }
      catch (Exception ex) {
        DevLogger.LogError(ex);
      }
      finally {
        Monitor.Exit(job);
      }

    }

    private UjmwQueueHeartbeatResult SendHeartbeat(UjmwQueueLease lease, string callbacks) {

      try {

        string salt;
        string hash;
        CreateAuth(lease.PollingSecret, out salt, out hash);

        return _Queue.Heartbeat(typeof(TService), _QueueName, _ProcessorId, lease.RequestId, salt, hash, callbacks);
      }
      catch (Exception ex) {
        DevLogger.LogError(ex);
        return null;
      }

    }

    private void ReturnEntry(RunningJob job) {

      string salt;
      string hash;
      CreateAuth(job.Lease.PollingSecret, out salt, out hash);

      UjmwQueueResult result = _Queue.Return(typeof(TService), _QueueName, _ProcessorId, job.Lease.RequestId, salt, hash);

      if (result != null && result.Fault != null) {
        DevLogger.LogTrace(2091845400104, 72403, $"UJMW queue job {job.Lease.RequestId} could not be returned: {result.Fault}");
      }

    }

    private void CompleteEntry(RunningJob job, string responseJson) {

      //rule 2: the completion requests the next entry in the same round-trip (replaces a separate dequeue GET);
      //the slot of the completing job is handed over to the next job
      UjmwQueueMethodFilter dequeueNext = null;
      if (_PollerTask != null) {
        dequeueNext = _MethodFilter;
      }

      DateTime deadline = DateTime.UtcNow.Add(_LeaseLossTimeout);

      while (true) {

        try {

          string salt;
          string hash;
          CreateAuth(job.Lease.PollingSecret, out salt, out hash);

          UjmwQueueLease next = _Queue.Complete(
            typeof(TService), _QueueName, _ProcessorId, job.Lease.RequestId, salt, hash, responseJson, dequeueNext
          );

          if (next != null && next.Fault != null) {
            DevLogger.LogTrace(2091845400105, 72404, $"UJMW queue job {job.Lease.RequestId} completion rejected: {next.Fault}");
            return;
          }

          if (dequeueNext != null) {

            bool nextFound = (next != null && next.HasEntry);

            if (nextFound) {

              //hand over the slot: the completing job is replaced by a reservation, so that
              //the poller can not allocate this slot concurrently while the next job is confirmed
              lock (_SyncRoot) {
                _RunningJobs.Remove(job);
                _ReservedSlots++;
              }

              try {
                this.StartJob(next);
              }
              finally {
                lock (_SyncRoot) {
                  _ReservedSlots--;
                }
              }

            }

            this.OnDequeueAttempted(nextFound, true);
          }

          return;
        }
        catch (Exception ex) {

          DevLogger.LogError(ex);

          if (DateTime.UtcNow > deadline) {
            return;
          }

          Thread.Sleep(1000);
        }

      }

    }

    /// <summary>
    /// Builds the dequeue filter from C# method names (whitelist minus blacklist, all overloads).
    /// </summary>
    private UjmwQueueMethodFilter BuildFilter(string[] methodWhitelist, string[] methodBlacklist) {

      string[] whitelist = this.ResolveTransportNames(methodWhitelist);
      string[] blacklist = this.ResolveTransportNames(methodBlacklist);

      if (whitelist != null && blacklist != null) {
        return UjmwQueueMethodFilter.Whitelist(whitelist.Except(blacklist, StringComparer.OrdinalIgnoreCase).ToArray());
      }

      if (whitelist != null) {
        return UjmwQueueMethodFilter.Whitelist(whitelist);
      }

      if (blacklist != null) {
        return UjmwQueueMethodFilter.Blacklist(blacklist);
      }

      return UjmwQueueMethodFilter.All;
    }

    private string[] ResolveTransportNames(string[] csharpMethodNames) {

      if (csharpMethodNames == null) {
        return null;
      }

      List<string> result = new List<string>();

      foreach (string name in csharpMethodNames) {

        string[] transportNames = _Dispatcher.GetTransportMethodNames(name);
        if (transportNames.Length == 0) {
          throw new ArgumentException($"The contract '{typeof(TService).Name}' has no method named '{name}'.");
        }

        result.AddRange(transportNames);
      }

      return result.Distinct().ToArray();
    }

    private static void CreateAuth(string secret, out string salt, out string hash) {
      salt = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSaltLength);
      hash = UjmwAsyncProtocol.ComputePollingHash(secret, salt);
    }

    private class RunningJob {

      public RunningJob(UjmwQueueLease lease, UjmwQueueCallbackBuffer callbacks) {
        this.Lease = lease;
        this.Callbacks = callbacks;
        this.LastHeartbeat = DateTime.UtcNow;
      }

      public readonly UjmwQueueLease Lease;
      public readonly UjmwQueueCallbackBuffer Callbacks;
      public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
      public Task Task = null;
      public DateTime LastHeartbeat;
      public volatile bool Killed = false;
      public volatile bool LeaseLost = false;
      public volatile bool CancellationSignaled = false;

    }

    #endregion

  }

}
