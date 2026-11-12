using System.Collections.Generic;
using System.Linq;

namespace System.Web.UJMW {

  /// <summary>
  /// Method filter of a dequeue request ('*' = any method, 'm1,m4' = whitelist, '!m1,!m4' = blacklist).
  /// Method names are transport-level method names.
  /// </summary>
  public sealed class UjmwQueueMethodFilter {

    private readonly string[] _MethodNames;
    private readonly bool _IsBlacklist;

    private UjmwQueueMethodFilter(string[] methodNames, bool isBlacklist) {
      _MethodNames = methodNames;
      _IsBlacklist = isBlacklist;
    }

    /// <summary>
    /// A filter which matches every method ('*').
    /// </summary>
    public static UjmwQueueMethodFilter All {
      get {
        return new UjmwQueueMethodFilter(new string[0], true);
      }
    }

    /// <summary>
    /// Creates a filter which only matches the given methods.
    /// </summary>
    public static UjmwQueueMethodFilter Whitelist(string[] methodNames) {

      if (methodNames == null) {
        throw new ArgumentNullException(nameof(methodNames));
      }

      return new UjmwQueueMethodFilter(methodNames.Distinct().ToArray(), false);
    }

    /// <summary>
    /// Creates a filter which matches every method except the given ones.
    /// </summary>
    public static UjmwQueueMethodFilter Blacklist(string[] methodNames) {

      if (methodNames == null) {
        throw new ArgumentNullException(nameof(methodNames));
      }

      return new UjmwQueueMethodFilter(methodNames.Distinct().ToArray(), true);
    }

    /// <summary>
    /// True for a blacklist (or '*'), false for a whitelist.
    /// </summary>
    public bool IsBlacklist {
      get {
        return _IsBlacklist;
      }
    }

    /// <summary>
    /// The listed method names (without the '!' prefix).
    /// </summary>
    public string[] MethodNames {
      get {
        return _MethodNames.ToArray();
      }
    }

    /// <summary>
    /// Returns true if the given transport-level method name is accepted by this filter.
    /// </summary>
    public bool IsMatch(string methodName) {

      bool listed = _MethodNames.Contains(methodName, StringComparer.OrdinalIgnoreCase);

      if (_IsBlacklist) {
        return !listed;
      }

      return listed;
    }

    /// <summary>
    /// Parses the wire syntax ('*', 'm1,m4' or '!m1,!m4'). Returns false for empty or mixed filters.
    /// </summary>
    public static bool TryParse(string filterExpression, out UjmwQueueMethodFilter filter, out string error) {

      filter = null;
      error = null;

      if (string.IsNullOrWhiteSpace(filterExpression)) {
        error = "Empty method filter";
        return false;
      }

      string[] items = filterExpression.Split(',').Select((item) => item.Trim()).Where((item) => item.Length > 0).ToArray();

      if (items.Length == 1 && items[0] == "*") {
        filter = UjmwQueueMethodFilter.All;
        return true;
      }

      int negatedCount = items.Count((item) => item.StartsWith("!"));

      if (items.Length == 0 || items.Contains("*")) {
        error = "Invalid method filter '" + filterExpression + "'";
        return false;
      }

      if (negatedCount > 0 && negatedCount < items.Length) {
        error = "Mixed whitelist and blacklist items within method filter '" + filterExpression + "'";
        return false;
      }

      if (negatedCount > 0) {
        filter = UjmwQueueMethodFilter.Blacklist(items.Select((item) => item.Substring(1)).ToArray());
      }
      else {
        filter = UjmwQueueMethodFilter.Whitelist(items);
      }

      return true;
    }

    /// <summary>
    /// Returns the wire syntax of the filter.
    /// </summary>
    public override string ToString() {

      if (_IsBlacklist) {

        if (_MethodNames.Length == 0) {
          return "*";
        }

        return string.Join(",", _MethodNames.Select((name) => "!" + name));
      }

      return string.Join(",", _MethodNames);
    }

  }

  /// <summary>
  /// Result of an enqueue operation (caller side).
  /// </summary>
  public class UjmwQueueTicket {

    private string _EntryId = null;
    private string _PollingSecret = null;
    private string _Fault = null;

    /// <summary>
    /// Id of the queue entry (null if the enqueue was rejected).
    /// </summary>
    public string EntryId {
      get {
        return _EntryId;
      }
      set {
        _EntryId = value;
      }
    }

