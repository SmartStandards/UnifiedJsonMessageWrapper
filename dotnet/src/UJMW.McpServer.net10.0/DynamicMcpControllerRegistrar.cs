using System;
using System.Collections.Generic;
using System.Linq;

namespace System.Web.UJMW {

  /// <summary>
  /// Collects interface registrations exposed through the dynamic MCP endpoint.
  /// </summary>
  public sealed class DynamicMcpControllerRegistrar {

    private readonly List<DynamicMcpAccessRegistration> _Entries = new List<DynamicMcpAccessRegistration>();
    private readonly DynamicMcpControllerOptions _Options = new DynamicMcpControllerOptions();

    /// <summary>
    /// Gets or sets the route of the MCP JSON-RPC endpoint.
    /// </summary>
    public string Route {
      get {
        return _Options.Route;
      }
      set {
        _Options.Route = value;
      }
    }

    /// <summary>
    /// Gets or sets the logical MCP server name returned during initialization.
    /// </summary>
    public string ServerName {
      get {
        return _Options.ServerName;
      }
      set {
        _Options.ServerName = value;
      }
    }

    /// <summary>
    /// Gets or sets the logical MCP server version returned during initialization.
    /// </summary>
    public string ServerVersion {
      get {
        return _Options.ServerVersion;
      }
      set {
        _Options.ServerVersion = value;
      }
    }

    /// <summary>
    /// Adds MCP access to all API Explorer-visible operations of the given interface.
    /// </summary>
    /// <typeparam name="TService">The interface type exposed as MCP tools.</typeparam>
    /// <param name="optionsConfigurator">The optional access options configurator.</param>
    public void AddAccessTo<TService>(Action<DynamicMcpAccessOptions> optionsConfigurator = null) {
      this.AddAccessTo(typeof(TService), optionsConfigurator);
    }

    /// <summary>
    /// Adds MCP access to all API Explorer-visible operations of the given interface.
    /// </summary>
    /// <param name="serviceType">The interface type exposed as MCP tools.</param>
    /// <param name="optionsConfigurator">The optional access options configurator.</param>
    public void AddAccessTo(Type serviceType, Action<DynamicMcpAccessOptions> optionsConfigurator = null) {
      DynamicMcpAccessOptions accessOptions = new DynamicMcpAccessOptions();
      if (optionsConfigurator != null) {
        optionsConfigurator.Invoke(accessOptions);
      }
      this.AddAccessTo(serviceType, accessOptions);
    }

    /// <summary>
    /// Adds MCP access to all API Explorer-visible operations of the given interface.
    /// </summary>
    /// <param name="serviceType">The interface type exposed as MCP tools.</param>
    /// <param name="options">The access options.</param>
    public void AddAccessTo(Type serviceType, DynamicMcpAccessOptions options) {
      if (serviceType == null) {
        throw new ArgumentNullException(nameof(serviceType));
      }
      if (options == null) {
        options = new DynamicMcpAccessOptions();
      }
      _Entries.Add(new DynamicMcpAccessRegistration(serviceType, options));
    }

    /// <summary>
    /// Gets all registered interface access entries.
    /// </summary>
    internal DynamicMcpAccessRegistration[] Entries {
      get {
        return _Entries.ToArray();
      }
    }

    /// <summary>
    /// Gets the global MCP endpoint options.
    /// </summary>
    internal DynamicMcpControllerOptions Options {
      get {
        return _Options;
      }
    }

  }

}
