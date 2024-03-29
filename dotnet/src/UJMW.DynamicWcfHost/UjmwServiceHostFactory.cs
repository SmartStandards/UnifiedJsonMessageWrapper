﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.ServiceModel.Web;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
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

  //-------------------------------------------------------------------------------------------------------------------
  // could also be neccessary
  //-------------------------------------------------------------------------------------------------------------------
  //  <runtime>
  //    <assemblyBinding xmlns = "urn:schemas-microsoft-com:asm.v1" >
  //      <dependentAssembly>
  //        <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
  //        <bindingRedirect oldVersion = "0.0.0.0-14.0.0.0" newVersion="8.0.0.0" />
  //      </dependentAssembly>

  public class UjmwServiceHostFactory : ServiceHostFactory {

    //needs to have an parameterless constructor, because
    //the typename of this class is just written into the Web.config
    public UjmwServiceHostFactory() {
    }

    protected override ServiceHost CreateServiceHost(Type serviceImplementationType, Uri[] baseAddresses) {
      try {

        Uri primaryUri = baseAddresses[0];

        if (UjmwHostConfiguration.ForceHttps) {
          primaryUri = new Uri(primaryUri.ToString().Replace("http://", "https://"));
        }

        UjmwHostConfiguration.ContractSelector.Invoke(serviceImplementationType, primaryUri.ToString(), out Type contractInterface);

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
        host.AddServiceEndpoint(endpoint);


        //https://weblogs.asp.net/scottgu/437027

        if(UjmwHostConfiguration.DiableNtlm) {
          host.Authentication.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
        }
        else {
          host.Authentication.AuthenticationSchemes = AuthenticationSchemes.Ntlm;
        }

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

    private static WebHttpBinding _CustomizedWebHttpBindingSecured = null;
    private static WebHttpBinding _CustomizedWebHttpBinding = null;

    private WebHttpBinding GetCustomizedWebHttpBinding() {
      if (UjmwHostConfiguration.ForceHttps) {

        if (_CustomizedWebHttpBindingSecured == null) {
          if (UjmwHostConfiguration.DiableNtlm) {
            _CustomizedWebHttpBindingSecured = new WebHttpBinding(WebHttpSecurityMode.Transport);
            _CustomizedWebHttpBindingSecured.Security.Transport.ClientCredentialType = HttpClientCredentialType.None;
          }
          else {
            _CustomizedWebHttpBindingSecured = new WebHttpBinding(WebHttpSecurityMode.Transport);
            _CustomizedWebHttpBindingSecured.Security.Transport.ClientCredentialType = HttpClientCredentialType.Ntlm;
          }
        }
        return _CustomizedWebHttpBindingSecured;

      }
      else {

        if (_CustomizedWebHttpBinding == null) {
          if (UjmwHostConfiguration.DiableNtlm) {
            _CustomizedWebHttpBinding = new WebHttpBinding(WebHttpSecurityMode.None);
            _CustomizedWebHttpBinding.Security.Transport.ClientCredentialType = HttpClientCredentialType.None;
          }
          else {
            _CustomizedWebHttpBinding = new WebHttpBinding(WebHttpSecurityMode.TransportCredentialOnly);
            _CustomizedWebHttpBinding.Security.Transport.ClientCredentialType = HttpClientCredentialType.Ntlm;
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

    internal class CustomizedJsonContentTypeMapper : WebContentTypeMapper {

      public override WebContentFormat GetMessageFormatForContentType(string contentType) {
        return WebContentFormat.Raw;
      }
    }



    internal class ServiceBehaviorToApplyDispatchHooks : IServiceBehavior, IErrorHandler {

      private IncommingRequestSideChannelConfiguration _InboundSideChannelCfg;
      private OutgoingResponseSideChannelConfiguration _OutboundSideChannelCfg;

      public ServiceBehaviorToApplyDispatchHooks(IncommingRequestSideChannelConfiguration inboundSideChannelCfg, OutgoingResponseSideChannelConfiguration outboundSideChannelCfg) {
        _InboundSideChannelCfg = inboundSideChannelCfg;
        _OutboundSideChannelCfg = outboundSideChannelCfg;
      }

      public void AddBindingParameters(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase, Collection<ServiceEndpoint> endpoints, BindingParameterCollection bindingParameters) {
      }

      public void ApplyDispatchBehavior(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase) {
        Type contractType = serviceDescription.Endpoints[0].Contract.ContractType;

        if (contractType == null) {
          throw new Exception($"ContractType for Service '{serviceDescription.Name}' was not found!");
        }
        else {
          foreach (ChannelDispatcher dispatcher in serviceHostBase.ChannelDispatchers) {
            foreach (EndpointDispatcher endpoint in dispatcher.Endpoints) {
              endpoint.DispatchRuntime.MessageInspectors.Add(new DispatchMessageInspector(_InboundSideChannelCfg, _OutboundSideChannelCfg));
            }
          }
        }

        //foreach (ChannelDispatcherBase channelDispatcherBase in serviceHostBase.ChannelDispatchers) {
        //  ChannelDispatcher channelDispatcher = channelDispatcherBase as ChannelDispatcher;
        //  if (channelDispatcher != null) {
        //    channelDispatcher.ErrorHandlers.Add(this);
        //  }
        //}

      }

      #region " IErrorHandler "

      public bool HandleError(Exception error) {
        //return (error is FaultException);
        return false;
       // return true;
      }

      public void ProvideFault(Exception error, MessageVersion version, ref Message fault) {
        fault = null;
        return;

        //MessageFault MF = FE.CreateMessageFault();
        //fault = Message.CreateMessage(version, MF, null);

        //BUG: this will return a body content of: 
        // <string xmlns="http://schemas.microsoft.com/2003/10/Serialization/">{ "fault":"error message" }</string>
        //string body = $"{{ \"fault\":\"{error.Message}\" }}";
        //fault = Message.CreateMessage(version, action: null, body: body);

        //BodyWriter bodyWriter = new CustomFaultBodyWriter(error);
        //fault = Message.CreateMessage(version, action: null, bodyWriter);

      }
      public void Validate(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase) {
      }

      //internal sealed class WebHttpErrorHandler : IErrorHandler {
      //  //private static readonly ILog logger = LogManager.GetLogger(typeof(WebHttpErrorHandler));

      //  public void ProvideFault(Exception error, MessageVersion version, ref Message fault) {
      //    var exception = new FaultException("Web Server error encountered. All details have been logged.");
      //    var messageFault = exception.CreateMessageFault();

      //    fault = Message.CreateMessage(version, messageFault, exception.Action);
      //  }

      //  public bool HandleError(Exception error) {
      //    logger.Error(string.Format("An error has occurred in the Web service {0}", error));

      //    return !(error is FaultException);
      //  }
      //}

      #endregion
    }

    internal class CustomFaultBodyWriter : BodyWriter {
      private string _Message;
      public CustomFaultBodyWriter(Exception e) : base(false) {
        _Message = e.Message;
      }

      protected override void OnWriteBodyContents(System.Xml.XmlDictionaryWriter writer) {
        //HACK: WCF PITA required (at elast for faultbody) to have an XML ROOT NODE before
        //writing raw - OH MY GOD!!!
        writer.WriteStartElement("UJMW");
        writer.WriteRaw($"{{ \"fault\":\"{_Message}\" }}");
        writer.WriteEndElement();
        //because of this our standard needs to allow XML-encapsulated messages like
        //<UJMW>{ ... }</UJMW>
      }
    }

    internal class DispatchMessageInspector : IDispatchMessageInspector {

      private IncommingRequestSideChannelConfiguration _InboundSideChannelCfg;
      private OutgoingResponseSideChannelConfiguration _OutboundSideChannelCfg;

      public DispatchMessageInspector(IncommingRequestSideChannelConfiguration inboundSideChannelCfg, OutgoingResponseSideChannelConfiguration outboundSideChannelCfg) {
        _InboundSideChannelCfg = inboundSideChannelCfg;
        _OutboundSideChannelCfg = outboundSideChannelCfg;
      }

      private Dictionary<String, MethodInfo> _MethodInfoCache = new Dictionary<String, MethodInfo>();

      public Object AfterReceiveRequest(ref Message incomingWcfMessage, IClientChannel channel, InstanceContext instanceContext) {
       
        //if (incomingWcfMessage.State == MessageState.Copied) {
        //  //the copy was implicitely created by us, in order to get the possibility to read the body
        //  //but it also triggers out own interceptor again - WTF!!!!
        //  return null;
        //}

        //no hook neccessarry
        if (UjmwHostConfiguration.AuthHeaderEvaluator == null && _InboundSideChannelCfg.AcceptedChannels.Length == 0) {
          return null;
        }

        RemoteEndpointMessageProperty clientEndpoint = OperationContext.Current.IncomingMessageProperties[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;
        string callingMachine = clientEndpoint.Address.ToString();

        string methodName = null;
        string fullCallUrl = null;
        if (incomingWcfMessage.Properties.TryGetValue("HttpOperationName", out object httpOperationName)) {
          methodName = httpOperationName?.ToString();
        }
        if (incomingWcfMessage.Properties.TryGetValue("Via", out object via)) {
          fullCallUrl = via?.ToString();
        }
        if (methodName == null || fullCallUrl == null) {
          return null;
        }

        MethodInfo calledContractMethod;
        lock (_MethodInfoCache) {
          if (!_MethodInfoCache.TryGetValue(fullCallUrl, out calledContractMethod)) {

            //HACK: instead of serviceImplementationType we would like to get the CONTRACT!
            Type serviceContractType = instanceContext.Host.Description.ServiceType;

            calledContractMethod = serviceContractType.GetMethod(methodName);

            _MethodInfoCache[fullCallUrl] = calledContractMethod;
          }
        }
        HttpRequestMessageProperty httpRequest = (HttpRequestMessageProperty)incomingWcfMessage.Properties[HttpRequestMessageProperty.Name];

        int httpReturnCode = 200;
        bool authFailed = false;

        if (UjmwHostConfiguration.AuthHeaderEvaluator != null) {
  
          string rawAuthHeader = null;

          if (httpRequest.Headers.AllKeys.Contains("Authorization")) {
            rawAuthHeader = httpRequest.Headers["Authorization"];
          }

          bool authSuccess = UjmwHostConfiguration.AuthHeaderEvaluator.Invoke(
            rawAuthHeader, calledContractMethod, callingMachine, ref httpReturnCode
          );

          if (!authSuccess) {

            //TODO: logging hook!!!
            Trace.TraceWarning("Rejected incomming request because AuthHeaderEvaluator returned false!");

            if (httpReturnCode == 200) {
              //default, if no specific code has been provided!
              throw new WebFaultException(HttpStatusCode.Forbidden);
            }
            throw new WebFaultException((HttpStatusCode)httpReturnCode);
          }

        }

        ///// RESTORE INCOMMING SIDECHANNEL /////

        bool sideChannelReceived = false;
        IDictionary<string, string> sideChannelContent = null;
        foreach (string acceptedChannel in _InboundSideChannelCfg.AcceptedChannels) {

          if (acceptedChannel == "_") {
            //we need to introspect the message...
            string rawMessage = this.GetBodyFromWcfMessage(ref incomingWcfMessage);
            var sr = new StringReader(rawMessage);
            var rdr = new JsonTextReader(sr);
            rdr.Read();
            while (rdr.TokenType != JsonToken.None) {
              if (rdr.TokenType == JsonToken.PropertyName) {
                if (rdr.Value.ToString() == "_") {
                  rdr.Read();
                  var serializer = new Newtonsoft.Json.JsonSerializer();
                  sideChannelContent = serializer.Deserialize<Dictionary<string, string>>(rdr);
                  sideChannelReceived = true;
                  break;
                }
                else {
                  rdr.Skip();
                }
              }
              rdr.Read();
            }
            if (sideChannelReceived) {
              _InboundSideChannelCfg.ProcessingMethod.Invoke(calledContractMethod, sideChannelContent);
              break;
            }
          }
          else { //lets look into the http header
            if (httpRequest.Headers.AllKeys.Contains(acceptedChannel)) {
              string rawSideChannelContent = httpRequest.Headers[acceptedChannel];
              var serializer = new Newtonsoft.Json.JsonSerializer();
              sideChannelContent = JsonConvert.DeserializeObject<Dictionary<string, string>>(rawSideChannelContent);
              sideChannelReceived= true;
              _InboundSideChannelCfg.ProcessingMethod.Invoke(calledContractMethod, sideChannelContent);
              break;
            }
          }
        }

        if (!sideChannelReceived && _InboundSideChannelCfg.AcceptedChannels.Length > 0) {
          if (_InboundSideChannelCfg.SkipAllowed) {
            //if the whole getter is null, then (and only in this case) it will be a 'silent skip'
            if (_InboundSideChannelCfg.DefaultsGetterOnSkip != null) {
              sideChannelContent = new Dictionary<string, string>();
              _InboundSideChannelCfg.DefaultsGetterOnSkip.Invoke(ref sideChannelContent);
              //also null (when the DefaultsGetterOnSkip sets the ref handle to null) can be
              //passed to the processing method...
              _InboundSideChannelCfg.ProcessingMethod.Invoke(calledContractMethod, sideChannelContent);
            }
          }
          else {
            //TODO: logging hook!!! 
            Trace.TraceWarning("Rejected incomming request because of missing side channel");
            throw new WebFaultException(HttpStatusCode.BadRequest);
          }
         
        }

        ///// (end) RESTORE INCOMMING SIDECHANNEL /////
        
        return calledContractMethod;// << this handle will be passed to 'BeforeSendReply' as 'correlationState'

      }


      public void BeforeSendReply(ref Message outgoingWcfMessage, Object correlationState) {

        MethodInfo calledContractMethod = null;
        if(!(correlationState is MethodInfo)) {

          //TODO: hier könnten wir auch im fehlerfall die fault-message FAKE!!!

          //string msg = this.GetBodyFromWcfMessage(ref outgoingWcfMessage);
          //outgoingWcfMessage = Message.CreateMessage(outgoingWcfMessage.Version, "FAULT", "invald request");
          //outgoingWcfMessage = new RawFaultMessage(Message.CreateMessage(outgoingWcfMessage.Version, "ccc", "ffff"),"KAPUTT");

          return;
        }
        else {
          calledContractMethod = (MethodInfo)correlationState;
        }

        if (_OutboundSideChannelCfg.ChannelsToProvide.Length > 0) {

          ///// CAPTURE OUTGOING BACKCHANNEL /////

          //prepare some ugly WCF hacking
          HttpResponseMessageProperty outgoingHttpResponse = null;
          if (outgoingWcfMessage.Properties.ContainsKey(HttpResponseMessageProperty.Name)) {
            outgoingHttpResponse = (HttpResponseMessageProperty) outgoingWcfMessage.Properties[HttpResponseMessageProperty.Name];
          }
          else {
            outgoingHttpResponse = new HttpResponseMessageProperty();
            outgoingWcfMessage.Properties.Add(HttpResponseMessageProperty.Name, outgoingHttpResponse);
          }

          //COLLECT THE DATA
          string serializedSnapshot = null;
          foreach (string channelName in _OutboundSideChannelCfg.ChannelsToProvide) {
            if(channelName == "_") {
              //NOTE: for that property we are to late because the body has already been serialized
              //therfore we háve already captured the data in the 'CustomizedJsonFormatter'
            }
            else {
              if(serializedSnapshot == null) { //on-demand, but bufferred...
                var snapshotContainer = new Dictionary<string, string>();
               _OutboundSideChannelCfg.CaptureMethod.Invoke(calledContractMethod, snapshotContainer);
                serializedSnapshot = JsonConvert.SerializeObject(snapshotContainer);
              }
              outgoingHttpResponse.Headers.Add(channelName, serializedSnapshot);
            }
          }

          ///// (end) CAPTURE OUTGOING BACKCHANNEL /////
          
        }
      }

      private string GetBodyFromWcfMessage(ref Message message) {
        //PFUI!!!
        Byte[] bodyBytes;
        MessageBuffer buffer = message.CreateBufferedCopy(Int32.MaxValue);
        //vvv nötig, weil jede message nur 1x gelsen werden kann
        message = buffer.CreateMessage();
        bodyBytes = buffer.CreateMessage().GetBody<byte[]>();
        return System.Text.Encoding.UTF8.GetString(bodyBytes);
      }

    }

    //https://stackoverflow.com/questions/62474354/wcf-message-formatter-not-formatting-fault-message
    internal class RawFaultMessage : Message {

      private Message _RelatedMessage;
      private string _FaultMessage;

      public RawFaultMessage(Message relatedMessage, string faultMessage) {
        _RelatedMessage = relatedMessage;
        _FaultMessage = faultMessage;
      }

      public override bool IsFault {
        get {
          return false;
        }
      }

      public override MessageHeaders Headers {
        get {
          return _RelatedMessage.Headers;
        }
      }

      public override MessageProperties Properties {
        get {
          return _RelatedMessage.Properties;
        }
      }

      public override MessageVersion Version {
        get {
          return _RelatedMessage.Version;
        }
      }

      protected override void OnWriteBodyContents(XmlDictionaryWriter writer) {
        writer.WriteRaw(_FaultMessage);
      }

    }

    internal class CustomizedWebHttpBehaviourForJson : WebHttpBehavior {

      private bool _RequestWrapperContainsUnderline;
      private OutgoingResponseSideChannelConfiguration _OutgoingResponseSideChannelConfig;

      public CustomizedWebHttpBehaviourForJson(bool requestWrapperContainsUnderline, OutgoingResponseSideChannelConfiguration outgoingResponseSideChannelConfig) {
        _RequestWrapperContainsUnderline = requestWrapperContainsUnderline;
        _OutgoingResponseSideChannelConfig = outgoingResponseSideChannelConfig;

        this.DefaultOutgoingRequestFormat = System.ServiceModel.Web.WebMessageFormat.Json;
        this.DefaultOutgoingResponseFormat = System.ServiceModel.Web.WebMessageFormat.Json;
        this.DefaultBodyStyle = System.ServiceModel.Web.WebMessageBodyStyle.Wrapped;

      }

      protected override IDispatchMessageFormatter GetRequestDispatchFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
        return new CustomizedJsonFormatter(
          operationDescription, true, endpoint.ListenUri,
          _RequestWrapperContainsUnderline, _OutgoingResponseSideChannelConfig
        );
      }

      protected override IDispatchMessageFormatter GetReplyDispatchFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
        return new CustomizedJsonFormatter(
          operationDescription, false, endpoint.ListenUri,
          _RequestWrapperContainsUnderline, _OutgoingResponseSideChannelConfig
        );
      }

      protected override IClientMessageFormatter GetRequestClientFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
        return new CustomizedJsonFormatter(
          operationDescription, true, endpoint.ListenUri,
          _RequestWrapperContainsUnderline, _OutgoingResponseSideChannelConfig
        );
      }

      protected override IClientMessageFormatter GetReplyClientFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
        return new CustomizedJsonFormatter(
          operationDescription, false, endpoint.ListenUri,
          _RequestWrapperContainsUnderline, _OutgoingResponseSideChannelConfig
        );
      }

      protected override void AddServerErrorHandlers(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher) {
        base.AddServerErrorHandlers(endpoint, endpointDispatcher);

        //TODO: TEST THIS ERROR HOOK
        //https://stackoverflow.com/questions/23212705/wcf-how-to-handle-errors-globally
        //endpointDispatcher.DispatchRuntime.ChannelDispatcher.ErrorHandlers.Add(new WebHttpErrorHandler());

        //IErrorHandler errorHandler = new CustomErrorHandler();
        //foreach (var channelDispatcher in serviceHostBase.ChannelDispatchers) {
        //  channelDispatcher.ErrorHandlers.Add(errorHandler);
        //}

      }

    }

    //PROBLEM: customized IDispatchMessageFormatters are not used for fault-messages - why????
    //https://stackoverflow.com/questions/62474354/wcf-message-formatter-not-formatting-fault-message
    internal class CustomizedJsonFormatter : IDispatchMessageFormatter, IClientMessageFormatter {

      private bool _RequestWrapperContainsUnderline;
      private OutgoingResponseSideChannelConfiguration _OutgoingResponseSideChannelConfig;
      private OperationDescription _OperationSchema;
      private Dictionary<String, int> _ParameterIndicesPerName;
      private Dictionary<String, object> _ParameterDefaults;
      private MessageDescription _RelevantMessageDesc;
      private Uri _Uri;

      //NOTE: this formatter will be constrcuted ONCE per CONTRACT-METHOD
      //but is recylced over several requests - so we have no performance problem here
      public CustomizedJsonFormatter(
        OperationDescription operation, bool isRequest, Uri uri,
        bool requestWrapperContainsUnderline, OutgoingResponseSideChannelConfiguration outgoingResponseSideChannelConfig
      ) {

        _RequestWrapperContainsUnderline = requestWrapperContainsUnderline;
        _OutgoingResponseSideChannelConfig = outgoingResponseSideChannelConfig;

        _OperationSchema = operation;
        _Uri = new Uri(uri.ToString() + "/" + operation.Name);

        if (isRequest) {
          _RelevantMessageDesc = operation.Messages.Where((m) => m.Direction == MessageDirection.Input).First();
        }
        else {
          _RelevantMessageDesc = operation.Messages.Where((m) => m.Direction == MessageDirection.Output).First();
        }

        //we need to prefetch the default-values for OPTIONAL parameters
        _ParameterDefaults = new Dictionary<String, object>();
        foreach (var param in operation.SyncMethod.GetParameters()) {
          if(param.IsOptional && param.HasDefaultValue) {
            _ParameterDefaults.Add(param.Name, param.DefaultValue);
          }
          else {
            _ParameterDefaults.Add(param.Name, null);
          }
        }

        bool containsUnderline = (isRequest && requestWrapperContainsUnderline) || (!isRequest && _OutgoingResponseSideChannelConfig.UnderlinePropertyIsProvided);

        _ParameterIndicesPerName = new Dictionary<String, int>();
        int indexWithoutUnderline = 0;
        for (int i = 0; i < _RelevantMessageDesc.Body.Parts.Count; i++) {
          string paramName = _RelevantMessageDesc.Body.Parts[i].Name;
          if(paramName != "_" || !containsUnderline) {
            _ParameterIndicesPerName.Add(paramName, indexWithoutUnderline);
            indexWithoutUnderline++;
          }
        }

      }

      //1. Client packt REQUEST ein 
      public Message SerializeRequest(MessageVersion messageVersion, Object[] parameters) {
        return this.SerializeOutgoingMessage(messageVersion, parameters, null, false);
      }

      //2. Server packt REQUEST aus
      public void DeserializeRequest(Message message, Object[] parameters) {
        this.DeserializeIncommingMessage(message, parameters, false);
      }

      //3. Server packt RESPONSE ein
      public Message SerializeReply(MessageVersion messageVersion, Object[] parameters, Object result) {
        return this.SerializeOutgoingMessage(messageVersion, parameters, result, true);
      }

      //4. Client packt RESPONSE aus
      public Object DeserializeReply(Message message, Object[] parameters) {
        return this.DeserializeIncommingMessage(message, parameters, true);
      }

      public Message SerializeOutgoingMessage(MessageVersion messageVersion, Object[] parameters, Object result, bool isReply) {
        Byte[] body;
        var serializer = new Newtonsoft.Json.JsonSerializer();

        using (var ms = new MemoryStream()) {
          using (var sw = new StreamWriter(ms, Encoding.UTF8)) {
            using (Newtonsoft.Json.JsonWriter writer = new Newtonsoft.Json.JsonTextWriter(sw)) {
              //'writer.Formatting = Newtonsoft.Json.Formatting.Indented;

              writer.WriteStartObject();

              if (_OutgoingResponseSideChannelConfig.UnderlinePropertyIsProvided) {
                MethodInfo calledContractMethod = _OperationSchema.SyncMethod;
                var snapshotContainer = new Dictionary<string, string>();
                _OutgoingResponseSideChannelConfig.CaptureMethod.Invoke(calledContractMethod, snapshotContainer);
                writer.WritePropertyName("_");
                serializer.Serialize(writer, snapshotContainer);
              }

              foreach (var p in _RelevantMessageDesc.Body.Parts.OrderBy((prt) => prt.Index)) {
                String byRefPropName = p.Name;
                Object byRefValue = parameters[p.Index];
                if (Char.IsUpper(byRefPropName[0])) {
                  byRefPropName = Char.ToLower(byRefPropName[0]) + byRefPropName.Substring(1);
                }
                writer.WritePropertyName(byRefPropName);
                serializer.Serialize(writer, byRefValue);
              }

              if (_RelevantMessageDesc.Body.ReturnValue != null &&
                _RelevantMessageDesc.Body.ReturnValue.Type != typeof(void) &&
                !String.IsNullOrWhiteSpace(_RelevantMessageDesc.Body.ReturnValue.Name)) {
                //should be "result" as customized on another place
                writer.WritePropertyName(_RelevantMessageDesc.Body.ReturnValue.Name);
                serializer.Serialize(writer, result);
              }

              writer.WriteEndObject();

              sw.Flush();
              body = ms.ToArray();

            }
          }
        }

        Message replyMessage = Message.CreateMessage(messageVersion, _OperationSchema.Messages[1].Action, new RawBodyWriter(body));
        replyMessage.Properties.Add(WebBodyFormatMessageProperty.Name, new WebBodyFormatMessageProperty(WebContentFormat.Raw));

        var respProp = new HttpResponseMessageProperty();
        respProp.Headers[HttpResponseHeader.ContentType] = "application/json";
        replyMessage.Properties.Add(HttpResponseMessageProperty.Name, respProp);

        replyMessage.Headers.To = _Uri;
        replyMessage.Properties.Via = _Uri;

        return replyMessage;
      }

      public Object DeserializeIncommingMessage(Message message, Object[] parameters, bool isReply) {

        Object bodyFormatProperty = null;

        if (!message.Properties.TryGetValue(WebBodyFormatMessageProperty.Name, out bodyFormatProperty)) {
          //occours for example on a incomming http-GET request (without a body)
          return null;
        }

        if (((WebBodyFormatMessageProperty)bodyFormatProperty).Format != WebContentFormat.Raw) {
          throw new InvalidOperationException("Incoming messages must have a body format of Raw. Is a ContentTypeMapper set on the WebHttpBinding?");
        }

        Type returnValueType = null;
        Object returnValue = null;
        String returnValueName = null;

        if (_RelevantMessageDesc.Body.ReturnValue != null && !String.IsNullOrWhiteSpace(_RelevantMessageDesc.Body.ReturnValue.Name)) {
          returnValueName = _RelevantMessageDesc.Body.ReturnValue.Name;
          returnValueType = _RelevantMessageDesc.Body.ReturnValue.Type;
        }

        var bodyReader = message.GetReaderAtBodyContents();
        bodyReader.ReadStartElement("Binary");

        lock (_ParameterDefaults) {
          int i = 0; //init the default-values for OPTIONAL parameters
          foreach (var kvp in _ParameterDefaults) {
            parameters[i] = kvp.Value;
            i++;
          }
        } 

        Byte[] rawBody = bodyReader.ReadContentAsBase64();
        var ms = new MemoryStream(rawBody);
        var sr = new StreamReader(ms);
        var serializer = new Newtonsoft.Json.JsonSerializer();

        Newtonsoft.Json.JsonReader reader = new Newtonsoft.Json.JsonTextReader(sr);
        reader.Read();
        if (reader.TokenType != Newtonsoft.Json.JsonToken.StartObject) {
          throw new InvalidOperationException("Input needs to be wrapped in an object");
        }

        reader.Read();

        lock (_ParameterIndicesPerName) {
          while (reader.TokenType == Newtonsoft.Json.JsonToken.PropertyName) {
            String parameterName = reader.Value?.ToString();
            reader.Read();
            if (!string.IsNullOrWhiteSpace(returnValueName) && parameterName.Equals(returnValueName, StringComparison.InvariantCultureIgnoreCase)) {
              returnValue = serializer.Deserialize(reader, returnValueType);
            }
            else if (_ParameterIndicesPerName.ContainsKey(parameterName)) {
              int parameterIndex = _ParameterIndicesPerName[parameterName];
              parameters[parameterIndex] = serializer.Deserialize(reader, _OperationSchema.Messages[0].Body.Parts[parameterIndex].Type);
            }
            else {
              reader.Skip();
            }
            reader.Read();
          }
        }

        reader.Close();
        sr.Close();
        ms.Close();

        return returnValue;
      }

      private class RawBodyWriter : BodyWriter {

        private Byte[] _Content;

        public RawBodyWriter(Byte[] content) : base(true) {
          _Content = content;
        }

        protected override void OnWriteBodyContents(XmlDictionaryWriter writer) {
          writer.WriteStartElement("Binary");
          writer.WriteBase64(_Content, 0, _Content.Length);
          writer.WriteEndElement();
        }

      }

    }

  }

}
