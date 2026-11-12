using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Reflection;
using System.Text;

namespace System.Web.UJMW {

  /// <summary>
  /// Logic of the '&lt;route&gt;/_queue' endpoint (processor side of the UJMW async queue mode, wire protocol see
  /// 'doc/epics/epic-async-queue-mode.md', chapter 5). The factory emits only a tiny subclass per queue
  /// carrying the route, api-group and auth attributes.
  /// </summary>
  public abstract class UjmwQueueControllerBase : Controller {

    private const string _JsonMimeType = "application/json";

    private static readonly MethodInfo _GetInfoMethod = typeof(IUjmwQueue).GetMethod(nameof(IUjmwQueue.GetInfo));
    private static readonly MethodInfo _DequeueMethod = typeof(IUjmwQueue).GetMethod(nameof(IUjmwQueue.Dequeue));
    private static readonly MethodInfo _HeartbeatMethod = typeof(IUjmwQueue).GetMethod(nameof(IUjmwQueue.Heartbeat));
    private static readonly MethodInfo _ReturnMethod = typeof(IUjmwQueue).GetMethod(nameof(IUjmwQueue.Return));
    private static readonly MethodInfo _CompleteMethod = typeof(IUjmwQueue).GetMethod(nameof(IUjmwQueue.Complete));

    private readonly IUjmwQueuingService _QueuingService;
    private readonly Type _ContractType;
    private readonly string _QueueName;

    /// <summary>
    /// Creates the controller (the queuing service is injected from DI).
    /// </summary>
    protected UjmwQueueControllerBase(IUjmwQueuingService queuingService) {

      if (queuingService == null) {
        throw new ArgumentNullException(nameof(queuingService));
      }

      Type contractType;
      string queueName;
      if (!UjmwQueueRegistry.TryGetControllerInfo(this.GetType(), out contractType, out queueName)) {
        throw new InvalidOperationException($"The controller '{this.GetType().Name}' is not registered as UJMW queue controller!");
      }

      _QueuingService = queuingService;
      _ContractType = contractType;
      _QueueName = queueName;

    }

    /// <summary>
    /// The queued contract type (convention, also used by the auth hooks - decided Q-AUTH).
    /// </summary>
    public Type ContractType {
      get {
        return _ContractType;
      }
    }

    /// <summary>
    /// The queue name (= resolved controller route of the contract controller).
    /// </summary>
    public string QueueName {
      get {
        return _QueueName;
      }
    }

    #region " Endpoints "

    /// <summary>
    /// Info (without 'dequeue') or dequeue (with 'dequeue').
    /// </summary>
    [HttpGet]
    public IActionResult GetInfoOrDequeue(
      [FromQuery(Name = UjmwQueueProtocol.ProcessorQueryParam)] string processor,
      [FromQuery(Name = UjmwQueueProtocol.DequeueQueryParam)] string dequeue,
      [FromQuery(Name = UjmwQueueProtocol.CapacityQueryParam)] string capacity
    ) {

      if (string.IsNullOrWhiteSpace(dequeue)) {
        UjmwQueueInfo info = _QueuingService.GetInfo(_ContractType, _QueueName);
        return this.JsonResponse(UjmwQueueWire.SerializeInfo(info));
      }

      UjmwQueueMethodFilter filter;
      string error;
      if (!UjmwQueueMethodFilter.TryParse(dequeue, out filter, out error)) {
        UjmwQueueLease rejected = new UjmwQueueLease();
        rejected.Fault = error;
        return this.JsonResponse(UjmwQueueWire.SerializeLease(rejected));
      }

      UjmwQueueLease lease = _QueuingService.Dequeue(_ContractType, _QueueName, processor, filter, UjmwQueueProtocol.ParseCapacity(capacity));

      return this.JsonResponse(UjmwQueueWire.SerializeLease(lease));
    }

    /// <summary>
    /// Heartbeat (the first one confirms the dequeue). The optional body carries buffered callback invocations.
    /// </summary>
    [HttpPut]
    public IActionResult Heartbeat(
      [FromQuery(Name = UjmwQueueProtocol.ProcessorQueryParam)] string processor,
      [FromQuery(Name = UjmwQueueProtocol.RequestIdQueryParam)] string requestId,
      [FromQuery(Name = UjmwQueueProtocol.PollingSaltQueryParam)] string pollingSalt,
      [FromQuery(Name = UjmwQueueProtocol.PollingHashQueryParam)] string pollingHash
    ) {

      string callbackInvocationsJson = this.ReadRequestBody();
      if (string.IsNullOrWhiteSpace(callbackInvocationsJson)) {
        callbackInvocationsJson = null;
      }

      UjmwQueueHeartbeatResult result = _QueuingService.Heartbeat(
        _ContractType, _QueueName, processor, requestId, pollingSalt, pollingHash, callbackInvocationsJson
      );

      return this.JsonResponse(UjmwQueueWire.SerializeHeartbeatResult(result));
    }

