using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
      Uri primaryUri = baseAddresses[0];

      if (UjmwServiceBehaviour.ForceHttps) {
        primaryUri = new Uri(primaryUri.ToString().Replace("http://", "https://"));
      }

      UjmwServiceBehaviour.ContractSelector.Invoke(serviceImplementationType, primaryUri.ToString(), out Type contractInterface);

      ServiceHost host = new ServiceHost(serviceImplementationType, new Uri[] { primaryUri });

      var behavior = host.Description.Behaviors.Find<ServiceBehaviorAttribute>();
      behavior.InstanceContextMode = InstanceContextMode.Single;
      behavior.ConcurrencyMode = ConcurrencyMode.Multiple;

      var customizedContractDescription = CreateCustomizedContractDescription(contractInterface, serviceImplementationType);

      var endpoint = new ServiceEndpoint(
        customizedContractDescription,
        GetCustomizedWebHttpBinding(),
        new EndpointAddress(primaryUri)
      );
      host.AddServiceEndpoint(endpoint);

      CustomBinding customizedBinding = new CustomBinding(endpoint.Binding);
      WebMessageEncodingBindingElement encodingBindingElement = customizedBinding.Elements.Find<WebMessageEncodingBindingElement>();
      encodingBindingElement.ContentTypeMapper = new CustomizedJsonContentTypeMapper();
      endpoint.Binding = customizedBinding;

      endpoint.Behaviors.Add(new CustomizedWebHttpBehaviourForJson());

      ServiceMetadataBehavior metadataBehaviour;
      if (host.Description.Behaviors.Contains(typeof(ServiceMetadataBehavior))) {
        metadataBehaviour = host.Description.Behaviors.Find<ServiceMetadataBehavior>();
      }
      else {
        metadataBehaviour = new ServiceMetadataBehavior();
        host.Description.Behaviors.Add(metadataBehaviour);
      }

      if (UjmwServiceBehaviour.ForceHttps) {
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
      debugBehaviour.IncludeExceptionDetailInFaults = (!UjmwServiceBehaviour.ForceHttps);

      SideChannelServiceBehavior customizedServiceBehaviour;
      if (host.Description.Behaviors.Contains(typeof(SideChannelServiceBehavior))) {
        customizedServiceBehaviour = host.Description.Behaviors.Find<SideChannelServiceBehavior>();
      }
      else {
        customizedServiceBehaviour = new SideChannelServiceBehavior();
        host.Description.Behaviors.Add(customizedServiceBehaviour);
      }

      return host;
    }

    private static WebHttpBinding _CustomizedWebHttpBindingSecured = null;
    private static WebHttpBinding _CustomizedWebHttpBinding = null;

    private WebHttpBinding GetCustomizedWebHttpBinding() {
      if (UjmwServiceBehaviour.ForceHttps) {

        if (_CustomizedWebHttpBindingSecured == null) {
          _CustomizedWebHttpBindingSecured = new WebHttpBinding(WebHttpSecurityMode.Transport);
          _CustomizedWebHttpBindingSecured.Security.Transport.ClientCredentialType = HttpClientCredentialType.None;
        }

        return _CustomizedWebHttpBindingSecured;
      }
      else {
        if (_CustomizedWebHttpBinding == null) {
          _CustomizedWebHttpBinding = new WebHttpBinding(WebHttpSecurityMode.None);
        }
        return _CustomizedWebHttpBinding;
      }
    }

    private static ContractDescription CreateCustomizedContractDescription(Type contractType, Type serviceType = null) {

      ContractDescription defaultContractDescription = null;
      if (serviceType == null) {
        defaultContractDescription = ContractDescription.GetContract(contractType);
      }
      else {
        defaultContractDescription = ContractDescription.GetContract(contractType, serviceType);
      }

      foreach (var od in defaultContractDescription.Operations) {
        var outMessages = od.Messages.Where((m) => m.Direction == MessageDirection.Output);
        foreach (var om in outMessages) {

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

    internal class SideChannelServiceBehavior : IServiceBehavior, IErrorHandler {

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
              endpoint.DispatchRuntime.MessageInspectors.Add(new DispatchMessageInspector());
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

      #endregion
    }

    internal class CustomFaultBodyWriter : BodyWriter {
      private string _Message;
      public CustomFaultBodyWriter(Exception e) : base(false) {
        _Message = e.Message;
      }

      protected override void OnWriteBodyContents(System.Xml.XmlDictionaryWriter writer) {
        //HACK: WCF PITA required to have an XML ROOT NODE before writing raw - OH MY GOD
        //because of this our standard needs to allow XML-encapsulated messages like
        //<UJMW>{ ... }</UJMW>
        writer.WriteStartElement("UJMW");
        writer.WriteRaw($"{{ \"fault\":\"{_Message}\" }}");
        writer.WriteEndElement();
      }
    }

    internal class DispatchMessageInspector : IDispatchMessageInspector {

      private Dictionary<String, MethodInfo> _MethodInfoCache = new Dictionary<String, MethodInfo>();

      public Object AfterReceiveRequest(ref Message incomingSoapRequest, IClientChannel channel, InstanceContext instanceContext) {

        if (UjmwServiceBehaviour.AuthHeaderEvaluator == null && UjmwServiceBehaviour.RequestSidechannelProcessor == null) {
          return null;
        }

        RemoteEndpointMessageProperty clientEndpoint = OperationContext.Current.IncomingMessageProperties[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;
        string callingMachine = clientEndpoint.Address.ToString();

        string methodName = null;
        string fullCallUrl = null;
        if (incomingSoapRequest.Properties.TryGetValue("HttpOperationName", out object httpOperationName)) {
          methodName = httpOperationName?.ToString();
        }
        if (incomingSoapRequest.Properties.TryGetValue("Via", out object via)) {
          fullCallUrl = via?.ToString();
        }
        if (methodName == null || fullCallUrl == null) {
          return null;
        }

        MethodInfo calledContractMethod;
        lock (_MethodInfoCache) {
          if (!_MethodInfoCache.TryGetValue(fullCallUrl, out calledContractMethod)) {

            //HACK: instead of serviceImplementationType we should evaluate the merhodinfo for the contract!
            Type serviceContractType = instanceContext.Host.Description.ServiceType;

            calledContractMethod = serviceContractType.GetMethod(methodName);
            _MethodInfoCache[fullCallUrl] = calledContractMethod;
          }
        }

        if (UjmwServiceBehaviour.AuthHeaderEvaluator == null) {
          this.ProcessRequestSideChannel(calledContractMethod);
          return null;
        }

        int httpReturnCode = 200;

        string rawAuthHeader = null;
        HttpRequestMessageProperty httpRequest = (HttpRequestMessageProperty)incomingSoapRequest.Properties[HttpRequestMessageProperty.Name];

        if (httpRequest.Headers.AllKeys.Contains("Authorization")) {
          rawAuthHeader = httpRequest.Headers["Authorization"];
        }

        bool continueProcessing = UjmwServiceBehaviour.AuthHeaderEvaluator.Invoke(
          rawAuthHeader, calledContractMethod, callingMachine, ref httpReturnCode
        );

        if (continueProcessing) {
          this.ProcessRequestSideChannel(calledContractMethod);
          return null;
        }
        else {
          if (httpReturnCode == 200) {
            //default, if no specific code has been provided!
            throw new WebFaultException(HttpStatusCode.Forbidden);
          }
          throw new WebFaultException((HttpStatusCode)httpReturnCode);
        }

      }

      private void ProcessRequestSideChannel(MethodInfo calledContractMethod) {
        if (UjmwServiceBehaviour.RequestSidechannelProcessor != null) {

          //TODO: extract request side channel...
          //UjmwServiceBehaviour.RequestSidechannelProcessor.Invoke(calledContractMethod, extractedContainer);

        }
      }

      public void BeforeSendReply(ref Message outgoingReply, Object correlationState) {
        if (UjmwServiceBehaviour.ResponseSidechannelCapturer != null) {

          //UjmwServiceBehaviour.ResponseSidechannelCapturer.Invoke(calledContractMethod, containerToReply);
          //TODO: inject response side channel...

        }
      }

    }

    internal class CustomizedWebHttpBehaviourForJson : WebHttpBehavior {

      public CustomizedWebHttpBehaviourForJson() {
        this.DefaultOutgoingRequestFormat = System.ServiceModel.Web.WebMessageFormat.Json;
        this.DefaultOutgoingResponseFormat = System.ServiceModel.Web.WebMessageFormat.Json;
        this.DefaultBodyStyle = System.ServiceModel.Web.WebMessageBodyStyle.Wrapped;
      }

      protected override IDispatchMessageFormatter GetRequestDispatchFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
        return new CustomizedJsonFormatter(operationDescription, true, endpoint.ListenUri);
      }

      protected override IDispatchMessageFormatter GetReplyDispatchFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
        return new CustomizedJsonFormatter(operationDescription, false, endpoint.ListenUri);
      }

      protected override IClientMessageFormatter GetRequestClientFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
        return new CustomizedJsonFormatter(operationDescription, true, endpoint.ListenUri);
      }

      protected override IClientMessageFormatter GetReplyClientFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
        return new CustomizedJsonFormatter(operationDescription, false, endpoint.ListenUri);
      }

    }

    internal class CustomizedJsonFormatter : IDispatchMessageFormatter, IClientMessageFormatter {

      private OperationDescription _OperationSchema;
      private Dictionary<String, int> _ParameterIndicesPerName;
      private Dictionary<String, object> _ParameterDefaults;
      private MessageDescription _RelevantMessageDesc;
      private Uri _Uri;

      //NOTE: this formatter will be constrcuted ONCE per CONTRACT-METHOD
      //but is recylced over several requests - so we have no performance problem here
      public CustomizedJsonFormatter(OperationDescription operation, bool isRequest, Uri uri) {

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
        
        _ParameterIndicesPerName = new Dictionary<String, int>();
        for (int i = 0; i < _RelevantMessageDesc.Body.Parts.Count; i++) {
          _ParameterIndicesPerName.Add(_RelevantMessageDesc.Body.Parts[i].Name, i);
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
