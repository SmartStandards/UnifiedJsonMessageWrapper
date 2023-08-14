using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Message = System.ServiceModel.Channels.Message;
using ServiceDescription = System.ServiceModel.Description.ServiceDescription;

namespace System.Web.UJMW {

  //  <system.serviceModel>
  //    <serviceHostingEnvironment aspNetCompatibilityEnabled="false" multipleSiteBindingsEnabled="true" >
  //      <serviceActivations>
  //        <add relativeAddress="v1/the-url.svc" service="TpeTpeTpe, AssAssAss"
  //             factory="System.Web.UJMW.UjmwServiceHostFactory, UJMW.DynamicWcfHost" />

  public class UjmwServiceHostFactory : ServiceHostFactory {

    //needs to have an parameterless constructor, because
    //the typename of this class is just written into the Web.config
    public UjmwServiceHostFactory() {
    }

    public delegate bool ServiceContractInterfaceSelector(
      Type serviceImplementationType,
      string url,
      out Type serviceContractInterfaceType
    );

    public static bool ForceHttps { get; set; } = false;
    public static ServiceContractInterfaceSelector ContractSelector { get; set; } = (
      (Type serviceImplementationType, string url, out Type serviceContractInterfaceType) => {

        Type[] contractInterfaces = serviceImplementationType.GetInterfaces().Where(
          (i) => i.GetCustomAttributes(true).Where((a) => a.GetType() == typeof(System.ServiceModel.ServiceContractAttribute)).Any()
        ).ToArray();

        if (contractInterfaces.Length == 0) {
          serviceContractInterfaceType = serviceImplementationType;
          return false;
        }

        if (contractInterfaces.Length > 1) {
          string[] urlTokens = url.Split('/');
          string versionFromUrl = null;
          for (int i = urlTokens.Length - 1; i > 0; i--) {
            if (Regex.IsMatch(urlTokens[i], "^([vV][0-9]{1,})$")) {
              versionFromUrl = urlTokens[i].ToLower();
              break;
            }
          }
          if (versionFromUrl != null) {
            var versionMatchingInterface = contractInterfaces.Where((i) => ("." + i.FullName.ToLower() + ".").Contains(versionFromUrl)).FirstOrDefault();
            if (versionMatchingInterface != null) {
              serviceContractInterfaceType = versionMatchingInterface;
              return true;
            }
          }
        }

        serviceContractInterfaceType = contractInterfaces.First();
        return true;
      }
    );

    protected override ServiceHost CreateServiceHost(Type serviceImplementationType, Uri[] baseAddresses) {
      Uri primaryUri = baseAddresses[0];

      if (ForceHttps) {
        primaryUri = new Uri(primaryUri.ToString().Replace("http://", "https://"));
      }

      ContractSelector.Invoke(serviceImplementationType, primaryUri.ToString(), out Type contractInterface);

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

      if (ForceHttps) {
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
      debugBehaviour.IncludeExceptionDetailInFaults = (!ForceHttps);

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
      if (ForceHttps) {

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
        var outMessages = od.Messages.Where((m)=> m.Direction == MessageDirection.Output);
        foreach (var om in outMessages) {

          if(om.Body != null && om.Body.ReturnValue != null) {

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

  internal class CustomizedJsonContentTypeMapper : WebContentTypeMapper {

    public override WebContentFormat GetMessageFormatForContentType(string contentType) {
      return WebContentFormat.Raw;
    }
  }

  internal class SideChannelServiceBehavior : IServiceBehavior {

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
            endpoint.DispatchRuntime.MessageInspectors.Add(new SideChannelDispatchMessageInspector());
          }
        }
      }
    }

    public void Validate(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase) {
    }

  }

  internal class SideChannelDispatchMessageInspector : IDispatchMessageInspector {

    public void BeforeSendReply(ref Message outgoingReply, Object correlationState) {

      //TODO: SEITENKANAL

    }

    public Object AfterReceiveRequest(ref Message incomingSoapRequest, IClientChannel  channel, InstanceContext instanceContext) {

      //TODO: SEITENKANAL

      return null;
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
    private MessageDescription _RelevantMessageDesc;
    private Uri _Uri;

    public CustomizedJsonFormatter(OperationDescription operation, bool isRequest, Uri uri) {

      _OperationSchema = operation;
      _Uri = new Uri(uri.ToString() + "/" + operation.Name);

      if (isRequest) {
        _RelevantMessageDesc = operation.Messages.Where((m) => m.Direction == MessageDirection.Input).First();
      }
      else {
        _RelevantMessageDesc = operation.Messages.Where((m) => m.Direction == MessageDirection.Output).First();
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

      if(!message.Properties.TryGetValue(WebBodyFormatMessageProperty.Name, out bodyFormatProperty)) {
        //occours for example on a incomming http-GET request (without a body)
        return null;
      }

      if (((WebBodyFormatMessageProperty)bodyFormatProperty).Format != WebContentFormat.Raw) {
        throw new InvalidOperationException("Incoming messages must have a body format of Raw. Is a ContentTypeMapper set on the WebHttpBinding?");
      }

      Type returnValueType = null;
      Object returnValue = null;
      String returnValueName = null;
  
      if(_RelevantMessageDesc.Body.ReturnValue != null && !String.IsNullOrWhiteSpace(_RelevantMessageDesc.Body.ReturnValue.Name)) {
        returnValueName = _RelevantMessageDesc.Body.ReturnValue.Name;
        returnValueType = _RelevantMessageDesc.Body.ReturnValue.Type;
      }

      var bodyReader = message.GetReaderAtBodyContents();
      bodyReader.ReadStartElement("Binary");

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

      while (reader.TokenType == Newtonsoft.Json.JsonToken.PropertyName) {
        String parameterName = reader.Value?.ToString();
        reader.Read();
        if(!string.IsNullOrWhiteSpace(returnValueName) && parameterName.Equals(returnValueName, StringComparison.InvariantCultureIgnoreCase)) {
          returnValue = serializer.Deserialize(reader, returnValueType);
        }
        else if (_ParameterIndicesPerName.ContainsKey(parameterName)) {
          int parameterIndex = _ParameterIndicesPerName[parameterName];
          parameters[parameterIndex] = serializer.Deserialize(reader, _OperationSchema.Messages[0].Body.Parts[parameterIndex].Type);
        }
      else{ 
          reader.Skip();
      }
        reader.Read();
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