using Logging.SmartStandards;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;
using System.ServiceModel.Web;

using static System.Web.UJMW.CustomizedJsonFormatter;

using Message = System.ServiceModel.Channels.Message;

namespace System.Web.UJMW {

  internal class DispatchMessageInspector : IDispatchMessageInspector {

    private IncommingRequestSideChannelConfiguration _InboundSideChannelCfg;
    private OutgoingResponseSideChannelConfiguration _OutboundSideChannelCfg;

    public DispatchMessageInspector(IncommingRequestSideChannelConfiguration inboundSideChannelCfg, OutgoingResponseSideChannelConfiguration outboundSideChannelCfg) {
      _InboundSideChannelCfg = inboundSideChannelCfg;
      _OutboundSideChannelCfg = outboundSideChannelCfg;
    }

    private class CorellationStateContainer {
      public string HttpVerb = null;
      public string HttpOrigin = null;
      public MethodInfo ContractMethod = null;
      public bool SkipBody = false;
    }

    private Dictionary<String, MethodInfo> _MethodInfoCache = new Dictionary<String, MethodInfo>();

    public Object AfterReceiveRequest(ref Message incomingWcfMessage, IClientChannel channel, InstanceContext instanceContext) {

      HttpRequestMessageProperty httpRequest = (HttpRequestMessageProperty)incomingWcfMessage.Properties[HttpRequestMessageProperty.Name];

      CorellationStateContainer corellationState = new CorellationStateContainer();
      corellationState.HttpVerb = httpRequest.Method;
      corellationState.HttpOrigin = httpRequest.Headers["origin"]?.ToString();
      if (string.IsNullOrWhiteSpace(corellationState.HttpOrigin)) {
        corellationState.HttpOrigin = "*";
      }

      //no hook neccessarry
      if (UjmwHostConfiguration.AuthHeaderEvaluator == null && _InboundSideChannelCfg.AcceptedChannels.Length == 0) {
        corellationState.SkipBody = true;
        return corellationState;
      }

      RemoteEndpointMessageProperty clientEndpoint = OperationContext.Current.IncomingMessageProperties[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;
      string callingMachine = clientEndpoint.Address.ToString();

      if (httpRequest.Method.Equals("OPTIONS", StringComparison.CurrentCultureIgnoreCase)) {
        corellationState.SkipBody = true;
        return corellationState;
      }

      Type serviceContractType = instanceContext.Host.Description.Endpoints[0].Contract.ContractType;

      string methodName = null;
      string fullCallUrl = null;
      if (incomingWcfMessage.Properties.TryGetValue("Via", out object via)) {
        fullCallUrl = via?.ToString();
      }
      if (incomingWcfMessage.Properties.TryGetValue("HttpOperationName", out object httpOperationName)) {
        methodName = httpOperationName?.ToString();
        if (methodName == string.Empty) {
          HookedOperationInvoker.CatchedExeptionFromCurrentOperation.Value = $"Unknown method (see Contract: '{serviceContractType.Name}') OR wrong HTTP verb!";
          return corellationState;
        }
      }
      if (methodName == null || fullCallUrl == null) {
        //happens, when .svc-Endpoint is called directly via Http-GET from browser!
        //so we want the outbound hook to skip immediately
        return null;
      }

      lock (_MethodInfoCache) {
        if (!_MethodInfoCache.TryGetValue(fullCallUrl, out corellationState.ContractMethod)) {

          if (!TryGetContractMethod(serviceContractType, methodName, out corellationState.ContractMethod)) {
            DevToTraceLogger.LogError(72005, $"Method '{methodName}' not found on contract type '{serviceContractType.Name}'!");
            throw new WebFaultException(HttpStatusCode.InternalServerError);
          }
          _MethodInfoCache[fullCallUrl] = corellationState.ContractMethod;
        }
      }

      int httpReturnCode = 200;
      string failedReason = string.Empty;

      if (UjmwHostConfiguration.AuthHeaderEvaluator != null) {

        string rawAuthHeader = null;

        if (httpRequest.Headers.AllKeys.Contains("Authorization")) {
          rawAuthHeader = httpRequest.Headers["Authorization"];
        }

        bool authSuccess = UjmwHostConfiguration.AuthHeaderEvaluator.Invoke(
          rawAuthHeader, serviceContractType, corellationState.ContractMethod,
          callingMachine, ref httpReturnCode, ref failedReason
        );

        if (!authSuccess) {

          DevToTraceLogger.LogWarning(72004, "Rejected incomming request because AuthHeaderEvaluator returned false!");

          if (string.IsNullOrWhiteSpace(failedReason)) {
            failedReason = "Forbidden";
          }
          HookedOperationInvoker.CatchedExeptionFromCurrentOperation.Value = failedReason;

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
          try {
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
              _InboundSideChannelCfg.ProcessingMethod.Invoke(corellationState.ContractMethod, sideChannelContent);
              break;
            }
          }
          catch (Exception ex) {
            HookedOperationInvoker.CatchedExeptionFromCurrentOperation.Value = "No valid JSON";
            throw new WebFaultException(HttpStatusCode.BadRequest);
          }
        }
        else { //lets look into the http header
          if (httpRequest.Headers.TryGetValue(acceptedChannel, out string rawSideChannelContent)) {
            var serializer = new Newtonsoft.Json.JsonSerializer();
            sideChannelContent = JsonConvert.DeserializeObject<Dictionary<string, string>>(rawSideChannelContent);
            sideChannelReceived = true;
            _InboundSideChannelCfg.ProcessingMethod.Invoke(corellationState.ContractMethod, sideChannelContent);
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
            _InboundSideChannelCfg.ProcessingMethod.Invoke(corellationState.ContractMethod, sideChannelContent);
          }
        }
        else {
          DevToTraceLogger.LogWarning(72003, "Rejected incomming request because of missing side channel");
          HookedOperationInvoker.CatchedExeptionFromCurrentOperation.Value = "No sidechannel provided.";
          throw new WebFaultException(HttpStatusCode.BadRequest);
        }

      }

      ///// (end) RESTORE INCOMMING SIDECHANNEL /////

      return corellationState; // << this handle will be passed to 'BeforeSendReply' as 'correlationState'

    }

