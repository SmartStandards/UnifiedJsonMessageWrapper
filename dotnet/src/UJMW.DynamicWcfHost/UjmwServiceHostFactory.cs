using Logging.SmartStandards;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.ServiceModel.Web;
using System.Text;
using System.Threading;
using System.Web.Hosting;
using System.Web.Services.Description;
using System.Xml;

using static System.Web.UJMW.CustomizedJsonFormatter;

using Message = System.ServiceModel.Channels.Message;
using ServiceDescription = System.ServiceModel.Description.ServiceDescription;

namespace System.Web.UJMW {

  //-------------------------------------------------------------------------------------------------------------------
  //  <system.serviceModel>
  //    <serviceHostingEnvironment aspNetCompatibilityEnabled="false" multipleSiteBindingsEnabled="true" >
  //      <serviceActivations>
  //        <add relativeAddress="v1/the-url.svc" service="TpeTpeTpe, AssAssAss"
  //             factory="System.Web.UJMW.UjmwServiceHostFactory, UJMW.DynamicWcfHost" />
  //-------------------------------------------------------------------------------------------------------------------

  public class UjmwServiceHostFactory : ServiceHostFactory {

    //needs to have an parameterless constructor, because
    //the typename of this class is just written into the Web.config
    public UjmwServiceHostFactory() {
    }

    private static WebHttpBinding _CustomizedWebHttpBindingSecured = null;
    private static WebHttpBinding _CustomizedWebHttpBinding = null;

    internal static string[] _CollectedApplicationBaseUrls = new string[] { };
    internal static string[] _CollectedEndpointRelativeUrls = new string[] { };

