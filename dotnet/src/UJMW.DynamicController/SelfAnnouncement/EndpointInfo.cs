using System.Diagnostics;

namespace System.Web.UJMW.SelfAnnouncement {

  [DebuggerDisplay("{RelativeRoute} ({ContractIdentifyingName})")]
  public class EndpointInfo {

    internal EndpointInfo(
      Type contractType,
      string contractIdentifyingName,
      string controllerTitle,
      string relativeRoute,
      EndpointCategory endpointCategory,
      DynamicUjmwControllerOptions ujmwOptions = null
    ) {

      this.ContractType = contractType;
      this.ContractIdentifyingName = contractIdentifyingName;
      this.ControllerTitle = controllerTitle;
      this.ControllerTitle = controllerTitle;
      this.EndpointCategory = endpointCategory;

      if (relativeRoute.StartsWith("http", StringComparison.InvariantCultureIgnoreCase)) {
        throw new ArgumentException("RelativeRoute must not start with http-prefix)");
      }
      this.RelativeRoute = relativeRoute;
      if (this.RelativeRoute.StartsWith("/")) {
        this.RelativeRoute = this.RelativeRoute.Substring(1, this.RelativeRoute.Length - 1);
      }
      this.UjmwOptions = ujmwOptions;
    }

    /// <summary>
    /// WARNING: can be null, if the contract is not based on an interface-Type!
    /// </summary>
    public Type ContractType { get; private set; }

    public string ContractIdentifyingName { get; private set; }

    public string ControllerTitle { get; private set; }

    public EndpointCategory EndpointCategory { get; private set; }

    /// <summary>
    /// NOTE: has NEVER a leading slash (technical ensured)
    /// </summary>
    public string RelativeRoute { get; private set; }

    /// <summary>
    /// WARNING: can be null, if the endpoint is not an ujmw endpoint!
    /// </summary>
    public DynamicUjmwControllerOptions UjmwOptions { get; private set; }

    public override string ToString() {
      return $"{RelativeRoute} ({ContractIdentifyingName})";
    }

    /// <summary></summary>
    /// <param name="prependBaseUrl">Required a trailing slash!</param>
    /// <returns></returns>
    public string ToString(string prependBaseUrl) {
      return $"{prependBaseUrl}{RelativeRoute} ({ContractIdentifyingName})";
    }

  }

  public enum EndpointCategory {
    Unknown = 0,
    DynamicUjmwController = 1,
    AnnouncementTriggerEndpoint = 2
  }

}
