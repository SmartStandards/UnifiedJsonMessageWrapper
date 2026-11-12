namespace System.Web.UJMW {

  /// <summary>
  /// Well-known fault messages and wire names of the UJMW async queue mode.
  /// </summary>
  public static class UjmwQueueProtocol {

    /// <summary>
    /// Reserved transport-level name of the queue infrastructure sub-route.
    /// </summary>
    public const string QueueRouteSegment = "_queue";

    public const string FaultCapacityExceeded = "Queue capacity exceeded";
    public const string FaultWrongRequestIdOrHash = "Wrong requestId or pollingHash";
    public const string FaultConfirmationMissed = "Withdrawn - dequeue confirmation missed";
    public const string FaultReleaseLimit = "Dequeued and Released 10 Times (Limit)";
    public const string FaultProcessorLost = "processor lost";
    public const string FaultProcessingTimeout = "Queue processing timeout";
    public const string FaultHostShuttingDown = "host is shutting down";
    public const string FaultCallbackBufferFull = "Callback buffer full";
    public const string FaultAmbiguousQueue = "Ambiguous queue (the contract type is registered more than once, a queueName is required)";
    public const string FaultUnknownQueue = "Unknown queue";

    public const string ProcessorQueryParam = "processor";
    public const string DequeueQueryParam = "dequeue";
    public const string RequestIdQueryParam = "requestId";
    public const string PollingSaltQueryParam = "pollingSalt";
    public const string PollingHashQueryParam = "pollingHash";

    /// <summary>
    /// Maximum number of explicit releases (the next release terminates the entry).
    /// </summary>
    public const int MaxReleases = 10;

    /// <summary>
    /// Reinsertion positions for the releases 1..10 (Fibonacci without the duplicate 1).
    /// </summary>
    public static int GetReinsertionIndex(int releaseCount) {

      int[] positions = new int[] { 0, 1, 2, 3, 5, 8, 13, 21, 34, 55 };

      if (releaseCount < 1) {
        return 0;
      }

      if (releaseCount > positions.Length) {
        return positions[positions.Length - 1];
      }

      return positions[releaseCount - 1];
    }

  }

}
