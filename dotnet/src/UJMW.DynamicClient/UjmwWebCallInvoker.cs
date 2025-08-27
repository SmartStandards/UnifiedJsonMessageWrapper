using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;

namespace System.Web.UJMW {

  //developed on base of 'https://github.com/KornSW/W3bstract/tree/master/W3bstract/W3bstract.WebServiceConnector/Client'

  internal class UjmwWebCallInvoker: IAbstractWebcallInvoker {

    private Type _ContractType;
    private IHttpPostExecutor _HttpPostExecutor;
    private Func<string> _UrlGetter;
    private OutgoingRequestSideChannelConfiguration _RequestSidechannelCfg;
    private IncommingResponseSideChannelConfiguration _ResponseSidechannelCfg;
    private Action _OnDisposeInvoked;

    public UjmwWebCallInvoker(
      Type applicableType,
      IHttpPostExecutor httpPostExecutor,
      Func<string> urlGetter,
      Action onDisposeInvoked = null
    ) {
      _ContractType = applicableType;
      _HttpPostExecutor = httpPostExecutor;
      _UrlGetter = urlGetter;
      _RequestSidechannelCfg = UjmwClientConfiguration.GetRequestSideChannelConfiguration(applicableType);
      _ResponseSidechannelCfg = UjmwClientConfiguration.GetResponseSideChannelConfiguration(applicableType);
      _OnDisposeInvoked = onDisposeInvoked;
      if (_OnDisposeInvoked == null) {
        _OnDisposeInvoked = (() => { });
      }
    }

    private static MethodInfo FindMethod(Type declaringType,string methodName) {
      MethodInfo m = declaringType.GetMethod(methodName);
      if(m != null) {
        return m; //99%
      }
      if(declaringType.BaseType != null) {
        m = FindMethod(declaringType.BaseType, methodName);
        if (m != null) {
          return m;
        }
      }
      foreach (Type iType in declaringType.GetInterfaces()){
        m = FindMethod(iType, methodName);
        if (m != null) {
          return m;
        }
      }
      return null;
    }

    private string _CachedEndpointUrl = null;
    private DateTime _EndpointUrlCacheTime = DateTime.MinValue;

    public object InvokeWebCall(string methodName, object[] arguments, string[] argumentNames, string methodSignatureString) {
         
      if(methodName == nameof(IDisposable.Dispose)) {
        _OnDisposeInvoked.Invoke();
        return null;
      }

      if (_EndpointUrlCacheTime < DateTime.Now) {
        _CachedEndpointUrl = _UrlGetter.Invoke();
        if (!_CachedEndpointUrl.EndsWith("/")) {
          _CachedEndpointUrl += "/";
        }
        _EndpointUrlCacheTime = DateTime.Now.AddSeconds(
          UjmwClientConfiguration.UrlGetterCacheSec
        );
      }

      string endpointUrl = _CachedEndpointUrl;

      int currentTry = 0;
      do {
        currentTry++;

        try {

          return InvokeWebCallInternal(endpointUrl, methodName, arguments, argumentNames, methodSignatureString);

        }
        catch (Exception ex) {
          if(!UjmwClientConfiguration.RetryDecider.Invoke(_ContractType, ex, currentTry, ref endpointUrl)) {
            throw;
          }
          if (!endpointUrl.EndsWith("/")) {
            endpointUrl += "/";
          }
        }

        Threading.Thread.Sleep(100); //< security feature 1
      } while (currentTry < 20); //< security feature 2
      throw new Exception("UJMW Dynamic Client detected an endless loop caused by the 'UjmwClientConfiguration.RetryDecider'!");

    }

    private object InvokeWebCallInternal(string rootUrl, string methodName, object[] arguments, string[] argumentNames, string methodSignatureString) {

      string fullUrl = rootUrl + methodName;

      MethodInfo method = FindMethod(_ContractType, methodName);
      var requestContent = new Dictionary<string, object>();
      Dictionary<string, string> requestHeaders = null;

      ///// CAPTURE OUTGOING SIDECHANNEL /////
      if (_RequestSidechannelCfg.UnderlinePropertyIsProvided || _RequestSidechannelCfg.ChannelsToProvide.Any()) {
        var sideChannelContent = new Dictionary<string, string>();
        string sideChannelJson = null;

        _RequestSidechannelCfg.CaptureMethod.Invoke(method, sideChannelContent);

        foreach (string channel in _RequestSidechannelCfg.ChannelsToProvide) {
          if (channel == "_") {
            requestContent["_"] = sideChannelContent;
          }
          else { 
            if(requestHeaders == null) {
              requestHeaders = new Dictionary<string, string>();
              //will be done once, if we need in as json
              sideChannelJson = JsonConvert.SerializeObject(sideChannelContent);
            }
            requestHeaders[channel] = sideChannelJson;
          }
        }
      }
      ///// (end) CAPTURE OUTGOING SIDECHANNEL /////

      var parameters = method.GetParameters();

      int paramIndex = 0;
      foreach (var param in parameters) {
        if (param.ParameterType.IsByRef) {
          if (param.IsOut) {
            //OUT
          }
          else {
            //REF
            requestContent[param.Name] = arguments[paramIndex];
          }
        }
        else if (param.IsOptional) {
          bool optionalValueNotProvided = (arguments[paramIndex] != DBNull.Value);
          //OPT
          if (optionalValueNotProvided) {
            requestContent[param.Name] = arguments[paramIndex];
          }
        }
        else {
          requestContent[param.Name] = arguments[paramIndex];
        }
        paramIndex++;
      }

