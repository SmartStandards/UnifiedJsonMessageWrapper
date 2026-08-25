namespace System.Web.UJMW {

  /// <summary>
  /// Describes one JSON-RPC request accepted by the dynamic MCP endpoint.
  /// </summary>
  public sealed class DynamicMcpJsonRpcRequest {

    /// <summary>
    /// Gets or sets the JSON-RPC protocol marker. Use "2.0".
    /// </summary>
    public string Jsonrpc { get; set; }

    /// <summary>
    /// Gets or sets the JSON-RPC request id.
    /// </summary>
    public object Id { get; set; }

    /// <summary>
    /// Gets or sets the MCP method name, for example "initialize", "tools/list" or "tools/call".
    /// </summary>
    public string Method { get; set; }

    /// <summary>
    /// Gets or sets the method parameters.
    /// </summary>
    public object Params { get; set; }

  }

}