    public void BeforeSendReply(ref Message outgoingWcfMessage, Object correlationState) {
      CorellationStateContainer state = null;
      if ((correlationState is CorellationStateContainer)) {
        state = (CorellationStateContainer)correlationState;
      }

      //prepare some ugly WCF hacking (get access to the HttpResponse)   
      HttpResponseMessageProperty outgoingHttpResponse = null;
      if (outgoingWcfMessage.Properties.ContainsKey(HttpResponseMessageProperty.Name)) {
        outgoingHttpResponse = (HttpResponseMessageProperty)outgoingWcfMessage.Properties[HttpResponseMessageProperty.Name];
      }
      else {
        outgoingHttpResponse = new HttpResponseMessageProperty();
        outgoingWcfMessage.Properties.Add(HttpResponseMessageProperty.Name, outgoingHttpResponse);
      }

      //apply additional headers globally (especially for CORS)
      outgoingHttpResponse.Headers["Allow"] = "POST,OPTIONS";
      if (!string.IsNullOrEmpty(UjmwHostConfiguration.CorsAllowOrigin)) {
        string corsOrigin = UjmwHostConfiguration.CorsAllowOrigin;
        if (state != null) {
          corsOrigin = corsOrigin.Replace("{origin}", state.HttpOrigin);
        }
        else {
          corsOrigin = corsOrigin.Replace("{origin}", "*");
        }
        outgoingHttpResponse.Headers.Add("Access-Control-Allow-Origin", corsOrigin);
      }
      if (!string.IsNullOrEmpty(UjmwHostConfiguration.CorsAllowMethod)) {
        outgoingHttpResponse.Headers.Add("Access-Control-Allow-Method", UjmwHostConfiguration.CorsAllowMethod);
      }
      if (!string.IsNullOrEmpty(UjmwHostConfiguration.CorsAllowHeaders)) {
        outgoingHttpResponse.Headers.Add("Access-Control-Allow-Headers", UjmwHostConfiguration.CorsAllowHeaders);
      }

      if (state == null && outgoingHttpResponse.StatusCode == HttpStatusCode.OK) {
        outgoingHttpResponse.StatusCode = HttpStatusCode.OK;
        //dont skip body - because this resonse contains the .svc-overview page
        return;
      }
      if (state != null && state.SkipBody) {
        outgoingHttpResponse.StatusCode = HttpStatusCode.OK;
        //disable that html response body with WCF-ramblings
        outgoingHttpResponse.SuppressEntityBody = true;
        return;
      }
      if (state?.ContractMethod == null) { //<< indicates a BadRequest
        outgoingHttpResponse.StatusCode = HttpStatusCode.BadRequest;

        //disable that html response body with WCF-ramblings
        outgoingHttpResponse.SuppressEntityBody = true;

        //in all cases, we habve a body-less response, so it makes sence to apply error-messages
        //to the Http-StatusDescription (because there is no fault property)!
        if (!string.IsNullOrWhiteSpace(HookedOperationInvoker.CatchedExeptionFromCurrentOperation.Value)) {
          outgoingHttpResponse.StatusDescription = HookedOperationInvoker.CatchedExeptionFromCurrentOperation.Value;
        }

        return;
      }

      if (_OutboundSideChannelCfg.ChannelsToProvide.Length > 0) {
        ///// CAPTURE OUTGOING BACKCHANNEL /////

        //COLLECT THE DATA
        string serializedSnapshot = null;
        foreach (string channelName in _OutboundSideChannelCfg.ChannelsToProvide) {
          if (channelName == "_") {
            //NOTE: for that property we are to late because the body has already been serialized
            //therfore we háve already captured the data in the 'CustomizedJsonFormatter'
          }
          else {
            if (serializedSnapshot == null) { //on-demand, but bufferred...
              var snapshotContainer = new Dictionary<string, string>();
              _OutboundSideChannelCfg.CaptureMethod.Invoke(state.ContractMethod, snapshotContainer);
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
      if (message.IsEmpty) {
        return string.Empty;
      }
      //vvv nötig, weil jede message nur 1x gelsen werden kann
      message = buffer.CreateMessage();
      bodyBytes = buffer.CreateMessage().GetBody<byte[]>();
      return System.Text.Encoding.UTF8.GetString(bodyBytes);
    }

    internal static bool TryGetContractMethod(Type serviceContractType, string methodName, out MethodInfo method) {
      method = serviceContractType.GetMethod(methodName);
      if (method != null) {
        return true;
      }
      foreach (Type aggregatedContract in serviceContractType.GetInterfaces()) {
        if (TryGetContractMethod(aggregatedContract, methodName, out method)) {
          return true;
        }
      }
      if (serviceContractType.BaseType != null) {
        return TryGetContractMethod(serviceContractType.BaseType, methodName, out method);
      }
      else {
        return false;
      }
    }

  }

}
