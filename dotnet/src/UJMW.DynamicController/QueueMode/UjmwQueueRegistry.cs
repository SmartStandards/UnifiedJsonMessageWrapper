using System.Collections.Generic;

namespace System.Web.UJMW {

  /// <summary>
  /// Holds the queue metadata of all emitted queue-mode controllers (contract controllers and '_queue' controllers)
  /// and the bridges (one per contract controller type and queuing service instance, decided Q-KEY).
  /// </summary>
  internal static class UjmwQueueRegistry {

    private static readonly Dictionary<Type, QueueControllerInfo> _ControllerInfos = new Dictionary<Type, QueueControllerInfo>();
    private static readonly List<BridgeEntry> _Bridges = new List<BridgeEntry>();

    /// <summary>
    /// Registers an emitted controller type as queue-mode controller of the given contract type and queue name (= resolved route).
    /// </summary>
    public static void RegisterController(Type controllerType, Type contractType, string queueName) {

      lock (_ControllerInfos) {
        _ControllerInfos[controllerType] = new QueueControllerInfo(contractType, queueName);
      }

    }

    /// <summary>
    /// Returns the queue metadata of a controller type (false, if it is no queue-mode controller).
    /// </summary>
    public static bool TryGetControllerInfo(Type controllerType, out Type contractType, out string queueName) {

      contractType = null;
      queueName = null;

      QueueControllerInfo info;
      lock (_ControllerInfos) {
        if (!_ControllerInfos.TryGetValue(controllerType, out info)) {
          return false;
        }
      }

      contractType = info.ContractType;
      queueName = info.QueueName;

      return true;
    }

    /// <summary>
    /// Returns the bridge for a queue-mode contract controller (null, if the controller type is no queue-mode controller).
    /// </summary>
    public static IUjmwQueueBridge TryGetBridge(Type controllerType, object injectedInstance) {

      Type contractType;
      string queueName;
      if (!TryGetControllerInfo(controllerType, out contractType, out queueName)) {
        return null;
      }

      IUjmwQueuingService queuingService = injectedInstance as IUjmwQueuingService;
      if (queuingService == null) {
        throw new Exception($"The queue-mode controller '{controllerType.Name}' requires an 'IUjmwQueuingService' to be injected!");
      }

      lock (_Bridges) {

        foreach (BridgeEntry entry in _Bridges) {
          if (entry.ControllerType == controllerType && ReferenceEquals(entry.Bridge.QueuingService, queuingService)) {
            return entry.Bridge;
          }
        }

        UjmwQueueBridge bridge = new UjmwQueueBridge(queuingService, contractType, queueName);
        _Bridges.Add(new BridgeEntry(controllerType, bridge));

        return bridge;
      }

    }

    /// <summary>
    /// Cancels the outer operations of all bridges (the queuing service must still be alive, see C-7).
    /// </summary>
    public static void ShutdownBridges() {

      UjmwQueueBridge[] bridges;
      lock (_Bridges) {
        bridges = new UjmwQueueBridge[_Bridges.Count];
        for (int i = 0; i < _Bridges.Count; i++) {
          bridges[i] = _Bridges[i].Bridge;
        }
      }

      foreach (UjmwQueueBridge bridge in bridges) {
        bridge.Shutdown();
      }

    }

    private sealed class QueueControllerInfo {

      public QueueControllerInfo(Type contractType, string queueName) {
        this.ContractType = contractType;
        this.QueueName = queueName;
      }

      public readonly Type ContractType;
      public readonly string QueueName;

    }

    private sealed class BridgeEntry {

      public BridgeEntry(Type controllerType, UjmwQueueBridge bridge) {
        this.ControllerType = controllerType;
        this.Bridge = bridge;
      }

      public readonly Type ControllerType;
      public readonly UjmwQueueBridge Bridge;

    }

  }

}
