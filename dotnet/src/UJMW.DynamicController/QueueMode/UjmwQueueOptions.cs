namespace System.Web.UJMW {

  /// <summary>
  /// Reason why a confirmed lease was lost.
  /// </summary>
  public enum UjmwQueueLeaseLossReason {

    /// <summary>
    /// No heartbeat arrived within 'LeaseLossTimeoutSec'.
    /// </summary>
    HeartbeatTimeout = 1

  }

  /// <summary>
  /// Decides whether an entry whose confirmed lease was lost should be redelivered (true)
  /// or terminated with the fault 'processor lost' (false).
  /// </summary>
  public delegate bool UjmwQueueRedeliveryPolicy(
    Type contractType, string queueName, string methodName, int attempt, UjmwQueueLeaseLossReason reason
  );

  /// <summary>
  /// Configuration of the UJMW async queue mode (host side). Every value has a default,
  /// so no configuration is mandatory (see 'doc/epics/epic-async-queue-mode.md', 5.6).
  /// </summary>
  public class UjmwQueueOptions {

    private int _MaxQueueSize = 1000;
    private int _DequeueConfirmationTimeoutSec = 2;
    private int _HeartbeatSec = 3;
    private int _LeaseLossTimeoutSec = 60;
    private int _SynchronousCallTimeoutSec = 100;
    private bool _AllowAnonymousInfo = false;
    private UjmwQueueRedeliveryPolicy _RedeliveryPolicy = null;
    private UjmwLongRunningOptions _LongRunningOptions = null;

    /// <summary>
    /// Maximum number of waiting entries per queue. A full queue rejects the enqueue immediately. Default: 1000.
    /// </summary>
    public int MaxQueueSize {
      get {
        return _MaxQueueSize;
      }
      set {
        _MaxQueueSize = value;
      }
    }

    /// <summary>
    /// Time in which an allocation must be confirmed by a first heartbeat. Default: 2 seconds.
    /// </summary>
    public int DequeueConfirmationTimeoutSec {
      get {
        return _DequeueConfirmationTimeoutSec;
      }
      set {
        _DequeueConfirmationTimeoutSec = value;
      }
    }

    /// <summary>
    /// Expected interval of the heartbeats (transported as 'heartbeatSec'). Default: 3 seconds.
    /// </summary>
    public int HeartbeatSec {
      get {
        return _HeartbeatSec;
      }
      set {
        _HeartbeatSec = value;
      }
    }

    /// <summary>
    /// Maximum gap between two heartbeats before the lease-loss handling is invoked. Default: 60 seconds.
    /// </summary>
    public int LeaseLossTimeoutSec {
      get {
        return _LeaseLossTimeoutSec;
      }
      set {
        _LeaseLossTimeoutSec = value;
      }
    }

    /// <summary>
    /// Maximum time a synchronous (non-'CancellationToken') method waits for its queued execution. Default: 100 seconds.
    /// </summary>
    public int SynchronousCallTimeoutSec {
      get {
        return _SynchronousCallTimeoutSec;
      }
      set {
        _SynchronousCallTimeoutSec = value;
      }
    }

    /// <summary>
    /// If true, the info endpoint ('GET _queue' without 'dequeue') can be accessed anonymously. Default: false.
    /// </summary>
    public bool AllowAnonymousInfo {
      get {
        return _AllowAnonymousInfo;
      }
      set {
        _AllowAnonymousInfo = value;
      }
    }

    /// <summary>
    /// Decides about the redelivery after the loss of a confirmed lease.
    /// If null (default), no redelivery takes place and the caller receives the fault 'processor lost'.
    /// </summary>
    public UjmwQueueRedeliveryPolicy RedeliveryPolicy {
      get {
        return _RedeliveryPolicy;
      }
      set {
        _RedeliveryPolicy = value;
      }
    }

    /// <summary>
    /// Source of the callback buffer limit and the outcome retention (identical to the long-running feature).
    /// If null (default), 'UjmwHostConfiguration.LongRunningOptions' is used.
    /// </summary>
    public UjmwLongRunningOptions LongRunningOptions {
      get {
        if (_LongRunningOptions == null) {
          return UjmwHostConfiguration.LongRunningOptions;
        }
        return _LongRunningOptions;
      }
      set {
        _LongRunningOptions = value;
      }
    }

    /// <summary>
    /// Evaluates the redelivery policy (default: no redelivery).
    /// </summary>
    public bool EvaluateRedelivery(Type contractType, string queueName, string methodName, int attempt, UjmwQueueLeaseLossReason reason) {

      if (_RedeliveryPolicy == null) {
        return false;
      }

      return _RedeliveryPolicy.Invoke(contractType, queueName, methodName, attempt, reason);
    }

  }

}