    /// <summary>
    /// Returns a leased entry to the queue (or confirms a requested cancellation).
    /// </summary>
    [HttpDelete]
    public IActionResult Return(
      [FromQuery(Name = UjmwQueueProtocol.ProcessorQueryParam)] string processor,
      [FromQuery(Name = UjmwQueueProtocol.RequestIdQueryParam)] string requestId,
      [FromQuery(Name = UjmwQueueProtocol.PollingSaltQueryParam)] string pollingSalt,
      [FromQuery(Name = UjmwQueueProtocol.PollingHashQueryParam)] string pollingHash
    ) {

      UjmwQueueResult result = _QueuingService.Return(_ContractType, _QueueName, processor, requestId, pollingSalt, pollingHash);

      return this.JsonResponse(UjmwQueueWire.SerializeResult(result));
    }

    /// <summary>
    /// Delivers the final UJMW response (optionally combined with the dequeue of the next entry).
    /// </summary>
    [HttpPost]
    public IActionResult Complete(
      [FromQuery(Name = UjmwQueueProtocol.ProcessorQueryParam)] string processor,
      [FromQuery(Name = UjmwQueueProtocol.RequestIdQueryParam)] string requestId,
      [FromQuery(Name = UjmwQueueProtocol.PollingSaltQueryParam)] string pollingSalt,
      [FromQuery(Name = UjmwQueueProtocol.PollingHashQueryParam)] string pollingHash,
      [FromQuery(Name = UjmwQueueProtocol.CapacityQueryParam)] string capacity
    ) {

      string responseJson;
      UjmwQueueMethodFilter dequeueNext;
      string error;

      if (!UjmwQueueWire.TryParseCompletionBody(this.ReadRequestBody(), out responseJson, out dequeueNext, out error)) {
        UjmwQueueLease rejected = new UjmwQueueLease();
        rejected.Fault = error;
        return this.JsonResponse(UjmwQueueWire.SerializeCompletionResult(rejected, false));
      }

      UjmwQueueLease lease = _QueuingService.Complete(
        _ContractType, _QueueName, processor, requestId, pollingSalt, pollingHash, responseJson, dequeueNext,
        UjmwQueueProtocol.ParseCapacity(capacity)
      );

      return this.JsonResponse(UjmwQueueWire.SerializeCompletionResult(lease, dequeueNext != null));
    }

    #endregion

    #region " Auth Hook Support "

    /// <summary>
    /// Returns the reference method of 'IUjmwQueue' which corresponds to the current request (decided Q-AUTH).
    /// </summary>
    internal MethodInfo GetTargetQueueMethod(HttpRequest request) {

      string httpMethod = request.Method.ToUpperInvariant();

      if (httpMethod == "PUT") {
        return _HeartbeatMethod;
      }

      if (httpMethod == "DELETE") {
        return _ReturnMethod;
      }

      if (httpMethod == "POST") {
        return _CompleteMethod;
      }

      if (IsInfoRequest(request)) {
        return _GetInfoMethod;
      }

      return _DequeueMethod;
    }

    /// <summary>
    /// True, if the current request is an info request which may be accessed anonymously ('UjmwQueueOptions.AllowAnonymousInfo').
    /// </summary>
    internal bool IsAnonymousInfoRequest(HttpRequest request) {

      if (!IsInfoRequest(request)) {
        return false;
      }

      UjmwInMemoryQueuingService inMemoryService = _QueuingService as UjmwInMemoryQueuingService;
      if (inMemoryService == null) {
        return false;
      }

      return inMemoryService.Options.AllowAnonymousInfo;
    }

    private static bool IsInfoRequest(HttpRequest request) {

      if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)) {
        return false;
      }

      string dequeue = request.Query[UjmwQueueProtocol.DequeueQueryParam].ToString();

      return string.IsNullOrWhiteSpace(dequeue);
    }

    #endregion

    #region " Helpers "

    private IActionResult JsonResponse(string json) {

      //dequeue has side effects, so no response of this endpoint must ever be cached (V-1)
      this.HttpContext.Response.Headers["Cache-Control"] = "no-store";

      return this.Content(json, _JsonMimeType);
    }

    private string ReadRequestBody() {

      using (StreamReader reader = new StreamReader(this.HttpContext.Request.Body, Encoding.UTF8)) {
        //synchronous IO is disabled by default in ASP.NET Core, so the async read is awaited blocking
        return reader.ReadToEndAsync().GetAwaiter().GetResult();
      }

    }

    #endregion

  }

}
