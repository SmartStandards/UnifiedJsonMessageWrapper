using System.Diagnostics;

namespace System.Web.UJMW.SelfAnnouncement {

  [DebuggerDisplay("{RelativeRoute}: {ContractType.Name}")]
  public class EndpointInfo {

    internal EndpointInfo(
      Type contractType,
      string controllerTitle,
      string relativeRoute,
      DynamicUjmwControllerOptions options
    ) {
      this.ContractType = contractType;
      this.ControllerTitle = controllerTitle;
      this.RelativeRoute = relativeRoute;
      if (relativeRoute.StartsWith("http", StringComparison.InvariantCultureIgnoreCase)) {
        throw new ArgumentException("RelativeRoute must not start with http-prefix)");
      }
      if (!this.RelativeRoute.StartsWith("/")) {
        this.RelativeRoute = "/" + this.RelativeRoute;
      }
      this.Options = options;
    }

    public Type ContractType { get; private set; }

    public string ControllerTitle { get; private set; }

    /// <summary>
    /// NOTE: has ALWAYS a leading slash (technical ensured)
    /// </summary>
    public string RelativeRoute { get; private set; }

    public DynamicUjmwControllerOptions Options { get; private set; }

  }

}
