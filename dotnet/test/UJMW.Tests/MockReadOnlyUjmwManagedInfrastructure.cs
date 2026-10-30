using System;
using System.Collections.Generic;
using System.Linq;

namespace System.Web.UJMW {

  /// <summary>
  /// Read-only mock implementation of 'IUjmwManagedInfrastructure' with static demo data.
  /// It is intended for manual tests of the UJMW Test Client: add the test assembly to the client,
  /// click the green highlighted type and register it as store provider.
  /// It supports 'Resolve' (based on the wellknown endpoints, where the zone name is used as sub route).
  /// </summary>
  public class MockReadOnlyUjmwManagedInfrastructure : IUjmwManagedInfrastructure {

    private const string _HostA = "https://server-a.example.com";
    private const string _HostB = "https://server-b.example.com";

    /// <summary>
    /// Returns the capabilities: read-only, but able to resolve urls.
    /// </summary>
    public UjmwInfrastructureCapabilities GetCapabilities() {

      UjmwInfrastructureCapabilities capabilities = new UjmwInfrastructureCapabilities();
      capabilities.ZonesReadOnly = true;
      capabilities.HostsReadOnly = true;
      capabilities.WellknownEndpointsReadOnly = true;
      capabilities.OAuthConfigurationsReadOnly = true;
      capabilities.CanResolve = true;

      return capabilities;
    }

    /// <summary>
    /// Returns a small zone tree ('Production/EU', 'Production/US', 'Test').
    /// </summary>
    public UjmwInfrastructureZone[] GetZones() {

      UjmwInfrastructureZone production = CreateZone("Production", "Productive systems");
      production.ChildZones = new UjmwInfrastructureZone[] {
        CreateZone("EU", "Productive systems (Europe)"),
        CreateZone("US", "Productive systems (United States)")
      };
      UjmwInfrastructureZone test = CreateZone("Test", "Test systems");

      return new UjmwInfrastructureZone[] { production, test };
    }

    /// <summary>
    /// Not supported (read-only store).
    /// </summary>
    public void SaveZones(UjmwInfrastructureZone[] rootZones) {
      throw new NotSupportedException("The mock store is read-only.");
    }

    /// <summary>
    /// Returns two demo hosts with web applications.
    /// </summary>
    public UjmwInfrastructureHost[] GetHosts() {

      UjmwInfrastructureHost hostA = new UjmwInfrastructureHost();
      hostA.HostUrl = _HostA;
      hostA.Description = "Demo server A";
      hostA.WebApplications = new UjmwWebApplication[] {
        CreateWebApplication("DemoWebService", "demo"),
        CreateWebApplication("Backoffice", "apps/backoffice")
      };

      UjmwInfrastructureHost hostB = new UjmwInfrastructureHost();
      hostB.HostUrl = _HostB;
      hostB.Description = "Demo server B";
      hostB.WebApplications = new UjmwWebApplication[] {
        CreateWebApplication("DemoWebService", "demo")
      };

      return new UjmwInfrastructureHost[] { hostA, hostB };
    }

    /// <summary>
    /// Not supported (read-only store).
    /// </summary>
    public void SaveHosts(UjmwInfrastructureHost[] hosts) {
      throw new NotSupportedException("The mock store is read-only.");
    }

    /// <summary>
    /// Returns one demo OAuth configuration.
    /// </summary>
    public UjmwOAuthConfiguration[] GetOAuthConfigurations() {

      UjmwOAuthConfiguration configuration = new UjmwOAuthConfiguration();
      configuration.Name = "Mock IdP";
      configuration.AuthorizeUrl = "https://idp.example.com/authorize";
      configuration.TokenUrl = "https://idp.example.com/token";
      configuration.ClientId = "mock-client";
      configuration.ClientSecret = string.Empty;
      configuration.RedirectUri = "http://localhost/oauth-callback";
      configuration.Scope = "openid";
      configuration.UsePkce = true;

      return new UjmwOAuthConfiguration[] { configuration };
    }

