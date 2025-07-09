using System.Diagnostics;

namespace System.Web.UJMW.SelfAnnouncement {

  [DebuggerDisplay("{RelativeRoute} ({ContractIdentifyingName})")]
  public class EndpointInfo {

    internal EndpointInfo(
      Type contractType,
      string contractIdentifyingName,
      string title,
      string relativeRoute,
      EndpointCategory endpointCategory
    ) {

      this.ContractType = contractType;
      this.ContractIdentifyingName = contractIdentifyingName;
      this.Title = title;
      this.EndpointCategory = endpointCategory;

      if (relativeRoute.StartsWith("http", StringComparison.InvariantCultureIgnoreCase)) {
        throw new ArgumentException("RelativeRoute must not start with http-prefix)");
      }
      this.RelativeRoute = relativeRoute;
      if (this.RelativeRoute.StartsWith("/")) {
        this.RelativeRoute = this.RelativeRoute.Substring(1, this.RelativeRoute.Length - 1);
      }

    }

    /// <summary>
    /// WARNING: can be null, if the contract is not based on an interface-Type!
    /// </summary>
    public Type ContractType { get; private set; }

    public string ContractIdentifyingName { get; private set; }

    public string Title { get; private set; }

    public EndpointCategory EndpointCategory { get; private set; }

    /// <summary>
    /// NOTE: has NEVER a leading slash (technical ensured)
    /// </summary>
    public string RelativeRoute { get; private set; }

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
    DynamicUjmwFacade = 1,
    AnnouncementTriggerEndpoint = 2,
    SwaggerDefinition = 3,
    SwaggerUi = 4,
    Custom = 5
  }

}
