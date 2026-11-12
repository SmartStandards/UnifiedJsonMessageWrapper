using System.Collections.Generic;

namespace System.Web.UJMW {

  /// <summary>
  /// Processor side of the UJMW async queue mode (basis of everything under '&lt;route&gt;/_queue').
  /// All methods carry the contract type as first parameter, so that every implementation can map an entry
  /// to its concrete endpoint. 'queueName' is the resolved controller route (null = the only queue of the contract type).
  /// See 'doc/epics/epic-async-queue-mode.md'.
  /// </summary>
  public interface IUjmwQueue {

    /// <summary>
    /// Returns statistics about the queue of the given contract (no side effects).
    /// </summary>
    UjmwQueueInfo GetInfo(Type contractType, string queueName);

    /// <summary>
    /// Allocates the next matching entry for the given processor. The allocation must be confirmed
    /// by a first heartbeat within the dequeue confirmation timeout, otherwise it is withdrawn.
    /// Returns an empty lease (RequestId == null) if no matching entry is available.
    /// 'capacity' is the (optional) maximum number of parallel jobs of the processor (-1 = unknown),
    /// which allows the server to calculate the utilization.
    /// </summary>
    UjmwQueueLease Dequeue(Type contractType, string queueName, string processorId, UjmwQueueMethodFilter methodFilter, int capacity);

    /// <summary>
    /// Heartbeat of a leased entry. Delivers buffered callback invocations (JSON, UJMW callback wire format)
    /// and returns whether a cancellation was requested by the caller.
    /// </summary>
    UjmwQueueHeartbeatResult Heartbeat(
      Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash,
      string callbackInvocationsJson
    );

    /// <summary>
    /// Returns a leased entry to the queue (or confirms the cancellation if it was requested).
    /// </summary>
    UjmwQueueResult Return(Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash);

    /// <summary>
    /// Delivers the final UJMW response (return / out / ref / fault / remaining callback invocations / sidechannel).
    /// If 'dequeueNext' is not null, the next matching entry is allocated in the same round-trip.
    /// The returned lease is never null: it is empty (RequestId == null) if no next entry was requested or available
    /// and carries a 'Fault' if the completion itself was rejected.
    /// 'capacity' is the (optional) maximum number of parallel jobs of the processor (-1 = unknown).
    /// </summary>
    UjmwQueueLease Complete(
      Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash,
      string responseJson, UjmwQueueMethodFilter dequeueNext, int capacity
    );

  }

  /// <summary>
  /// Caller side of the UJMW async queue mode (plus the processor side by inheritance).
  /// </summary>
  public interface IUjmwQueuingService : IUjmwQueue {

    /// <summary>
    /// Enqueues a UJMW request (wire format JSON incl. sidechannel) for the given transport-level method name.
    /// </summary>
    UjmwQueueTicket Enqueue(Type contractType, string queueName, string methodName, string requestJson, bool isLongRunning);

    /// <summary>
    /// Returns the current state of an entry: execution state, drained callback invocations
    /// and - once terminated - the final response JSON. Returns false for unknown entries / wrong hash.
    /// </summary>
    bool TryGetState(
      Type contractType, string queueName, string entryId, string pollingSalt, string pollingHash, TimeSpan maxWait,
      out UjmwQueueEntryState state
    );

    /// <summary>
    /// Requests the cancellation of an entry. A queued entry is removed immediately (execState -2),
    /// a leased entry is flagged ('cancellationRequested' on the next heartbeat).
    /// </summary>
    bool TryRequestCancel(
      Type contractType, string queueName, string entryId, string pollingSalt, string pollingHash, TimeSpan maxWait,
      out UjmwQueueEntryState state
    );

  }

}
