namespace System.Web.UJMW {

  /// <summary>
  /// Bridge between a queue-mode controller and the 'IUjmwQueuingService':
  /// provides the contract proxy (used as root service instance) and a dedicated operation store.
  /// </summary>
  internal interface IUjmwQueueBridge {

    /// <summary>
    /// Dynamic UJMW client proxy of the contract, whose transport is a 'UjmwQueueBridgeExecutor'.
    /// </summary>
    object ContractProxy { get; }

    /// <summary>
    /// Dedicated operation store without concurrency limit (see C-1 in 'doc/epics/epic-async-queue-mode.md').
    /// </summary>
    IUjmwOperationStore OperationStore { get; }

  }

  /// <summary>
  /// Default bridge implementation. One instance exists per queue-mode controller type and queuing service
  /// (= per contract type + resolved controller route, decided Q-KEY).
  /// </summary>
  internal sealed class UjmwQueueBridge : IUjmwQueueBridge {

    private readonly IUjmwQueuingService _QueuingService;
    private readonly object _ContractProxy;
    private readonly UjmwInMemoryOperationStore _OperationStore;

    /// <summary>
    /// Creates the bridge for the queue of the given contract type and queue name (= resolved controller route).
    /// </summary>
    public UjmwQueueBridge(IUjmwQueuingService queuingService, Type contractType, string queueName) {

      if (queuingService == null) {
        throw new ArgumentNullException(nameof(queuingService));
      }

      _QueuingService = queuingService;

      UjmwLongRunningOptions longRunningOptions = UjmwHostConfiguration.LongRunningOptions;
      UjmwQueueBridgeExecutor executor = new UjmwQueueBridgeExecutor(queuingService, contractType, queueName);

      UjmwInMemoryQueuingService inMemoryService = queuingService as UjmwInMemoryQueuingService;
      if (inMemoryService != null) {
        inMemoryService.RegisterQueue(contractType, queueName);
        executor.SynchronousCallTimeout = TimeSpan.FromSeconds(inMemoryService.Options.SynchronousCallTimeoutSec);
        longRunningOptions = inMemoryService.Options.LongRunningOptions;
      }

      string bridgeUrl = UjmwQueueBridgeExecutor.BuildBridgeUrl(contractType);
      _ContractProxy = DynamicClientFactory.CreateInstance(contractType, executor, () => bridgeUrl);

      _OperationStore = new UjmwInMemoryOperationStore(CreateUnlimitedOptions(longRunningOptions));

    }

    /// <summary>
    /// The queuing service this bridge is bound to.
    /// </summary>
    public IUjmwQueuingService QueuingService {
      get {
        return _QueuingService;
      }
    }

    /// <summary>
    /// Dynamic UJMW client proxy of the contract.
    /// </summary>
    public object ContractProxy {
      get {
        return _ContractProxy;
      }
    }

    /// <summary>
    /// Dedicated operation store without concurrency limit.
    /// </summary>
    public IUjmwOperationStore OperationStore {
      get {
        return _OperationStore;
      }
    }

    /// <summary>
    /// Cancels all outer operations of this bridge (the queuing service itself must outlive this call, see C-7).
    /// </summary>
    public void Shutdown() {
      _OperationStore.Shutdown();
    }

    /// <summary>
    /// The outer operations of queued calls only wait for a processor, so they must never be limited
    /// (otherwise they would starve each other). All other settings are taken over.
    /// </summary>
    private static UjmwLongRunningOptions CreateUnlimitedOptions(UjmwLongRunningOptions template) {

      UjmwLongRunningOptions options = new UjmwLongRunningOptions();
      options.MaxConcurrentLongRunningAsyncCalls = int.MaxValue;

      if (template != null) {
        options.CallbackBufferLimit = template.CallbackBufferLimit;
        options.FreezeTimeBeforeCallbackBufferOverflow = template.FreezeTimeBeforeCallbackBufferOverflow;
        options.MinOutcomePreservTime = template.MinOutcomePreservTime;
        options.MaxOutcomePreservTime = template.MaxOutcomePreservTime;
        options.OutcomeCleanupLimit = template.OutcomeCleanupLimit;
        options.ThrottlingLevel = template.ThrottlingLevel;
        options.CalculateNbf = template.CalculateNbf;
      }

      return options;
    }

  }

}
