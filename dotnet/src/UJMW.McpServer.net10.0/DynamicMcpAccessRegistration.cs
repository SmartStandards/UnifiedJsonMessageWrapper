using System;

namespace System.Web.UJMW {

  /// <summary>
  /// Stores one interface registration for the dynamic MCP endpoint.
  /// </summary>
  internal sealed class DynamicMcpAccessRegistration {

    private Type _ServiceType;
    private DynamicMcpAccessOptions _Options;

    /// <summary>
    /// Creates a new registration for one service interface.
    /// </summary>
    /// <param name="serviceType">The service interface type.</param>
    /// <param name="options">The MCP exposure options.</param>
    public DynamicMcpAccessRegistration(Type serviceType, DynamicMcpAccessOptions options) {
      _ServiceType = serviceType;
      _Options = options;
    }

    /// <summary>
    /// Gets the service interface type.
    /// </summary>
    public Type ServiceType {
      get {
        return _ServiceType;
      }
    }

    /// <summary>
    /// Gets the MCP exposure options.
    /// </summary>
    public DynamicMcpAccessOptions Options {
      get {
        return _Options;
      }
    }

  }

}