    /// <summary></summary>
    /// <param name="serviceImplementationType"></param>
    /// <param name="baseAddresses">ACHTUNG: MICROSOFT meint hier die baseAddresses der .svc-Datei selbst - also nicht die der Applikation!</param>
    /// <returns></returns>
    protected override ServiceHost CreateServiceHost(Type serviceImplementationType, Uri[] baseAddresses) {
      try {

        UjmwHostConfiguration.WaitForSetupCompleted();

        CollectUrlsFrom(baseAddresses);

        //TODO: macht dass wirklich sinn, dass wir immer nur die erste baseAddress nehmen?
        // -> was wenn localost oder der servername hier kommt, aber ein A-Record angefragt wird?
        Uri primaryUri = baseAddresses[0];

        if (UjmwHostConfiguration.ForceHttps) {
          primaryUri = new Uri(primaryUri.ToString().Replace("http://", "https://"));
        }

        bool contractFound = UjmwHostConfiguration.ContractSelector.Invoke(serviceImplementationType, primaryUri.ToString(), out Type contractInterface);

        DevLogger.LogTrace(0, 72001, $"Creating UjmwServiceHost for '{serviceImplementationType.FullName}' as '{contractInterface.FullName}' at '{primaryUri}'.");


        //TODO: sollten wir hier nicht ALLE baseAddresses angeben?
        ServiceHost host = new ServiceHost(serviceImplementationType, new Uri[] { primaryUri });

        var inboundSideChannelCfg = UjmwHostConfiguration.GetRequestSideChannelConfiguration(contractInterface);
        var outboundSideChannelCfg = UjmwHostConfiguration.GetResponseSideChannelConfiguration(contractInterface);

        var behavior = host.Description.Behaviors.Find<ServiceBehaviorAttribute>();
        behavior.InstanceContextMode = InstanceContextMode.Single;
        behavior.ConcurrencyMode = ConcurrencyMode.Multiple;

        var customizedContractDescription = CreateCustomizedContractDescription(
          contractInterface,
          inboundSideChannelCfg.UnderlinePropertyIsAccepted,
          outboundSideChannelCfg.UnderlinePropertyIsProvided,
          serviceImplementationType
        );

        var endpoint = new ServiceEndpoint(
          customizedContractDescription,
          GetCustomizedWebHttpBinding(),
          new EndpointAddress(primaryUri)
        );

        //TODO: sollte nwir hier nicht endpoints für ALLE baseAddresses adden?
        host.AddServiceEndpoint(endpoint);

        //https://weblogs.asp.net/scottgu/437027
        AuthenticationSchemes hostAuthenticationSchemes;
        if (UjmwHostConfiguration.RequireNtlm) {
          hostAuthenticationSchemes =  AuthenticationSchemes.Ntlm;
        }
        else{
          hostAuthenticationSchemes = AuthenticationSchemes.Anonymous | AuthenticationSchemes.Ntlm;
        }
        //it seems not to work properly when changing the host afterwards:
        //host.Authentication.AuthenticationSchemes = hostAuthenticationSchemes;

        DevLogger.LogTrace(0, 72002, $"CURRENT AuthenticationSchemes -> {hostAuthenticationSchemes}");

        CustomBinding customizedBinding = new CustomBinding(endpoint.Binding);
        WebMessageEncodingBindingElement encodingBindingElement = customizedBinding.Elements.Find<WebMessageEncodingBindingElement>();
        encodingBindingElement.ContentTypeMapper = new CustomizedJsonContentTypeMapper();
        endpoint.Binding = customizedBinding;
    
        bool requestWrapperContainsUnderline = inboundSideChannelCfg.AcceptedChannels.Contains("_");
        bool responseWrapperContainsUnderline = outboundSideChannelCfg.ChannelsToProvide.Contains("_");
        endpoint.Behaviors.Add(new CustomizedWebHttpBehaviourForJson(
          requestWrapperContainsUnderline, outboundSideChannelCfg
        ));

        ServiceMetadataBehavior metadataBehaviour;
        if (host.Description.Behaviors.Contains(typeof(ServiceMetadataBehavior))) {
          metadataBehaviour = host.Description.Behaviors.Find<ServiceMetadataBehavior>();
        }
        else {
          metadataBehaviour = new ServiceMetadataBehavior();
          host.Description.Behaviors.Add(metadataBehaviour);
        }
    
        if (UjmwHostConfiguration.ForceHttps) {
          metadataBehaviour.HttpsGetEnabled = true;
          metadataBehaviour.HttpGetEnabled = false;
        }
        else {
          metadataBehaviour.HttpGetEnabled = true;
        }

        ServiceDebugBehavior debugBehaviour;
        if (host.Description.Behaviors.Contains(typeof(ServiceDebugBehavior))) {
          debugBehaviour = host.Description.Behaviors.Find<ServiceDebugBehavior>();
        }
        else {
          debugBehaviour = new ServiceDebugBehavior();
          host.Description.Behaviors.Add(debugBehaviour);
        }
        debugBehaviour.IncludeExceptionDetailInFaults = (!UjmwHostConfiguration.ForceHttps);
 
        ServiceBehaviorToApplyDispatchHooks customizedServiceBehaviour;
        if (host.Description.Behaviors.Contains(typeof(ServiceBehaviorToApplyDispatchHooks))) {
          customizedServiceBehaviour = host.Description.Behaviors.Find<ServiceBehaviorToApplyDispatchHooks>();
        }
        else { 
          customizedServiceBehaviour = new ServiceBehaviorToApplyDispatchHooks(inboundSideChannelCfg, outboundSideChannelCfg);
          host.Description.Behaviors.Add(customizedServiceBehaviour);
        }

        ServiceAuthenticationBehavior authBehavoir = null;
        authBehavoir = host.Description.Behaviors.Find<ServiceAuthenticationBehavior>();
        if (authBehavoir == null) {
          authBehavoir = new ServiceAuthenticationBehavior();
          authBehavoir.AuthenticationSchemes = hostAuthenticationSchemes;
          host.Description.Behaviors.Add(authBehavoir);
        }
        else {
          authBehavoir.AuthenticationSchemes = hostAuthenticationSchemes;
        }

        return host;
      }
      catch (Exception ex) {
        if (UjmwHostConfiguration.FactoryExceptionVisitor != null) {
          UjmwHostConfiguration.FactoryExceptionVisitor.Invoke(ex);

          //HACK: wir wissen nicht, ob das gut ist -> WCF soll einfach den service skippen
          return null;

        }
        else {
          throw;
        }
      }
    }

    private static void CollectUrlsFrom(Uri[] concreteAddresses) {

      string applicationVPathWithTrailingSlash = HostingEnvironment.ApplicationVirtualPath ?? "/";
      if (!applicationVPathWithTrailingSlash.EndsWith("/")) {
        applicationVPathWithTrailingSlash += "/";
      }

      foreach (Uri uri in concreteAddresses) {

        string endpointReleativePathWithoutLeadingSlash = uri.AbsolutePath.Substring(applicationVPathWithTrailingSlash.Length).TrimStart('/');
        string applicationBaseUrlWithTrailingSlash;

        //filtert u.a. auch komische TCP-bindings heraus, die UJMW nicht supportet!
        if (uri.Scheme.Equals("http", StringComparison.CurrentCultureIgnoreCase) || uri.Scheme.Equals("https", StringComparison.CurrentCultureIgnoreCase)) {

          if (uri.IsDefaultPort) {
            applicationBaseUrlWithTrailingSlash = $"{uri.Scheme}://{uri.Host}{applicationVPathWithTrailingSlash}";
          }
          else {
            applicationBaseUrlWithTrailingSlash = $"{uri.Scheme}://{uri.Host}:{uri.Port}{applicationVPathWithTrailingSlash}";
          }

          _CollectedApplicationBaseUrls = _CollectedApplicationBaseUrls.Concat(new string[] { applicationBaseUrlWithTrailingSlash }).Distinct().ToArray();
          _CollectedEndpointRelativeUrls = _CollectedEndpointRelativeUrls.Concat(new string[] { endpointReleativePathWithoutLeadingSlash }).Distinct().ToArray();
      
        }

      }

    }