    /// <summary>
    /// Secret to authenticate polls / cancellations of this entry (caller side).
    /// </summary>
    public string PollingSecret {
      get {
        return _PollingSecret;
      }
      set {
        _PollingSecret = value;
      }
    }

    /// <summary>
    /// Queue framework fault (for example 'Queue capacity exceeded'), otherwise null.
    /// </summary>
    public string Fault {
      get {
        return _Fault;
      }
      set {
        _Fault = value;
      }
    }

  }

  /// <summary>
  /// Caller-side view of a queue entry.
  /// </summary>
  public class UjmwQueueEntryState {

    private int _ExecState = 0;
    private bool _Cancellable = true;
    private string _CallbackInvocationsJson = null;
    private string _ResponseJson = null;
    private string _Fault = null;

    /// <summary>
    /// Execution state as defined by the long-running protocol
    /// (-2 = cancelled, -1 = cancellation requested, 0 = queued, 1 = running, 2 = completed, 3 = failed).
    /// </summary>
    public int ExecState {
      get {
        return _ExecState;
      }
      set {
        _ExecState = value;
      }
    }

    /// <summary>
    /// False while the processor reports that a cancellation is currently not possible.
    /// </summary>
    public bool Cancellable {
      get {
        return _Cancellable;
      }
      set {
        _Cancellable = value;
      }
    }

    /// <summary>
    /// Drained callback invocations as JSON object ('{ "&lt;name&gt;Invokes": [ { "#": 1, "arg1": ... } ] }') or null.
    /// </summary>
    public string CallbackInvocationsJson {
      get {
        return _CallbackInvocationsJson;
      }
      set {
        _CallbackInvocationsJson = value;
      }
    }

    /// <summary>
    /// Final UJMW response JSON (only for completed / failed entries delivered by a processor).
    /// </summary>
    public string ResponseJson {
      get {
        return _ResponseJson;
      }
      set {
        _ResponseJson = value;
      }
    }

    /// <summary>
    /// Fault of the queue framework itself (for example 'processor lost'), otherwise null.
    /// </summary>
    public string Fault {
      get {
        return _Fault;
      }
      set {
        _Fault = value;
      }
    }

  }

  /// <summary>
  /// Allocation of a queue entry for a processor (dequeue result). Empty, if 'RequestId' is null.
  /// </summary>
  public class UjmwQueueLease {

    private int _TotalInQueue = 0;
    private string _MethodName = null;
    private string _RequestId = null;
    private string _RequestJson = null;
    private string _PollingSecret = null;
    private int _HeartbeatSec = 0;
    private string _Fault = null;

    /// <summary>
    /// Number of entries which are still waiting in the queue.
    /// </summary>
    public int TotalInQueue {
      get {
        return _TotalInQueue;
      }
      set {
        _TotalInQueue = value;
      }
    }

    /// <summary>
    /// Transport-level method name of the allocated entry.
    /// </summary>
    public string MethodName {
      get {
        return _MethodName;
      }
      set {
        _MethodName = value;
      }
    }

    /// <summary>
    /// Id of the allocated entry (null = nothing allocated).
    /// </summary>
    public string RequestId {
      get {
        return _RequestId;
      }
      set {
        _RequestId = value;
      }
    }

    /// <summary>
    /// The UJMW request JSON (incl. the sidechannel '_') exactly as a regular host would receive it.
    /// </summary>
    public string RequestJson {
      get {
        return _RequestJson;
      }
      set {
        _RequestJson = value;
      }
    }

    /// <summary>
    /// Per-allocation secret for 'pollingSalt' / 'pollingHash' of the subsequent PUT / DELETE / POST requests.
    /// </summary>
    public string PollingSecret {
      get {
        return _PollingSecret;
      }
      set {
        _PollingSecret = value;
      }
    }

    /// <summary>
    /// Expected heartbeat interval in seconds.
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
    /// Queue framework fault, otherwise null.
    /// </summary>
    public string Fault {
      get {
        return _Fault;
      }
      set {
        _Fault = value;
      }
    }

    /// <summary>
    /// True if an entry was allocated.
    /// </summary>
    public bool HasEntry {
      get {
        return (_RequestId != null);
      }
    }

  }

  /// <summary>
  /// Result of a heartbeat.
  /// </summary>
  public class UjmwQueueHeartbeatResult {

