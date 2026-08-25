using System;

namespace System.Web.UJMW {

  /// <summary>
  /// Provides global configuration values for the dynamic MCP endpoint.
  /// </summary>
  public sealed class DynamicMcpControllerOptions {

    private string _Route = "_mcp";
    private string _ServerName = "UJMW MCP Server";
    private string _ServerVersion = "1.0.0";
    private string _ApiGroupName = "MCP";

    /// <summary>
    /// Gets or sets the route of the MCP JSON-RPC endpoint.
    /// </summary>
    public string Route {
      get {
        return _Route;
      }
      set {
        _Route = value;
      }
    }

    /// <summary>
    /// Gets or sets the logical MCP server name returned during initialization.
    /// </summary>
    public string ServerName {
      get {
        return _ServerName;
      }
      set {
        _ServerName = value;
      }
    }

    /// <summary>
    /// Gets or sets the logical MCP server version returned during initialization.
    /// </summary>
    public string ServerVersion {
      get {
        return _ServerVersion;
      }
      set {
        _ServerVersion = value;
      }
    }

    /// <summary>
    /// Gets or sets the API Explorer group name used for the MCP endpoint itself.
    /// </summary>
    public string ApiGroupName {
      get {
        return _ApiGroupName;
      }
      set {
        _ApiGroupName = value;
      }
    }

  }

}