    public static WebHttpBinding GetCustomizedWebHttpBinding() {
      if (UjmwHostConfiguration.ForceHttps) {

        if (_CustomizedWebHttpBindingSecured == null) {

          if (!UjmwHostConfiguration.RequireNtlm) {
            _CustomizedWebHttpBindingSecured = new WebHttpBinding(WebHttpSecurityMode.Transport);
            _CustomizedWebHttpBindingSecured.Security.Transport.ClientCredentialType = HttpClientCredentialType.None;
          }
          else {
            _CustomizedWebHttpBindingSecured = new WebHttpBinding(WebHttpSecurityMode.Transport);
            _CustomizedWebHttpBindingSecured.Security.Transport.ClientCredentialType = HttpClientCredentialType.Ntlm;
          }

          if(UjmwHostConfiguration.HttpBindingCustomizingHook != null) {
            UjmwHostConfiguration.HttpBindingCustomizingHook.Invoke(_CustomizedWebHttpBindingSecured);
          }

        }

        return _CustomizedWebHttpBindingSecured;
      }
      else {

        if (_CustomizedWebHttpBinding == null) {

          if (!UjmwHostConfiguration.RequireNtlm) {
            _CustomizedWebHttpBinding = new WebHttpBinding(WebHttpSecurityMode.None);
            _CustomizedWebHttpBinding.Security.Transport.ClientCredentialType = HttpClientCredentialType.None;
          }
          else {
            _CustomizedWebHttpBinding = new WebHttpBinding(WebHttpSecurityMode.TransportCredentialOnly);
            _CustomizedWebHttpBinding.Security.Transport.ClientCredentialType = HttpClientCredentialType.Ntlm;
          }

          if (UjmwHostConfiguration.HttpBindingCustomizingHook != null) {
            UjmwHostConfiguration.HttpBindingCustomizingHook.Invoke(_CustomizedWebHttpBinding);
          }

        }

        return _CustomizedWebHttpBinding;
      }
    }

    private static ContractDescription CreateCustomizedContractDescription(
      Type contractType, bool addUnderlineToRequest, bool addUnderlineToResponse, Type serviceType = null
    ) {

      ContractDescription defaultContractDescription = null;
      if (serviceType == null) {
        defaultContractDescription = ContractDescription.GetContract(contractType);
      }
      else {
        defaultContractDescription = ContractDescription.GetContract(contractType, serviceType);
      }

      foreach (var od in defaultContractDescription.Operations) {
        if (addUnderlineToRequest) {
          var inMessages = od.Messages.Where((m) => m.Direction == MessageDirection.Input);
          foreach (var im in inMessages) {

            var underlinePropertyDescription = new MessagePartDescription("_", im.Body.WrapperNamespace);
            underlinePropertyDescription.Type = typeof(Dictionary<string,string>);
            underlinePropertyDescription.Multiple = false;
            underlinePropertyDescription.ProtectionLevel = Net.Security.ProtectionLevel.None;
            //underlinePropertyDescription.Index = om.Body.ReturnValue.Index;
            //underlinePropertyDescription.MemberInfo = om.Body.ReturnValue.MemberInfo;
            im.Body.Parts.Add(underlinePropertyDescription);

          }
        }
        var outMessages = od.Messages.Where((m) => m.Direction == MessageDirection.Output);
        foreach (var om in outMessages) {

          if (addUnderlineToResponse) {
            //within the dispatcher we are to late, because were already getting the serialized body,
            //so weve just doing that job in the 'CustomizedJsonFormatter' when serializeing, which
            //allows us to skip an offical registration of that property
          }

          //TODO: fault property?

          if (om.Body != null && om.Body.ReturnValue != null) {
            var customizedReturnValueDescription = new MessagePartDescription("return", om.Body.ReturnValue.Namespace);
            customizedReturnValueDescription.Type = om.Body.ReturnValue.Type;
            customizedReturnValueDescription.ProtectionLevel = om.Body.ReturnValue.ProtectionLevel;
            customizedReturnValueDescription.Index = om.Body.ReturnValue.Index;
            customizedReturnValueDescription.Multiple = om.Body.ReturnValue.Multiple;
            customizedReturnValueDescription.MemberInfo = om.Body.ReturnValue.MemberInfo;
            om.Body.ReturnValue = customizedReturnValueDescription;
          }

        }

      }

      return defaultContractDescription;
    }
 
  }

}
