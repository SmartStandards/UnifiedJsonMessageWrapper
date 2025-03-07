﻿using Logging.SmartStandards;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Configuration;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Hosting;

namespace System.Web.UJMW.SelfAnnouncement {

  internal static class EndpointEnumerationHelper {

    private static System.Configuration.Configuration _WebConfig = null;
    public static System.Configuration.Configuration WebConfig { 
      get {
        if (_WebConfig == null){
          ExeConfigurationFileMap fileMap = new ExeConfigurationFileMap();
          fileMap.ExeConfigFilename = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
          _WebConfig = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
        }
        return _WebConfig;
      }
    }

    public static EndpointInfo[] EnumerateWcfEndpoints() {
      string webConfigFileFullName = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
      string applicatioRootDirectory = Path.GetDirectoryName(webConfigFileFullName);

      List<EndpointInfo> foundEndpoints = new List<EndpointInfo>();

      //1. via .svc-Files

      foreach (string svcFileFullName in Directory.GetFiles(applicatioRootDirectory, "*.svc", SearchOption.AllDirectories)) {
        
        string relativeUrl = svcFileFullName.Substring(applicatioRootDirectory.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
        string svcFileContent = File.ReadAllText(svcFileFullName);

        Type serviceType = TryPickServiceTypeFromSvcContent(svcFileContent);
        if(serviceType != null) {

          Type factoryType = TryPickFactoryTypeFromSvcContent(svcFileContent);

          EndpointInfo info = BuildEndpointInfo(serviceType, relativeUrl, factoryType);
          foundEndpoints.Add(info);

        }
              
      }

      //2. via ServiceActivation in Web.config

      var serviceModelConfiguration = WebConfig.GetSectionGroup("system.serviceModel");
      ServicesSection servicesConfiguration = serviceModelConfiguration.Sections["services"] as ServicesSection;

      ServiceHostingEnvironmentSection serviceHostingEnvironmentConfiguration = serviceModelConfiguration.Sections["serviceHostingEnvironment"] as ServiceHostingEnvironmentSection;
      if (serviceHostingEnvironmentConfiguration != null) {
        var serviceActivationConfigurations = serviceHostingEnvironmentConfiguration.ServiceActivations as ServiceActivationElementCollection;
        if (serviceActivationConfigurations != null) {
          foreach (var serviceActivationConfiguration in serviceActivationConfigurations.OfType<ServiceActivationElement>()) {
            string serviceTypeFullQualifiedName = serviceActivationConfiguration.Service;
            if (serviceActivationConfiguration.RelativeAddress.EndsWith(".svc")) {
  
              Type serviceType = PerformExtendedTypeSearch(serviceTypeFullQualifiedName);
              if (serviceType != null) {
                  
                Type factoryType = PerformExtendedTypeSearch(serviceActivationConfiguration.Factory);

                EndpointInfo info = BuildEndpointInfo(
                  serviceType, serviceActivationConfiguration.RelativeAddress, factoryType
                );
                foundEndpoints.Add(info);
              }
            }
          }
        }
      }

      return foundEndpoints.ToArray();
    }

    private static EndpointInfo BuildEndpointInfo(Type serviceType, string relativeUrl, Type factoryType) {

      Type contractType = null;
      UjmwHostConfiguration.ContractSelector.Invoke(serviceType, relativeUrl, out contractType);
      EndpointCategory epCategory = EndpointCategory.Unknown;

      if (contractType.FullName.Equals("SwaggerWcf.ISwaggerWcfEndpoint", StringComparison.CurrentCultureIgnoreCase)) {
        epCategory = EndpointCategory.SwaggerDefinition;
      }
      else if (serviceType == typeof(AnnouncementTriggerEndpoint)) {
        epCategory = EndpointCategory.AnnouncementTriggerEndpoint;
      }
      else if (factoryType != null) {
        if (factoryType == typeof(UjmwServiceHostFactory)) {
          epCategory = EndpointCategory.DynamicUjmwFacade;
        }
      }

      return new EndpointInfo(
        contractType,
        SelfAnnouncementHelper.BuildContractidentifyingName(contractType),
        serviceType.Name,
        relativeUrl,
        epCategory
      );

    }

    public static string TryGetEndpointAddressFromConfig(string serviceTypeName) {

      var serviceModelSectionGroup = ServiceModelSectionGroup.GetSectionGroup(WebConfig);

      foreach (ServiceElement serviceElement in serviceModelSectionGroup.Services.Services) {
        if (serviceElement.Name == serviceTypeName) {
          // Wenn der Service gefunden wird, geben wir die erste Basisadresse zurück
          if (serviceElement.Host.BaseAddresses.Count > 0) {
            return serviceElement.Host.BaseAddresses[0].BaseAddress;
          }
        }
      }

      return null;
    }

    /// <summary>
    /// Alle Urls haben ein '/' am ende!
    /// </summary>
    /// <returns></returns>
    public static string[] GetBaseAddressesFromConfig() {
      HashSet<string> baseAddresses = new HashSet<string>();

      var serviceModelSectionGroup = ServiceModelSectionGroup.GetSectionGroup(WebConfig);

      foreach (ServiceElement serviceElement in serviceModelSectionGroup.Services.Services) {
        foreach (BaseAddressElement baseAddressElement in serviceElement.Host.BaseAddresses) {
          Uri endpointAddress = new Uri(baseAddressElement.BaseAddress);
          string url = endpointAddress.AbsoluteUri.Substring(0, endpointAddress.AbsoluteUri.Length - endpointAddress.AbsolutePath.Length) + HostingEnvironment.ApplicationVirtualPath;
          //string url = $"{baseAddress.Scheme}://{baseAddress.Host}:{baseAddress.Port}{HostingEnvironment.ApplicationVirtualPath}/";
          baseAddresses.Add(url);
        }
      }

      return baseAddresses.ToArray();
    }

    internal static string GetBaseAddressFromCurrentRequest() {
      Uri requestUrl = null;
      try {
        if(OperationContext.Current != null) {
          // ggf. bei F5 in Visual Studio
          requestUrl = OperationContext.Current.RequestContext.RequestMessage.Headers.To;
        }
      }
      catch (Exception ex) {
      }
      if (requestUrl == null || string.IsNullOrWhiteSpace(requestUrl.AbsoluteUri)) {
        try {
          // in servicehost.exe
          //ACHTUNG HIER IST DER PORT MANCHMAL 80, OBWOHL DER VS DEV-SERVER UNTER EINEM ANDEREN LÄUFT
          if (HttpContext.Current != null) {
            requestUrl = HttpContext.Current.Request.Url;
          }
        }
        catch (Exception ex) {
        }
      }

      if (requestUrl == null || string.IsNullOrWhiteSpace(requestUrl.AbsoluteUri)) {
        return null;
      }
      else {

        string url = requestUrl.AbsoluteUri.Substring(0, requestUrl.AbsoluteUri.Length - requestUrl.AbsolutePath.Length) + HostingEnvironment.ApplicationVirtualPath;
        //string url = $"{requestUrl.Scheme}://{requestUrl.Host}:{requestUrl.Port}{HostingEnvironment.ApplicationVirtualPath}";
        if (url.EndsWith("/")) {
          return url;
        }
        else {
          return url + "/";
        }
      }

    }

    private static Type TryPickServiceTypeFromSvcContent(string svcFileContent) {
      Match match = Regex.Match(svcFileContent, @"Service=""(?<type>[\w\.]+)""");
      if (match.Success && !string.IsNullOrWhiteSpace(match.Groups["type"]?.Value)) {
        string typeName = match.Groups["type"].Value;
        return PerformExtendedTypeSearch(typeName);
      }
      return null;
    }

    private static Type TryPickFactoryTypeFromSvcContent(string svcFileContent) {
      Match match = Regex.Match(svcFileContent, @"Factory=""(?<type>[\w\.]+)""");
      if (match.Success && !string.IsNullOrWhiteSpace(match.Groups["type"]?.Value)) {
        string typeName = match.Groups["type"].Value;
        return PerformExtendedTypeSearch(typeName);
      }
      return null;
    }

    private static Type PerformExtendedTypeSearch(string typeName) {

      if (string.IsNullOrWhiteSpace(typeName)) {
        return null;
      }

      Type serviceType;
      try {
        //assembly-qualifierte namen werden i.d.r immer gefunden
        serviceType = Type.GetType(typeName);
        if (serviceType != null) {
          return serviceType;
        }
      }
      catch {
      }

      //bei typnamen ohne assembly-qualifizierung sollte man davon ausgehn,
      //dass es sich um services aus den direkt referenzierten oder der haupt-assembly
      //handelt, welche bereits geladen sein sollten
      foreach (Assembly ass in AppDomain.CurrentDomain.GetAssemblies()) {
        try {
          serviceType = ass.GetType(typeName);
          if (serviceType != null) {
            return serviceType;
          }
        }
        catch {
        }
      }

      return null;
    }

  }

}