    private bool _CancellationRequested = false;
    private string _Fault = null;

    /// <summary>
    /// True if the processor must cancel (and discard) the job.
    /// </summary>
    public bool CancellationRequested {
      get {
        return _CancellationRequested;
      }
      set {
        _CancellationRequested = value;
      }
    }

    /// <summary>
    /// Queue framework fault, otherwise null.
    /// </summary>
    public string Fault {
      get {
        return _Fault;
      }
      set {
        _Fault = value;
      }
    }

  }

  /// <summary>
  /// Generic result of a queue operation.
  /// </summary>
  public class UjmwQueueResult {

    private string _Fault = null;

    /// <summary>
    /// Queue framework fault, otherwise null.
    /// </summary>
    public string Fault {
      get {
        return _Fault;
      }
      set {
        _Fault = value;
      }
    }

  }

  /// <summary>
  /// Counters of a queue (total or per method).
  /// </summary>
  public class UjmwQueueStats {

    private int _Queued = 0;
    private int _Active = 0;
    private int _Past1m = 0;
    private int _Past10m = 0;
    private int _Past1h = 0;
    private int _Past10h = 0;

    /// <summary>
    /// Entries waiting for a processor.
    /// </summary>
    public int Queued {
      get {
        return _Queued;
      }
      set {
        _Queued = value;
      }
    }

    /// <summary>
    /// Entries currently allocated / leased by a processor.
    /// </summary>
    public int Active {
      get {
        return _Active;
      }
      set {
        _Active = value;
      }
    }

    /// <summary>
    /// Entries terminated within the past minute.
    /// </summary>
    public int Past1m {
      get {
        return _Past1m;
      }
      set {
        _Past1m = value;
      }
    }

    /// <summary>
    /// Entries terminated within the past 10 minutes.
    /// </summary>
    public int Past10m {
      get {
        return _Past10m;
      }
      set {
        _Past10m = value;
      }
    }

    /// <summary>
    /// Entries terminated within the past hour.
    /// </summary>
    public int Past1h {
      get {
        return _Past1h;
      }
      set {
        _Past1h = value;
      }
    }

    /// <summary>
    /// Entries terminated within the past 10 hours.
    /// </summary>
    public int Past10h {
      get {
        return _Past10h;
      }
      set {
        _Past10h = value;
      }
    }

  }

  /// <summary>
  /// Statistics of a processor (identified by its informational name).
  /// </summary>
  public class UjmwQueueProcessorInfo {

    private int _Active = 0;
    private DateTime _LastActivity = DateTime.MinValue;

    /// <summary>
    /// Entries currently allocated / leased by this processor.
    /// </summary>
    public int Active {
      get {
        return _Active;
      }
      set {
        _Active = value;
      }
    }

    /// <summary>
    /// UTC time of the latest dequeue, heartbeat or completion.
    /// </summary>
    public DateTime LastActivity {
      get {
        return _LastActivity;
      }
      set {
        _LastActivity = value;
      }
    }

  }

  /// <summary>
  /// Statistics of a queue (result of the info endpoint).
  /// </summary>
  public class UjmwQueueInfo {

    private UjmwQueueStats _StatsTotal = new UjmwQueueStats();
    private Dictionary<string, UjmwQueueStats> _StatsPerMethod = new Dictionary<string, UjmwQueueStats>();
    private Dictionary<string, UjmwQueueProcessorInfo> _Processors = new Dictionary<string, UjmwQueueProcessorInfo>();
    private string _Fault = null;

    /// <summary>
    /// Counters over all methods.
    /// </summary>
    public UjmwQueueStats StatsTotal {
      get {
        return _StatsTotal;
      }
      set {
        _StatsTotal = value;
      }
    }

    /// <summary>
    /// Counters per transport-level method name.
    /// </summary>
    public Dictionary<string, UjmwQueueStats> StatsPerMethod {
      get {
        return _StatsPerMethod;
      }
      set {
        _StatsPerMethod = value;
      }
    }

    /// <summary>
    /// Known processors (informational names).
    /// </summary>
    public Dictionary<string, UjmwQueueProcessorInfo> Processors {
      get {
        return _Processors;
      }
      set {
        _Processors = value;
      }
    }

    /// <summary>
    /// Queue framework fault, otherwise null.
    /// </summary>
    public string Fault {
      get {
        return _Fault;
      }
      set {
        _Fault = value;
      }
    }

  }

}
