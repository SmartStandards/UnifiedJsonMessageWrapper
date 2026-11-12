#if NET5
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace System.Web.UJMW {

  /// <summary>
  /// Specification tests for the default in-memory queuing service of the async queue mode
  /// (see 'doc/epics/epic-async-queue-mode.md', chapters 4, 5 and 10).
  /// </summary>
  [TestClass]
  public class UjmwInMemoryQueuingServiceTests {

    private interface IQueueContract {
    }

    private const string _Queue = "api/queue";

    private DateTime _Now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    #region " Helpers "

    /// <summary>
    /// Creates a service which uses the test clock.
    /// </summary>
    private UjmwInMemoryQueuingService CreateService(UjmwQueueOptions options = null) {

      if (options == null) {
        options = new UjmwQueueOptions();
      }

      UjmwInMemoryQueuingService service = new UjmwInMemoryQueuingService(options, () => _Now);
      service.RegisterQueue(typeof(IQueueContract), _Queue);

      return service;
    }

    /// <summary>
    /// Creates a fresh salt and the corresponding hash for the given secret.
    /// </summary>
    private static void Auth(string secret, out string salt, out string hash) {

      salt = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSaltLength);
      hash = UjmwAsyncProtocol.ComputePollingHash(secret, salt);

    }

    private static UjmwQueueEntryState GetState(UjmwInMemoryQueuingService service, UjmwQueueTicket ticket) {

      string salt;
      string hash;
      Auth(ticket.PollingSecret, out salt, out hash);

      UjmwQueueEntryState state;
      Assert.IsTrue(service.TryGetState(typeof(IQueueContract), _Queue, ticket.EntryId, salt, hash, TimeSpan.Zero, out state));

      return state;
    }

    private static UjmwQueueHeartbeatResult Heartbeat(UjmwInMemoryQueuingService service, UjmwQueueLease lease, string callbacksJson = null) {

      string salt;
      string hash;
      Auth(lease.PollingSecret, out salt, out hash);

      return service.Heartbeat(typeof(IQueueContract), _Queue, "worker", lease.RequestId, salt, hash, callbacksJson);
    }

    private static UjmwQueueResult Return(UjmwInMemoryQueuingService service, UjmwQueueLease lease) {

      string salt;
      string hash;
      Auth(lease.PollingSecret, out salt, out hash);

      return service.Return(typeof(IQueueContract), _Queue, "worker", lease.RequestId, salt, hash);
    }

    private static UjmwQueueLease Complete(UjmwInMemoryQueuingService service, UjmwQueueLease lease, string responseJson, UjmwQueueMethodFilter next = null) {

      string salt;
      string hash;
      Auth(lease.PollingSecret, out salt, out hash);

      return service.Complete(typeof(IQueueContract), _Queue, "worker", lease.RequestId, salt, hash, responseJson, next, -1);
    }

    private static UjmwQueueLease Dequeue(UjmwInMemoryQueuingService service, UjmwQueueMethodFilter filter = null) {

      if (filter == null) {
        filter = UjmwQueueMethodFilter.All;
      }

      return service.Dequeue(typeof(IQueueContract), _Queue, "worker", filter, -1);
    }

    private static UjmwQueueTicket Enqueue(UjmwInMemoryQueuingService service, string methodName, string requestJson = "{}") {
      return service.Enqueue(typeof(IQueueContract), _Queue, methodName, requestJson, true);
    }

    #endregion

    [TestMethod]
    public void Enqueue_Dequeue_Confirm_Complete_DeliversResponseToCaller() {

      UjmwInMemoryQueuingService service = this.CreateService();

      UjmwQueueTicket ticket = Enqueue(service, "Foo", "{\"a\":1}");
      Assert.IsNull(ticket.Fault);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateQueued, GetState(service, ticket).ExecState);

      UjmwQueueLease lease = Dequeue(service);
      Assert.IsTrue(lease.HasEntry);
      Assert.AreEqual("Foo", lease.MethodName);
      Assert.AreEqual("{\"a\":1}", lease.RequestJson);
      Assert.AreEqual(3, lease.HeartbeatSec);

      UjmwQueueHeartbeatResult heartbeat = Heartbeat(service, lease);
      Assert.IsNull(heartbeat.Fault);
      Assert.IsFalse(heartbeat.CancellationRequested);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateRunning, GetState(service, ticket).ExecState);

      UjmwQueueLease next = Complete(service, lease, "{\"return\":42,\"fault\":null}");
      Assert.IsNull(next.Fault);
      Assert.IsFalse(next.HasEntry);

      UjmwQueueEntryState state = GetState(service, ticket);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCompleted, state.ExecState);
      Assert.AreEqual("{\"return\":42,\"fault\":null}", state.ResponseJson);

    }

    [TestMethod]
    public void Complete_WithBusinessFault_TerminatesAsFailed() {

      UjmwInMemoryQueuingService service = this.CreateService();

      UjmwQueueTicket ticket = Enqueue(service, "Foo");
      UjmwQueueLease lease = Dequeue(service);
      Heartbeat(service, lease);
      Complete(service, lease, "{\"fault\":\"boom\"}");

      Assert.AreEqual(UjmwAsyncProtocol._ExecStateFailed, GetState(service, ticket).ExecState);

    }

    [TestMethod]
    public void Enqueue_FullQueue_IsRejectedImmediately() {

      UjmwQueueOptions options = new UjmwQueueOptions();
      options.MaxQueueSize = 2;
      UjmwInMemoryQueuingService service = this.CreateService(options);

      Assert.IsNull(Enqueue(service, "Foo").Fault);
      Assert.IsNull(Enqueue(service, "Foo").Fault);

      UjmwQueueTicket rejected = Enqueue(service, "Foo");
      Assert.AreEqual(UjmwQueueProtocol.FaultCapacityExceeded, rejected.Fault);
      Assert.IsNull(rejected.EntryId);

    }

    [TestMethod]
    public void Dequeue_Filters_TakeFrontMostMatchingEntry() {

      UjmwInMemoryQueuingService service = this.CreateService();

      Enqueue(service, "A");
      Enqueue(service, "B");
      Enqueue(service, "C");

      UjmwQueueLease whitelisted = Dequeue(service, UjmwQueueMethodFilter.Whitelist(new string[] { "C" }));
      Assert.AreEqual("C", whitelisted.MethodName);

      UjmwQueueLease blacklisted = Dequeue(service, UjmwQueueMethodFilter.Blacklist(new string[] { "A" }));
      Assert.AreEqual("B", blacklisted.MethodName);

      UjmwQueueLease nothing = Dequeue(service, UjmwQueueMethodFilter.Whitelist(new string[] { "X" }));
      Assert.IsFalse(nothing.HasEntry);
      Assert.AreEqual(1, nothing.TotalInQueue);

      UjmwQueueLease all = Dequeue(service);
      Assert.AreEqual("A", all.MethodName);

    }

    [TestMethod]
    public void MethodFilter_TryParse_SupportsWireSyntax() {

      UjmwQueueMethodFilter filter;
      string error;

      Assert.IsTrue(UjmwQueueMethodFilter.TryParse("*", out filter, out error));
      Assert.IsTrue(filter.IsMatch("Anything"));

      Assert.IsTrue(UjmwQueueMethodFilter.TryParse("m1,m4", out filter, out error));
      Assert.IsTrue(filter.IsMatch("m4"));
      Assert.IsFalse(filter.IsMatch("m2"));
      Assert.AreEqual("m1,m4", filter.ToString());

      Assert.IsTrue(UjmwQueueMethodFilter.TryParse("!m1,!m4", out filter, out error));
      Assert.IsFalse(filter.IsMatch("m1"));
      Assert.IsTrue(filter.IsMatch("m2"));
      Assert.AreEqual("!m1,!m4", filter.ToString());

      Assert.IsFalse(UjmwQueueMethodFilter.TryParse("m1,!m4", out filter, out error));
      Assert.IsNotNull(error);

    }

    [TestMethod]
    public void Allocation_NotConfirmedInTime_IsWithdrawnAndRedeliveredWithPriority() {

      UjmwInMemoryQueuingService service = this.CreateService();

      Enqueue(service, "First");
      Enqueue(service, "Second");

      UjmwQueueLease lease = Dequeue(service);
      Assert.AreEqual("First", lease.MethodName);

      _Now = _Now.AddSeconds(3);

      UjmwQueueHeartbeatResult late = Heartbeat(service, lease);
      Assert.IsTrue(late.CancellationRequested);
      Assert.AreEqual(UjmwQueueProtocol.FaultConfirmationMissed, late.Fault);

      UjmwQueueLease redelivered = Dequeue(service);
      Assert.AreEqual("First", redelivered.MethodName);
      Assert.AreNotEqual(lease.PollingSecret, redelivered.PollingSecret);

    }

    [TestMethod]
    public void LeaseLoss_DefaultPolicy_TerminatesWithProcessorLost() {

      UjmwInMemoryQueuingService service = this.CreateService();

      UjmwQueueTicket ticket = Enqueue(service, "Foo");
      UjmwQueueLease lease = Dequeue(service);
      Heartbeat(service, lease);

      _Now = _Now.AddSeconds(61);

      UjmwQueueEntryState state = GetState(service, ticket);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateFailed, state.ExecState);
      Assert.AreEqual(UjmwQueueProtocol.FaultProcessorLost, state.Fault);

      UjmwQueueLease rejected = Complete(service, lease, "{}");
      Assert.AreEqual(UjmwQueueProtocol.FaultProcessorLost, rejected.Fault);

    }

    [TestMethod]
    public void LeaseLoss_RedeliveryPolicy_RequeuesEntryWithPriority() {

      UjmwQueueOptions options = new UjmwQueueOptions();
      int observedAttempt = 0;
      options.RedeliveryPolicy = (Type contractType, string queueName, string methodName, int attempt, UjmwQueueLeaseLossReason reason) => {
        observedAttempt = attempt;
        return true;
      };
      UjmwInMemoryQueuingService service = this.CreateService(options);

      UjmwQueueTicket ticket = Enqueue(service, "Foo");
      Enqueue(service, "Bar");
      UjmwQueueLease lease = Dequeue(service);
      Heartbeat(service, lease);

      _Now = _Now.AddSeconds(61);

      Assert.AreEqual(UjmwAsyncProtocol._ExecStateQueued, GetState(service, ticket).ExecState);
      Assert.AreEqual(1, observedAttempt);
      Assert.AreEqual("Foo", Dequeue(service).MethodName);

    }

    [TestMethod]
    public void Return_RequeuesByFibonacciPositions_AndTerminatesAfterTenReleases() {

      UjmwInMemoryQueuingService service = this.CreateService();

      UjmwQueueTicket ticket = Enqueue(service, "Poison");
      for (int i = 0; i < 5; i++) {
        Enqueue(service, "Other" + i.ToString());
      }

      //release 1 -> index 0
      UjmwQueueLease lease = Dequeue(service);
      Heartbeat(service, lease);
      Assert.IsNull(Return(service, lease).Fault);
      Assert.AreEqual("Poison", Dequeue(service, UjmwQueueMethodFilter.All).MethodName);

      Assert.AreEqual(0, UjmwQueueProtocol.GetReinsertionIndex(1));
      Assert.AreEqual(1, UjmwQueueProtocol.GetReinsertionIndex(2));
      Assert.AreEqual(5, UjmwQueueProtocol.GetReinsertionIndex(5));
      Assert.AreEqual(55, UjmwQueueProtocol.GetReinsertionIndex(10));

      UjmwQueueMethodFilter onlyPoison = UjmwQueueMethodFilter.Whitelist(new string[] { "Poison" });

      //the entry is currently allocated (dequeued above) -> 9 more releases are still requeued
      lease = null;
      for (int release = 2; release <= 10; release++) {

        if (lease == null) {
          _Now = _Now.AddSeconds(3); //withdraw the open allocation of the check above (does not count)
        }

        lease = Dequeue(service, onlyPoison);
        Assert.IsTrue(lease.HasEntry, "release " + release.ToString());
        Heartbeat(service, lease);
        Assert.IsNull(Return(service, lease).Fault);

      }

      lease = Dequeue(service, onlyPoison);
      Heartbeat(service, lease);
      Return(service, lease);

      UjmwQueueEntryState state = GetState(service, ticket);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateFailed, state.ExecState);
      Assert.AreEqual(UjmwQueueProtocol.FaultReleaseLimit, state.Fault);

    }

    [TestMethod]
    public void Return_SecondRelease_IsReinsertedAtIndexOne() {

      UjmwInMemoryQueuingService service = this.CreateService();

      Enqueue(service, "X");
      Enqueue(service, "A");
      Enqueue(service, "B");

      UjmwQueueLease lease = Dequeue(service);
      Heartbeat(service, lease);
      Return(service, lease); //release 1 -> X, A, B

      lease = Dequeue(service);
      Assert.AreEqual("X", lease.MethodName);
      Heartbeat(service, lease);
      Return(service, lease); //release 2 -> A, X, B

      Assert.AreEqual("A", Dequeue(service).MethodName);
      Assert.AreEqual("X", Dequeue(service).MethodName);
      Assert.AreEqual("B", Dequeue(service).MethodName);

    }

    [TestMethod]
    public void Cancel_QueuedEntry_TerminatesImmediately() {

      UjmwInMemoryQueuingService service = this.CreateService();

      UjmwQueueTicket ticket = Enqueue(service, "Foo");

      string salt;
      string hash;
      Auth(ticket.PollingSecret, out salt, out hash);

      UjmwQueueEntryState state;
      Assert.IsTrue(service.TryRequestCancel(typeof(IQueueContract), _Queue, ticket.EntryId, salt, hash, TimeSpan.Zero, out state));
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCancelled, state.ExecState);

      Assert.IsFalse(Dequeue(service).HasEntry);

    }

    [TestMethod]
    public void Cancel_LeasedEntry_IsSignalledViaHeartbeat_AndConfirmedViaReturn() {

      UjmwInMemoryQueuingService service = this.CreateService();

      UjmwQueueTicket ticket = Enqueue(service, "Foo");
      UjmwQueueLease lease = Dequeue(service);
      Heartbeat(service, lease);

      string salt;
      string hash;
      Auth(ticket.PollingSecret, out salt, out hash);

      UjmwQueueEntryState state;
      Assert.IsTrue(service.TryRequestCancel(typeof(IQueueContract), _Queue, ticket.EntryId, salt, hash, TimeSpan.Zero, out state));
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCancellationRequested, state.ExecState);

      Assert.IsTrue(Heartbeat(service, lease).CancellationRequested);

      Assert.IsNull(Return(service, lease).Fault);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCancelled, GetState(service, ticket).ExecState);

    }

    [TestMethod]
    public void Heartbeat_CallbackInvocations_AreDrainedByCallerInSequenceOrder() {

      UjmwInMemoryQueuingService service = this.CreateService();

      UjmwQueueTicket ticket = Enqueue(service, "Foo");
      UjmwQueueLease lease = Dequeue(service);
      Heartbeat(service, lease);

      Heartbeat(service, lease, "{\"reportProgressInvokes\":[{\"#\":2,\"arg1\":\"b\"},{\"#\":1,\"arg1\":\"a\"}]}");

      UjmwQueueEntryState state = GetState(service, ticket);
      JObject callbacks = JObject.Parse(state.CallbackInvocationsJson);
      JArray items = (JArray)callbacks["reportProgressInvokes"];

      Assert.AreEqual(2, items.Count);
      Assert.AreEqual(1, items[0]["#"].Value<int>());
      Assert.AreEqual(2, items[1]["#"].Value<int>());

      Assert.IsNull(GetState(service, ticket).CallbackInvocationsJson);

    }

    [TestMethod]
    public void Heartbeat_FullCallbackBuffer_RejectsInvocations() {

      UjmwQueueOptions options = new UjmwQueueOptions();
      UjmwLongRunningOptions longRunningOptions = new UjmwLongRunningOptions();
      longRunningOptions.CallbackBufferLimit = 2;
      options.LongRunningOptions = longRunningOptions;
      UjmwInMemoryQueuingService service = this.CreateService(options);

      Enqueue(service, "Foo");
      UjmwQueueLease lease = Dequeue(service);
      Heartbeat(service, lease);

      Assert.IsNull(Heartbeat(service, lease, "{\"cbInvokes\":[{\"#\":1},{\"#\":2}]}").Fault);
      Assert.AreEqual(UjmwQueueProtocol.FaultCallbackBufferFull, Heartbeat(service, lease, "{\"cbInvokes\":[{\"#\":3}]}").Fault);

    }

    [TestMethod]
    public void Lease_WrongHash_IsRejectedWithoutDisclosure() {

      UjmwInMemoryQueuingService service = this.CreateService();

      Enqueue(service, "Foo");
      UjmwQueueLease lease = Dequeue(service);

      UjmwQueueHeartbeatResult result = service.Heartbeat(typeof(IQueueContract), _Queue, "worker", lease.RequestId, "salt00", "wrong", null);
      Assert.AreEqual(UjmwQueueProtocol.FaultWrongRequestIdOrHash, result.Fault);

      UjmwQueueEntryState state;
      Assert.IsFalse(service.TryGetState(typeof(IQueueContract), _Queue, lease.RequestId, "salt00", "wrong", TimeSpan.Zero, out state));

    }

    [TestMethod]
    public void Complete_WithDequeueNext_AllocatesNextEntry() {

      UjmwInMemoryQueuingService service = this.CreateService();

      Enqueue(service, "A");
      Enqueue(service, "B");

      UjmwQueueLease lease = Dequeue(service);
      Heartbeat(service, lease);

      UjmwQueueLease next = Complete(service, lease, "{\"return\":1}", UjmwQueueMethodFilter.All);
      Assert.IsNull(next.Fault);
      Assert.AreEqual("B", next.MethodName);
      Assert.IsNotNull(next.PollingSecret);
      Assert.IsNull(Heartbeat(service, next).Fault);

    }

    [TestMethod]
    public void GetInfo_ReportsQueuedActiveProcessorsAndTerminations() {

      UjmwInMemoryQueuingService service = this.CreateService();

      Enqueue(service, "A");
      Enqueue(service, "A");
      Enqueue(service, "B");

      UjmwQueueLease lease = Dequeue(service);
      Heartbeat(service, lease);
      Complete(service, lease, "{}");

      Dequeue(service);

      UjmwQueueInfo info = service.GetInfo(typeof(IQueueContract), _Queue);

      Assert.IsNull(info.Fault);
      Assert.AreEqual(1, info.StatsTotal.Queued);
      Assert.AreEqual(1, info.StatsTotal.Active);
      Assert.AreEqual(1, info.StatsTotal.Past1m);
      Assert.AreEqual(1, info.StatsPerMethod["A"].Past10h);
      Assert.AreEqual(1, info.StatsPerMethod["B"].Queued);
      Assert.IsTrue(info.Processors.ContainsKey("worker"));
      Assert.AreEqual(1, info.Processors["worker"].Active);

    }

    [TestMethod]
    public void QueueName_Null_IsAmbiguousForMultipleRegistrations() {

      UjmwInMemoryQueuingService service = this.CreateService();
      service.RegisterQueue(typeof(IQueueContract), "api/other");

      UjmwQueueInfo info = service.GetInfo(typeof(IQueueContract), null);
      Assert.AreEqual(UjmwQueueProtocol.FaultAmbiguousQueue, info.Fault);

    }

    [TestMethod]
    public void Shutdown_RejectsEnqueue_AndDoesNotCancelLeasedEntries() {

      UjmwInMemoryQueuingService service = this.CreateService();

      UjmwQueueTicket ticket = Enqueue(service, "Foo");
      UjmwQueueLease lease = Dequeue(service);
      Heartbeat(service, lease);

      service.Shutdown();

      Assert.AreEqual(UjmwQueueProtocol.FaultHostShuttingDown, Enqueue(service, "Foo").Fault);

      string salt;
      string hash;
      Auth(ticket.PollingSecret, out salt, out hash);
      UjmwQueueEntryState state;
      service.TryRequestCancel(typeof(IQueueContract), _Queue, ticket.EntryId, salt, hash, TimeSpan.Zero, out state);

      Assert.IsFalse(Heartbeat(service, lease).CancellationRequested);

    }

    private static void Poll(UjmwInMemoryQueuingService service, string processorId) {
      service.Dequeue(typeof(IQueueContract), _Queue, processorId, UjmwQueueMethodFilter.All, -1);
    }

    [TestMethod]
    public void ProcessorCapacity_IsStoredInStats_AndMinusOneIfNotSupplied() {

      UjmwInMemoryQueuingService service = this.CreateService();

      service.Dequeue(typeof(IQueueContract), _Queue, "withCapacity", UjmwQueueMethodFilter.All, 4);
      service.Dequeue(typeof(IQueueContract), _Queue, "withoutCapacity", UjmwQueueMethodFilter.All, -1);

      UjmwQueueInfo info = service.GetInfo(typeof(IQueueContract), _Queue);

      Assert.AreEqual(4, info.Processors["withCapacity"].Capacity);
      Assert.AreEqual(-1, info.Processors["withoutCapacity"].Capacity);

      string json = UjmwQueueWire.SerializeInfo(info);
      Assert.IsTrue(json.Contains("\n"), "the stats JSON should be indented");

      UjmwQueueInfo parsed = UjmwQueueWire.ParseInfo(json);
      Assert.AreEqual(4, parsed.Processors["withCapacity"].Capacity);
      Assert.AreEqual(-1, parsed.Processors["withoutCapacity"].Capacity);

    }

    [TestMethod]
    public void ProcessorCapacity_ParseIgnoresInvalidValues() {

      Assert.AreEqual(-1, UjmwQueueProtocol.ParseCapacity(null));
      Assert.AreEqual(-1, UjmwQueueProtocol.ParseCapacity("abc"));
      Assert.AreEqual(-1, UjmwQueueProtocol.ParseCapacity("0"));
      Assert.AreEqual(3, UjmwQueueProtocol.ParseCapacity("3"));

    }

    [TestMethod]
    public void StaleProcessor_IsRemovedAfterOneDay_IfSameAgentHasAliveInstance() {

      UjmwInMemoryQueuingService service = this.CreateService();

      Poll(service, "agentA#01-01#10-00-00");
      Poll(service, "agentB#01-01#10-00-00");

      _Now = _Now.AddDays(1).AddMinutes(1);

      Poll(service, "agentA#01-02#10-01-00");

      UjmwQueueInfo info = service.GetInfo(typeof(IQueueContract), _Queue);

      Assert.IsFalse(info.Processors.ContainsKey("agentA#01-01#10-00-00"));
      Assert.IsTrue(info.Processors.ContainsKey("agentA#01-02#10-01-00"));

      //no alive instance of 'agentB' - the knowledge is kept
      Assert.IsTrue(info.Processors.ContainsKey("agentB#01-01#10-00-00"));

    }

    [TestMethod]
    public void StaleProcessor_IsKept_IfSameAgentInstanceIsNotAlive() {

      UjmwInMemoryQueuingService service = this.CreateService();

      Poll(service, "agentA#old");
      _Now = _Now.AddDays(1);
      Poll(service, "agentA#new");
      _Now = _Now.AddMinutes(5);

      UjmwQueueInfo info = service.GetInfo(typeof(IQueueContract), _Queue);

      Assert.IsTrue(info.Processors.ContainsKey("agentA#old"));
      Assert.IsTrue(info.Processors.ContainsKey("agentA#new"));

    }

    [TestMethod]
    public void ProcessorName_Convention_AgentNameAndGeneratedName() {

      Assert.AreEqual("host/IFoo", UjmwQueueProtocol.GetAgentName("host/IFoo#03-14#08-15-00"));
      Assert.AreEqual("plain", UjmwQueueProtocol.GetAgentName("plain"));

      string generated = UjmwQueueProtocol.GenerateProcessorName("host/IFoo");
      StringAssert.Matches(generated, new Text.RegularExpressions.Regex(@"^host/IFoo#\d{2}-\d{2}#\d{2}-\d{2}-\d{2}$"));

    }

  }

}
#endif