      var jss = new JsonSerializerSettings();
      jss.Formatting = Formatting.Indented;
      jss.DateFormatHandling = DateFormatHandling.IsoDateFormat;
      jss.ContractResolver = new CamelCasePropertyNamesContractResolver();
      string rawJsonRequest = JsonConvert.SerializeObject(requestContent, jss);

      // ############### HTTP POST #############################################
       
      int httpReturnCode = _HttpPostExecutor.ExecuteHttpPost(
        fullUrl,
        rawJsonRequest, requestHeaders,
        out string rawJsonResponse, out var responseHeaders,
        out string reasonPhrase
      );

      // Informational responses (100 – 199)
      // Successful responses(200 – 299)
      // Redirection messages(300 – 399)
      // Client error responses(400 – 499)
      // Server error responses(500 – 599)
      if (httpReturnCode == 401) {
        throw new UnauthorizedAccessException($"Authorization issue! Received HTTP code 401 - {reasonPhrase} (URL: '{fullUrl}').");
      }
      else if (httpReturnCode < 200 || httpReturnCode > 299) {
        throw new Exception($"Response indicates no success! Received HTTP code {httpReturnCode} - '{reasonPhrase}'  (URL: '{fullUrl}' - Request: '{rawJsonRequest}').");
      }

      //some old technologies can only return XML-encapulated replies
      //this is an hack to support this
      if (rawJsonResponse.StartsWith("<UJMW>")) {
        rawJsonResponse = rawJsonResponse.Substring(6, rawJsonResponse.Length - 13);
      }

      // #######################################################################

      var objectDeserializer = new JsonSerializer();
      object returnValue = null;
      using (StringReader sr = new StringReader(rawJsonResponse)) {
        using (JsonTextReader jr = new JsonTextReader(sr)) {
          jr.Read();
          if(jr.TokenType != JsonToken.StartObject) {
            throw new Exception("Response is no valid JSON: " + rawJsonResponse);
          }

          IDictionary<string, string> backChannelContent = null;
          string currentPropName = "";
          while(jr.Read()) {

            if(jr.TokenType == JsonToken.PropertyName) {
              currentPropName = jr.Value.ToString();
              if (currentPropName == "_") {
                jr.Read();
                backChannelContent = objectDeserializer.Deserialize<Dictionary<string, string>>(jr);
              }
              else if(currentPropName.Equals("return",StringComparison.InvariantCultureIgnoreCase)) {
                jr.Read();
                if (method.ReturnType != typeof(void)) {
                  returnValue = objectDeserializer.Deserialize(jr, method.ReturnType);
                }
              }
              else if (currentPropName.Equals("fault", StringComparison.InvariantCultureIgnoreCase)) {
                string faultMessage = jr.ReadAsString();
                if (!String.IsNullOrWhiteSpace(faultMessage)) {
                  UjmwClientConfiguration.FaultRepsonseHandler.Invoke(fullUrl, method, faultMessage);
                }
              }
              else {
                jr.Read();
                var param = parameters.Where((p) => p.Name.Equals(currentPropName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                  object value = null;
                  if (param != null && param.ParameterType.IsByRef) {
                    Type typeToDeserialize = param.ParameterType.GetElementType();
                    value = objectDeserializer.Deserialize(jr, typeToDeserialize);

                    int argIndex = Array.IndexOf(argumentNames, param.Name);
                    arguments[argIndex] = value;
                  }
              }
            }
            else if (jr.TokenType == JsonToken.StartObject) {
              string rawJson = jr.ReadAsString();

            }
            else {
            }
          }

          ///// RESTORE INCOMMING BACKCHANNEL /////
          if (_ResponseSidechannelCfg.UnderlinePropertyIsAccepted || _ResponseSidechannelCfg.AcceptedChannels.Length > 0) {

            bool backChannelreceived = false;
            foreach (string channelName in _ResponseSidechannelCfg.AcceptedChannels) {
              if (channelName == "_") {
                if (backChannelContent != null) {
                  ////// PROCESS //////
                  _ResponseSidechannelCfg.ProcessingMethod.Invoke(method, backChannelContent);
                  backChannelreceived = true;
                  break;
                }
              }
              else if (responseHeaders != null) {
                var hdr = responseHeaders.Where((h)=>h.Key == channelName);
                if (hdr.Any()) {
                  //will be done once, if we need in as json
                  string sideChannelFromHeader = hdr.First().Value.ToString();
                  backChannelContent = JsonConvert.DeserializeObject<Dictionary<string, string>>(sideChannelFromHeader);
                  ////// PROCESS //////
                  _ResponseSidechannelCfg.ProcessingMethod.Invoke(method, backChannelContent);
                  backChannelreceived = true;
                  break;
                }
              }
            }

            if (!backChannelreceived) {
              if (_ResponseSidechannelCfg.SkipAllowed) {
                //if the whole getter is null, then (and only in this case) it will be a 'silent skip'
                if(_ResponseSidechannelCfg.DefaultsGetterOnSkip != null) {
                  backChannelContent = new Dictionary<string, string>();
                  _ResponseSidechannelCfg.DefaultsGetterOnSkip.Invoke(ref backChannelContent);
                  //also null (when the DefaultsGetterOnSkip sets the ref handle to null) can be
                  //passed to the processing method...
                  _ResponseSidechannelCfg.ProcessingMethod.Invoke(method, backChannelContent);
                }
              }
              else {
                Trace.TraceWarning("Rejected incomming response because of missing side channel");
                throw new Exception("Response has no SideChannel");
              }
            }

          }
          ///// (end) RESTORE INCOMMING BACKCHANNEL /////

        }
      }

      return returnValue;
    }

  }

}
