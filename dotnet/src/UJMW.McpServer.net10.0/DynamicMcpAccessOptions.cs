using System;

namespace System.Web.UJMW {

  /// <summary>
  /// Provides configuration values for one interface exposed through the dynamic MCP endpoint.
  /// </summary>
  public sealed class DynamicMcpAccessOptions {

    private string _ApiGroupName = null;
    private string _ToolNamePrefix = null;
    private string _ToolNamePattern = "{Interface}_{Method}";
    private string _Description = null;
    private bool _CopyAuthorizationHeader = true;
    private string[] _CopiedHeaderNames = Array.Empty<string>();

    /// <summary>
    /// Gets or sets an optional API Explorer group filter.
    /// </summary>
    public string ApiGroupName {
      get {
        return _ApiGroupName;
      }
      set {
        _ApiGroupName = value;
      }
    }

    /// <summary>
    /// Gets or sets an optional fixed prefix prepended to generated MCP tool names.
    /// </summary>
    public string ToolNamePrefix {
      get {
        return _ToolNamePrefix;
      }
      set {
        _ToolNamePrefix = value;
      }
    }

    /// <summary>
    /// Gets or sets the generated MCP tool name pattern.
    /// </summary>
    public string ToolNamePattern {
      get {
        return _ToolNamePattern;
      }
      set {
        _ToolNamePattern = value;
      }
    }

    /// <summary>
    /// Gets or sets a description prefix for all exposed methods of this interface.
    /// </summary>
    public string Description {
      get {
        return _Description;
      }
      set {
        _Description = value;
      }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the Authorization header from the MCP request is forwarded.
    /// </summary>
    public bool CopyAuthorizationHeader {
      get {
        return _CopyAuthorizationHeader;
      }
      set {
        _CopyAuthorizationHeader = value;
      }
    }

    /// <summary>
    /// Gets or sets additional request headers copied from the MCP request to the target API call.
    /// </summary>
    public string[] CopiedHeaderNames {
      get {
        return _CopiedHeaderNames;
      }
      set {
        _CopiedHeaderNames = value;
      }
    }

  }

}
