using Newtonsoft.Json.Linq;
using System;
using System.Threading;

namespace UJMW.TestClient.Invocation {

  /// <summary>
  /// Control handle of one running UJMW long-running invocation: transports cancellation requests from the UI
  /// to the polling worker thread and intermediate state updates from the worker thread to the UI.
  /// </summary>
  internal sealed class LongRunningExecution {

    private readonly ManualResetEventSlim _WakeUp = new ManualResetEventSlim(false);
    private readonly Action<JObject> _OnUpdate;
    private volatile bool _CancellationRequested = false;
    private volatile int _FixedPollIntervalMs = -1;

    /// <summary>
    /// Fixed polling interval (ms) which overrides the 'pollNbf' of the server, or -1 (respect the 'pollNbf').
    /// </summary>
    public int FixedPollIntervalMs {
      get {
        return _FixedPollIntervalMs;
      }
    }

    /// <summary>
    /// Overrides the polling interval (same semantics as 'IUjmwClient.OverrideNbfPolling': 0 becomes 500 ms,
    /// negative values restore the default). A currently pending wait is interrupted.
    /// </summary>
    public void OverrideNbfPolling(int fixedSeconds) {

      if (fixedSeconds < 0) {
        _FixedPollIntervalMs = -1;
      } else if (fixedSeconds == 0) {
        _FixedPollIntervalMs = 500;
      } else {
        _FixedPollIntervalMs = fixedSeconds * 1000;
      }

      _WakeUp.Set();

    }

    /// <summary>
    /// Creates a new handle.
    /// </summary>
    /// <param name="onUpdate">Receives intermediate state updates (called on the worker thread).</param>
    public LongRunningExecution(Action<JObject> onUpdate) {

      _OnUpdate = onUpdate;

    }

    /// <summary>
    /// True, if the user requested the cancellation of the operation.
    /// </summary>
    public bool CancellationRequested {
      get {
        return _CancellationRequested;
      }
    }

    /// <summary>
    /// Requests the cancellation (a DELETE request is sent immediately instead of waiting for the next poll).
    /// </summary>
    public void RequestCancellation() {

      _CancellationRequested = true;
      _WakeUp.Set();

    }

    /// <summary>
    /// Has to be called after the DELETE request was sent, so that subsequent waits respect 'pollNbf' again.
    /// </summary>
    public void MarkCancellationSent() {

      _WakeUp.Reset();

    }

    /// <summary>
    /// Blocks until the given delay elapsed or a cancellation was requested.
    /// </summary>
    public void WaitForNextPoll(TimeSpan delay) {

      if (delay <= TimeSpan.Zero) {
        return;
      }

      _WakeUp.Wait(delay);

      //a wake-up caused by 'OverrideNbfPolling' must not shorten subsequent waits
      if (!_CancellationRequested) {
        _WakeUp.Reset();
      }

    }

    /// <summary>
    /// Forwards an intermediate state update to the UI.
    /// </summary>
    public void ReportUpdate(JObject update) {

      if (_OnUpdate != null) {
        _OnUpdate.Invoke(update);
      }

    }

  }

}
