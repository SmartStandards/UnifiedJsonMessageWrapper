using Logging.SmartStandards;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.Text;
using System.Threading;
using System.Xml;

using Message = System.ServiceModel.Channels.Message;

namespace System.Web.UJMW {

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
        if (!param.IsOut) {
          if (param.IsOptional && param.HasDefaultValue) {
            _ParameterDefaults.Add(param.Name, param.DefaultValue);
          }
          else {
            _ParameterDefaults.Add(param.Name, null);
          }
        }
      }

      bool containsUnderline = (isRequest && requestWrapperContainsUnderline) || (!isRequest && _OutgoingResponseSideChannelConfig.UnderlinePropertyIsProvided);

      _ParameterIndicesPerName = new Dictionary<String, int>();
      int indexWithoutUnderline = 0;
      for (int i = 0; i < _RelevantMessageDesc.Body.Parts.Count; i++) {
        string paramName = _RelevantMessageDesc.Body.Parts[i].Name;
        if (paramName != "_" || !containsUnderline) {
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

    private static Newtonsoft.Json.JsonSerializer serializer = new Newtonsoft.Json.JsonSerializer() {
      ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    public Message SerializeOutgoingMessage(MessageVersion messageVersion, Object[] parameters, Object result, bool isReply) {
      Byte[] body;

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

            if (!string.IsNullOrWhiteSpace(HookedOperationInvoker.CatchedExeptionFromCurrentOperation.Value)) {
              writer.WritePropertyName("fault");
              serializer.Serialize(writer, HookedOperationInvoker.CatchedExeptionFromCurrentOperation.Value);
            }
            else {

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
            Type targetTypeToDeserialize = _OperationSchema.Messages[0].Body.Parts[parameterIndex].Type;
            try {
              parameters[parameterIndex] = serializer.Deserialize(reader, targetTypeToDeserialize);
            }
            catch {
              parameters[parameterIndex] = null;
            }
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

    public class OperationBehaviorWhenDispatching : IOperationBehavior {

      private Type _ContractType;

      public OperationBehaviorWhenDispatching(Type contractType) {
        _ContractType = contractType;
      }

      public void AddBindingParameters(OperationDescription operationDescription, BindingParameterCollection bindingParameters) {
      }

      public void ApplyClientBehavior(OperationDescription operationDescription, ClientOperation clientOperation) {
      }

      public void ApplyDispatchBehavior(OperationDescription operationDescription, DispatchOperation dispatchOperation) {
        //ACHTUNG: operationDescription.DeclaringContract.ContractType IST FALSCH!!! das ist bei abgeleiteten contract die baisklasse!
        dispatchOperation.Invoker = new HookedOperationInvoker(dispatchOperation.Invoker, dispatchOperation, _ContractType);
      }

      public void Validate(OperationDescription operationDescription) {
      }

    }

    public class HookedOperationInvoker : IOperationInvoker {

      public static AsyncLocal<string> CatchedExeptionFromCurrentOperation = new AsyncLocal<string>();

      private readonly IOperationInvoker _BaseInvoker;
      private readonly string _OperationName;
      private readonly string _ControllerName;
      private readonly Type _ContractType;
      private readonly MethodInfo _ContractMethod;

      public HookedOperationInvoker(IOperationInvoker baseInvoker, DispatchOperation operation, Type contractType) {
        _BaseInvoker = baseInvoker;
        _ContractType = contractType;
        _OperationName = operation.Name;
        _ControllerName = operation.Parent.Type == null ? "[None]" : operation.Parent.Type.FullName;
        DispatchMessageInspector.TryGetContractMethod(_ContractType, operation.Name, out _ContractMethod);
        //TODO: prüfen:  operation.CallContextInitializers << sehr interessant!
      }

      public bool IsSynchronous => _BaseInvoker.IsSynchronous;

      public object[] AllocateInputs() {
        return _BaseInvoker.AllocateInputs();
      }

      public object Invoke(object instance, object[] inputs, out object[] outputs) {
        //if (UjmwHostConfiguration.LoggingHook != null) {
        //  UjmwHostConfiguration.LoggingHook.Invoke(0, $"Incomming call to UJMW Operation '{_ControllerName}.{_OperationName}'");
        //}
        DevToTraceLogger.LogTrace(0, $"Invoking UJMW call to UJMW Operation '{_ContractMethod.Name}'");

        try {
          if (UjmwHostConfiguration.ArgumentPreEvaluator != null) {
            try {
              UjmwHostConfiguration.ArgumentPreEvaluator.Invoke(_ContractType, _ContractMethod, inputs);
            }
            catch (TargetInvocationException ex) {
              throw new ApplicationException($"ArgumentPreEvaluator for '{_ContractMethod.Name}' has thrown an Exception: " + ex.InnerException.Message, ex.InnerException);
            }
            catch (Exception ex) {
              throw new ApplicationException($"ArgumentPreEvaluator for '{_ContractMethod.Name}' has thrown an Exception: " + ex.Message, ex);
            }
          }
          try {
            return _BaseInvoker.Invoke(instance, inputs, out outputs);
          }
          catch (TargetInvocationException ex) {
            throw new ApplicationException($"BL-Method '{_ContractMethod.Name}' has thrown an Exception: " + ex.InnerException.Message, ex.InnerException);
          }
          catch (Exception ex) {
            throw new ApplicationException($"BL-Method '{_ContractMethod.Name}' has thrown an Exception: " + ex.Message, ex);
          }
        }
        catch (Exception ex) {
          DevToTraceLogger.LogError(0, ex);
          //UjmwHostConfiguration.LoggingHook.Invoke(4, $"UJMW Operation has thrown Exception: {ex.Message}");
          if (UjmwHostConfiguration.HideExeptionMessageInFaultProperty) {
            CatchedExeptionFromCurrentOperation.Value = "BL-Exception";
          }
          else {
            CatchedExeptionFromCurrentOperation.Value = ex.Message;
          }
          outputs = new object[0];
          return null;
        }
      }

      public IAsyncResult InvokeBegin(object instance, object[] inputs, AsyncCallback callback, object state) {
        return _BaseInvoker.InvokeBegin(instance, inputs, callback, state);
      }

      public object InvokeEnd(object instance, out object[] outputs, IAsyncResult result) {
        return _BaseInvoker.InvokeEnd(instance, out outputs, result);
      }

    }

  }

}
