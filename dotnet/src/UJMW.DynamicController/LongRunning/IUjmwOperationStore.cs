using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// Pluggable management of UJMW long-running operations (queueing, execution, state, outcomes and callback buffers).
  /// The default implementation is 'UjmwInMemoryOperationStore' (see 'doc/epics/epic-long-running-operations.md').
  /// </summary>
  public interface IUjmwOperationStore {

    /// <summary>
    /// Initiates a new operation and waits up to the given time for its termination.
    /// If the operation terminated within the wait time, the returned snapshot carries the outcome
    /// and no 'AsyncId' (there is nothing left to poll).
    /// </summary>
    /// <param name="operationScope">Identifies the endpoint operation (polls are only accepted within the same scope).</param>
    /// <param name="body">The work to execute (typically in the background).</param>
    /// <param name="wait">Maximum time to wait for the termination before responding.</param>
    UjmwOperationSnapshot Initiate(string operationScope, UjmwOperationBody body, TimeSpan wait);

    /// <summary>
    /// Polls an existing operation. Returns false (without draining anything and without exposing
    /// whether the operation exists) if the 'asyncId' is unknown or the 'pollingHash' is wrong.
    /// </summary>
    bool TryPoll(string operationScope, string asyncId, string pollingSalt, string pollingHash, out UjmwOperationSnapshot snapshot);

    /// <summary>
    /// Requests the cancellation of an existing operation and waits up to the given time for its termination.
    /// Returns false (without draining anything) if the 'asyncId' is unknown or the 'pollingHash' is wrong.
    /// </summary>
    bool TryCancel(string operationScope, string asyncId, string pollingSalt, string pollingHash, TimeSpan wait, out UjmwOperationSnapshot snapshot);

    /// <summary>
    /// Number of operations which are currently queued or running.
    /// </summary>
    int ActiveOperationCount { get; }

    /// <summary>
    /// Signals the application shutdown: all operations get their cancellation token triggered.
    /// </summary>
    void Shutdown();

  }

  /// <summary>
  /// The work of a long-running operation. Callback parameters of the service method must be routed into the given recorder.
  /// </summary>
  public delegate UjmwOperationOutcome UjmwOperationBody(CancellationToken cancellationToken, IUjmwCallbackRecorder callbackRecorder);

  /// <summary>
  /// Receives the callback invocations of a running operation.
  /// </summary>
  public interface IUjmwCallbackRecorder {

    /// <summary>
    /// Records one invocation of the callback parameter with the given name.
    /// May block on a full buffer and throws an 'UjmwCallbackBufferOverflowException' if the buffer stays full.
    /// </summary>
    void Record(string callbackName, object[] arguments);

  }

  /// <summary>
  /// The final result of an operation.
  /// </summary>
  public sealed class UjmwOperationOutcome {

    private object _Payload;
    private bool _Failed;

    /// <summary>
    /// Creates an outcome.
    /// </summary>
    /// <param name="payload">Opaque payload (typically the response DTO), may be null.</param>
    /// <param name="failed">True, if the operation failed (execState 3).</param>
    public UjmwOperationOutcome(object payload, bool failed) {

      _Payload = payload;
      _Failed = failed;

    }

    /// <summary>
    /// Opaque payload (typically the response DTO), may be null.
    /// </summary>
    public object Payload {
      get {
        return _Payload;
      }
    }

    /// <summary>
    /// True, if the operation failed.
    /// </summary>
    public bool Failed {
      get {
        return _Failed;
      }
    }

  }

  /// <summary>
  /// One buffered invocation of a callback parameter.
  /// </summary>
  public sealed class UjmwCallbackInvocation {

    private long _Sequence;
    private string _CallbackName;
    private object[] _Arguments;

    /// <summary>
    /// Creates a callback invocation entry.
    /// </summary>
    public UjmwCallbackInvocation(long sequence, string callbackName, object[] arguments) {

      _Sequence = sequence;
      _CallbackName = callbackName;
      _Arguments = arguments;

    }

    /// <summary>
    /// Sequence number which is unique across all callbacks of one operation ('#').
    /// </summary>
    public long Sequence {
      get {
        return _Sequence;
      }
    }

    /// <summary>
    /// Name of the callback parameter.
    /// </summary>
    public string CallbackName {
      get {
        return _CallbackName;
      }
    }

    /// <summary>
    /// Positional arguments of the invocation.
    /// </summary>
    public object[] Arguments {
      get {
        return _Arguments;
      }
    }

  }

  /// <summary>
  /// The state of an operation as delivered to the client by a single POST, GET or DELETE request.
  /// </summary>
  public sealed class UjmwOperationSnapshot {

    private long _AsyncId;
    private int _ExecState;
    private bool _Cancellable;
    private DateTime _PollNbf;
    private string _PollingSecret;
    private UjmwOperationOutcome _Outcome;
    private UjmwCallbackInvocation[] _CallbackInvocations;

    /// <summary>
    /// Creates a snapshot.
    /// </summary>
    public UjmwOperationSnapshot(
      long asyncId, int execState, bool cancellable, DateTime pollNbf, string pollingSecret,
      UjmwOperationOutcome outcome, UjmwCallbackInvocation[] callbackInvocations
    ) {

      _AsyncId = asyncId;
      _ExecState = execState;
      _Cancellable = cancellable;
      _PollNbf = pollNbf;
      _PollingSecret = pollingSecret;
      _Outcome = outcome;
      _CallbackInvocations = callbackInvocations;

      if (_CallbackInvocations == null) {
        _CallbackInvocations = new UjmwCallbackInvocation[0];
      }

    }

    /// <summary>
    /// The id of the operation or 0, if it must not be exposed (operation terminated within the initiating request).
    /// </summary>
    public long AsyncId {
      get {
        return _AsyncId;
      }
    }

    /// <summary>
    /// The execution state (-2 .. 3, see 'execState' in the protocol).
    /// </summary>
    public int ExecState {
      get {
        return _ExecState;
      }
    }

    /// <summary>
    /// Whether a DELETE request is currently meaningful.
    /// </summary>
    public bool Cancellable {
      get {
        return _Cancellable;
      }
    }

    /// <summary>
    /// UTC point in time before which the client should not poll ('DateTime.MinValue' if not applicable).
    /// </summary>
    public DateTime PollNbf {
      get {
        return _PollNbf;
      }
    }

    /// <summary>
    /// Only within the response to the initiating request (and only if the operation has not terminated).
    /// </summary>
    public string PollingSecret {
      get {
        return _PollingSecret;
      }
    }

    /// <summary>
    /// The outcome, if the operation has terminated (otherwise null).
    /// </summary>
    public UjmwOperationOutcome Outcome {
      get {
        return _Outcome;
      }
    }

    /// <summary>
    /// The callback invocations which were drained by this request (ordered by sequence).
    /// </summary>
    public UjmwCallbackInvocation[] CallbackInvocations {
      get {
        return _CallbackInvocations;
      }
    }

    /// <summary>
    /// True for cancelled, completed and failed.
    /// </summary>
    public bool IsTerminal {
      get {
        return UjmwAsyncProtocol.IsTerminalExecState(_ExecState);
      }
    }

  }

  /// <summary>
  /// Thrown synchronously back to the service implementation, when a callback is invoked while the
  /// callback buffer stays full for longer than the configured freeze time.
  /// </summary>
  public class UjmwCallbackBufferOverflowException : Exception {

    /// <summary>
    /// Creates the exception.
    /// </summary>
    public UjmwCallbackBufferOverflowException(string message) : base(message) {
    }

  }

}
