using Microsoft.AspNetCore.Mvc.ApiExplorer;
using System;
using System.Web.UJMW;

namespace System.Web.UJMW.Mcp {

  /// <summary>
  /// Describes one API operation that is exposed as an MCP tool.
  /// </summary>
  public sealed class DynamicMcpToolDescriptor {

    private string _Name;
    private string _Description;
    private string _RelativePath;
    private string _HttpMethod;
    private Type _ServiceType;
    private DynamicUjmwControllerOptions _Options;
    private ApiDescription _ApiDescription;
    private Newtonsoft.Json.Linq.JObject _InputSchema;

    /// <summary>
    /// Gets or sets the MCP tool name.
    /// </summary>
    public string Name {
      get {
        return _Name;
      }
      set {
        _Name = value;
      }
    }

    /// <summary>
    /// Gets or sets the MCP tool description.
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
    /// Gets or sets the API relative path.
    /// </summary>
    public string RelativePath {
      get {
        return _RelativePath;
      }
      set {
        _RelativePath = value;
      }
    }

    /// <summary>
    /// Gets or sets the API HTTP method.
    /// </summary>
    public string HttpMethod {
      get {
        return _HttpMethod;
      }
      set {
        _HttpMethod = value;
      }
    }

    /// <summary>
    /// Gets or sets the service interface type.
    /// </summary>
    public Type ServiceType {
      get {
        return _ServiceType;
      }
      set {
        _ServiceType = value;
      }
    }

    /// <summary>
    /// Gets or sets the dynamic controller options.
    /// </summary>
    public DynamicUjmwControllerOptions Options {
      get {
        return _Options;
      }
      set {
        _Options = value;
      }
    }

    /// <summary>
    /// Gets or sets the API Explorer description.
    /// </summary>
    public ApiDescription ApiDescription {
      get {
        return _ApiDescription;
      }
      set {
        _ApiDescription = value;
      }
    }

    /// <summary>
    /// Gets or sets the MCP tool input schema.
    /// </summary>
    public Newtonsoft.Json.Linq.JObject InputSchema {
      get {
        return _InputSchema;
      }
      set {
        _InputSchema = value;
      }
    }

  }

}