    /// <summary>
    /// Not supported (read-only store).
    /// </summary>
    public void SaveOAuthConfigurations(UjmwOAuthConfiguration[] configurations) {
      throw new NotSupportedException("The mock store is read-only.");
    }

    /// <summary>
    /// Returns a wellknown endpoint on each demo 'DemoWebService' application for every contract
    /// (the endpoint url is the UJMW contract name).
    /// </summary>
    public UjmwWellknownEndpoint[] GetWellknownEndpoints(string contractName) {

      if (string.IsNullOrWhiteSpace(contractName)) {
        return new UjmwWellknownEndpoint[0];
      }

      bool isInterface = IsInterfaceName(contractName);
      string endpointUrl = UjmwContractNaming.GetContractName(contractName, isInterface);

      return new UjmwWellknownEndpoint[] {
        CreateEndpoint(contractName, _HostA, "DemoWebService", endpointUrl),
        CreateEndpoint(contractName, _HostB, "DemoWebService", endpointUrl)
      };
    }

    /// <summary>
    /// Not supported (read-only store).
    /// </summary>
    public void SaveWellknownEndpoint(UjmwWellknownEndpoint endpoint) {
      throw new NotSupportedException("The mock store is read-only.");
    }

    /// <summary>
    /// Not supported (read-only store).
    /// </summary>
    public void DeleteWellknownEndpoint(string contractName, string hostUrl, string webApplicationName) {
      throw new NotSupportedException("The mock store is read-only.");
    }

    /// <summary>
    /// Resolves the urls of a contract: zones below 'Production' are mapped to server A,
    /// all other zones to server B. The last segment of the zone path is used as sub route.
    /// </summary>
    public string[] Resolve(string contractName, string zone) {

      if (string.IsNullOrWhiteSpace(contractName) || string.IsNullOrWhiteSpace(zone)) {
        return new string[0];
      }

      string host = _HostB;
      if (zone.StartsWith("Production", StringComparison.OrdinalIgnoreCase)) {
        host = _HostA;
      }

      string[] zoneSegments = zone.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
      string subRoute = zoneSegments.Last().Trim().ToLowerInvariant();
      string endpointUrl = UjmwContractNaming.GetContractName(contractName, IsInterfaceName(contractName));

      return new string[] { host + "/demo/" + subRoute + "/" + endpointUrl };
    }

    /// <summary>
    /// Heuristic for the mock: a type name like 'IFoo' is treated as interface.
    /// </summary>
    private static bool IsInterfaceName(string typeName) {

      string shortName = typeName;
      int lastDot = typeName.LastIndexOf('.');
      if (lastDot >= 0) {
        shortName = typeName.Substring(lastDot + 1);
      }

      return (shortName.Length > 1 && shortName[0] == 'I' && char.IsUpper(shortName[1]));
    }

    /// <summary>
    /// Creates a zone without children.
    /// </summary>
    private static UjmwInfrastructureZone CreateZone(string name, string description) {

      UjmwInfrastructureZone zone = new UjmwInfrastructureZone();
      zone.Name = name;
      zone.Description = description;
      zone.ChildZones = new UjmwInfrastructureZone[0];

      return zone;
    }

    /// <summary>
    /// Creates a web application.
    /// </summary>
    private static UjmwWebApplication CreateWebApplication(string name, string applicationBaseUrl) {

      UjmwWebApplication application = new UjmwWebApplication();
      application.Name = name;
      application.ApplicationBaseUrl = applicationBaseUrl;

      return application;
    }

    /// <summary>
    /// Creates a wellknown endpoint.
    /// </summary>
    private static UjmwWellknownEndpoint CreateEndpoint(string contractName, string hostUrl, string webApplicationName, string endpointUrl) {

      UjmwWellknownEndpoint endpoint = new UjmwWellknownEndpoint();
      endpoint.ContractName = contractName;
      endpoint.HostUrl = hostUrl;
      endpoint.WebApplicationName = webApplicationName;
      endpoint.EndpointUrl = endpointUrl;

      return endpoint;
    }

  }

}
