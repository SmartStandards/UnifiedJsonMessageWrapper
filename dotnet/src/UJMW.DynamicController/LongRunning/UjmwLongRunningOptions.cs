namespace System.Web.UJMW {

  /// <summary>
  /// Calculates the UTC point in time before which a client should not poll again ('pollNbf').
  /// </summary>
  /// <param name="operationStartTime">UTC time when the operation has been initiated.</param>
  /// <param name="lastPollTime">UTC time of the current poll (the moment, the 'pollNbf' is calculated for).</param>
  /// <param name="overallActiveOperations">Number of operations which are currently queued or running.</param>
  public delegate DateTime UjmwPollNbfCalculator(DateTime operationStartTime, DateTime lastPollTime, int overallActiveOperations);

  /// <summary>
  /// Configuration of the server-side infrastructure for long-running operations.
  /// Every value has a default, so no configuration is mandatory (see 'doc/epics/epic-long-running-operations.md', 5.2).
  /// </summary>
  public class UjmwLongRunningOptions {

    private int _MaxConcurrentLongRunningAsyncCalls = 300;
    private int _CallbackBufferLimit = 1000;
    private TimeSpan _FreezeTimeBeforeCallbackBufferOverflow = TimeSpan.FromSeconds(30);
    private TimeSpan _MinOutcomePreservTime = TimeSpan.FromSeconds(10);
    private TimeSpan _MaxOutcomePreservTime = TimeSpan.FromSeconds(300);
    private int _OutcomeCleanupLimit = 100;
    private int _ThrottlingLevel = 1;
    private UjmwPollNbfCalculator _CalculateNbf = null;

    /// <summary>
    /// Maximum number of operations executed in parallel; further operations are queued (execState 0). Default: 300.
    /// </summary>
    public int MaxConcurrentLongRunningAsyncCalls {
      get {
        return _MaxConcurrentLongRunningAsyncCalls;
      }
      set {
        _MaxConcurrentLongRunningAsyncCalls = value;
      }
    }

    /// <summary>
    /// Maximum number of buffered, not yet delivered callback invocations per operation. Default: 1000.
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
    /// Grace period a callback invocation blocks on a full buffer before an
    /// 'UjmwCallbackBufferOverflowException' is thrown. Default: 30 seconds.
    /// </summary>
    public TimeSpan FreezeTimeBeforeCallbackBufferOverflow {
      get {
        return _FreezeTimeBeforeCallbackBufferOverflow;
      }
      set {
        _FreezeTimeBeforeCallbackBufferOverflow = value;
      }
    }

    /// <summary>
    /// Minimum time an outcome is preserved after the operation has terminated. Default: 10 seconds.
    /// </summary>
    public TimeSpan MinOutcomePreservTime {
      get {
        return _MinOutcomePreservTime;
      }
      set {
        _MinOutcomePreservTime = value;
      }
    }

    /// <summary>
    /// Maximum lifetime of an outcome after the operation has terminated (extended per operation
    /// by a 'pollNbf' that was actually sent). Default: 300 seconds.
    /// </summary>
    public TimeSpan MaxOutcomePreservTime {
      get {
        return _MaxOutcomePreservTime;
      }
      set {
        _MaxOutcomePreservTime = value;
      }
    }

    /// <summary>
    /// Number of preserved outcomes above which the cleanup (oldest first) starts. Default: 100.
    /// </summary>
    public int OutcomeCleanupLimit {
      get {
        return _OutcomeCleanupLimit;
      }
      set {
        _OutcomeCleanupLimit = value;
      }
    }

    /// <summary>
    /// Load-dependent throttling factor for the poll interval (1 = doubles at 100 active operations). Default: 1.
    /// </summary>
    public int ThrottlingLevel {
      get {
        return _ThrottlingLevel;
      }
      set {
        _ThrottlingLevel = value;
      }
    }

    /// <summary>
    /// Customizing hook for calculating the 'pollNbf'. If null, the default implementation
    /// (Fibonacci schedule combined with the load-dependent throttling) is used.
    /// </summary>
    public UjmwPollNbfCalculator CalculateNbf {
      get {
        return _CalculateNbf;
      }
      set {
        _CalculateNbf = value;
      }
    }

    /// <summary>
    /// Calculates the 'pollNbf' using the customizing hook or the default implementation.
    /// </summary>
    public DateTime EvaluatePollNbf(DateTime operationStartTime, DateTime lastPollTime, int overallActiveOperations) {

      if (_CalculateNbf != null) {
        return _CalculateNbf.Invoke(operationStartTime, lastPollTime, overallActiveOperations);
      }

      double elapsedSeconds = (lastPollTime - operationStartTime).TotalSeconds;
      int delaySeconds = UjmwAsyncProtocol.CalculateDefaultPollDelaySeconds(elapsedSeconds, overallActiveOperations, _ThrottlingLevel);

      return lastPollTime.AddSeconds(delaySeconds);
    }

  }

}
