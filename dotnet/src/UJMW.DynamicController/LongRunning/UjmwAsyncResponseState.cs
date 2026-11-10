using System.Collections.Generic;

namespace System.Web.UJMW {

  /// <summary>
  /// The 'async' wrapper property of a UJMW response (see 'doc/epics/epic-long-running-operations.md', 3.6).
  /// The JSON names are pinned explicitly, so that they are independent of the serializer and its naming policy.
  /// </summary>
  public class UjmwAsyncState {

    private string _AsyncId = null;
    private int _ExecState = 0;
    private DateTime? _PollNbf = null;
    private bool _Cancellable = false;
    private string _PollingSecret = null;

    /// <summary>
    /// Server-generated identifier of the operation (transported as string).
    /// Not present, if the operation has already terminated within the initiating request (nothing left to poll).
    /// </summary>
    [Newtonsoft.Json.JsonProperty(UjmwAsyncProtocol._AsyncIdPropertyName, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonPropertyName(UjmwAsyncProtocol._AsyncIdPropertyName)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string AsyncId {
      get {
        return _AsyncId;
      }
      set {
        _AsyncId = value;
      }
    }

    /// <summary>
    /// Execution state: -2 = cancelled, -1 = running (cancellation requested), 0 = queued, 1 = running,
    /// 2 = completed successfully, 3 = failed ('fault' is set).
    /// </summary>
    [Newtonsoft.Json.JsonProperty(UjmwAsyncProtocol._ExecStatePropertyName)]
    [System.Text.Json.Serialization.JsonPropertyName(UjmwAsyncProtocol._ExecStatePropertyName)]
    public int ExecState {
      get {
        return _ExecState;
      }
      set {
        _ExecState = value;
      }
    }

    /// <summary>
    /// ISO UTC date-time before which the client should not poll again ('not before').
    /// </summary>
    [Newtonsoft.Json.JsonProperty(UjmwAsyncProtocol._PollNbfPropertyName, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonPropertyName(UjmwAsyncProtocol._PollNbfPropertyName)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? PollNbf {
      get {
        return _PollNbf;
      }
      set {
        _PollNbf = value;
      }
    }

    /// <summary>
    /// Whether a DELETE request (cancellation) is currently meaningful.
    /// </summary>
    [Newtonsoft.Json.JsonProperty(UjmwAsyncProtocol._CancellablePropertyName)]
    [System.Text.Json.Serialization.JsonPropertyName(UjmwAsyncProtocol._CancellablePropertyName)]
    public bool Cancellable {
      get {
        return _Cancellable;
      }
      set {
        _Cancellable = value;
      }
    }

    /// <summary>
    /// Only within the response to the initiating POST: the secret to authenticate subsequent GET / DELETE requests
    /// via 'pollingSalt' and 'pollingHash' = Base64Url(SHA-256(UTF-8(pollingSecret + pollingSalt))) without padding.
    /// </summary>
    [Newtonsoft.Json.JsonProperty(UjmwAsyncProtocol._PollingSecretPropertyName, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonPropertyName(UjmwAsyncProtocol._PollingSecretPropertyName)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string PollingSecret {
      get {
        return _PollingSecret;
      }
      set {
        _PollingSecret = value;
      }
    }

  }

  /// <summary>
  /// Collects the callback invocations of a synchronously executed call (no 'wait'), which are
  /// delivered all together within the final response.
  /// </summary>
  internal class UjmwSynchronousCallbackRecorder : IUjmwCallbackRecorder {

    private readonly List<UjmwCallbackInvocation> _Invocations = new List<UjmwCallbackInvocation>();
    private long _LastSequence = 0;

    /// <summary>
    /// Records one invocation of the callback parameter with the given name.
    /// </summary>
    public void Record(string callbackName, object[] arguments) {

      lock (_Invocations) {
        _LastSequence++;
        _Invocations.Add(new UjmwCallbackInvocation(_LastSequence, callbackName, arguments));
      }

    }

    /// <summary>
    /// Returns all recorded invocations (ordered by sequence).
    /// </summary>
    public UjmwCallbackInvocation[] GetInvocations() {

      lock (_Invocations) {
        return _Invocations.ToArray();
      }

    }

  }

}
