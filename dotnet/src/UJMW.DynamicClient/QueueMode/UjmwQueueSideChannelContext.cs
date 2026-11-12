using System.Collections.Generic;
using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// Carries the original inbound sidechannel snapshot of a queue-mode controller call to the 'UjmwQueueBridgeExecutor',
  /// so that it is forwarded unchanged into the queue entry (instead of being re-captured by the client configuration).
  /// The value lives in an 'AsyncLocal' and therefore only flows within the current execution context.
  /// </summary>
  public static class UjmwQueueSideChannelContext {

    private static readonly AsyncLocal<IDictionary<string, string>> _Current = new AsyncLocal<IDictionary<string, string>>();

    /// <summary>
    /// The inbound sidechannel snapshot to forward (null = let the client configuration capture the sidechannel).
    /// </summary>
    public static IDictionary<string, string> Current {
      get {
        return _Current.Value;
      }
      set {
        _Current.Value = value;
      }
    }

  }

}
