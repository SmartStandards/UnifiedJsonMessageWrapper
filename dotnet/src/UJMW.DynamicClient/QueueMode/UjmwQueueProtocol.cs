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
    /// Optional query parameter of the dequeue GET and the completion POST: the maximum number of parallel jobs
    /// of the processor (allows the server to calculate the utilization together with the active jobs).
    /// </summary>
    public const string CapacityQueryParam = "capacity";

    /// <summary>
    /// Value for an unknown processor capacity.
    /// </summary>
    public const int UnknownCapacity = -1;

    /// <summary>
    /// Parses the optional 'capacity' query value (missing, ill-typed or non-positive values are ignored = -1).
    /// </summary>
    public static int ParseCapacity(string rawValue) {

      int capacity;
      if (!int.TryParse(rawValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out capacity)) {
        return UnknownCapacity;
      }

      if (capacity < 1) {
        return UnknownCapacity;
      }

      return capacity;
    }

    /// <summary>
    /// Separator within processor names: everything before the first '#' is the constant name of an agent installation,
    /// everything after it is free for instance discriminators (timestamps, sequence numbers, process ids, ...).
    /// </summary>
    public const string InstanceDiscriminatorSeparator = "#";

    /// <summary>
    /// Returns the constant agent name of a processor name (the part before the first '#').
    /// </summary>
    public static string GetAgentName(string processorName) {

      if (processorName == null) {
        return null;
      }

      int separatorIndex = processorName.IndexOf(InstanceDiscriminatorSeparator, StringComparison.Ordinal);
      if (separatorIndex < 0) {
        return processorName;
      }

      return processorName.Substring(0, separatorIndex);
    }

    /// <summary>
    /// Generates a processor name following the convention '&lt;agentName&gt;#MM-dd#HH-mm-ss' (local start time as discriminator).
    /// </summary>
    public static string GenerateProcessorName(string agentName) {

      DateTime startTime = DateTime.Now;

      return agentName + InstanceDiscriminatorSeparator + startTime.ToString("MM-dd") + InstanceDiscriminatorSeparator + startTime.ToString("HH-mm-ss");
    }

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
