using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static System.Web.UJMW.DynamicClientFactory;

namespace System.Web.UJMW {

  //developed on base of 'https://github.com/KornSW/W3bstract/tree/master/W3bstract/W3bstract.WebServiceConnector/Client'

  internal class UjmwWebCallInvoker: IAbstractWebcallInvoker {

    private Type _ContractType;
    private HttpPostMethod _HttpPostMethod;
    private Func<string> _UrlGetter;
    private RequestSidechannelCaptureMethod _RequestSidechannelCaptureMethod;
    private ResponseSidechannelProcessingMethod _ResponseSidechannelProcessor;

    public UjmwWebCallInvoker(
      Type applicableType,
      HttpPostMethod httpPostMethod,
      Func<string> urlGetter,
      RequestSidechannelCaptureMethod requestSidechannelCaptureMethod,
      ResponseSidechannelProcessingMethod responseSidechannelProcessor
    ) {
      _ContractType = applicableType;
      _HttpPostMethod = httpPostMethod;
      _UrlGetter = urlGetter;
      _RequestSidechannelCaptureMethod = requestSidechannelCaptureMethod;
      _ResponseSidechannelProcessor = responseSidechannelProcessor;
    } 

    public object InvokeWebCall(string methodName, object[] arguments, string[] argumentNames, string methodSignatureString) {
      string rootUrl = _UrlGetter.Invoke();
      if (!rootUrl.EndsWith("/")) {
        rootUrl += "/";
      }  
      string fullUrl = rootUrl + methodName;

      MethodInfo method = _ContractType.GetMethod(methodName);

      var requestContent = new Dictionary<string, object>();
    
      if(_RequestSidechannelCaptureMethod != null) {
        var sideChannelContent = new Dictionary<string, string>();
        _RequestSidechannelCaptureMethod.Invoke(sideChannelContent);
        requestContent["_"] = sideChannelContent;
      }

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
      string rawJsonContent = JsonConvert.SerializeObject(requestContent, jss);

      object returnValue = null;

      string rawJsonResponse = _HttpPostMethod.Invoke(fullUrl, rawJsonContent);
      var objectDeserializer = new JsonSerializer();
      using (StringReader sr = new StringReader(rawJsonResponse)) {
        using (JsonTextReader jr = new JsonTextReader(sr)) {
          jr.Read();
          if(jr.TokenType != JsonToken.StartObject) {
            throw new Exception("Response is no valid JSON: " + rawJsonResponse);
          }

          Dictionary<string, string> responseSideChannelContent = null;
          string currentPropName = "";
          while(jr.Read()) {

            if(jr.TokenType == JsonToken.PropertyName) {
              currentPropName = jr.Value.ToString();
              if (currentPropName == "_") {
                jr.Read();
                responseSideChannelContent = objectDeserializer.Deserialize<Dictionary<string, string>>(jr);
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
                  throw new UjmwFaultException(fullUrl, method, faultMessage);
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

          if (_ResponseSidechannelProcessor != null) {
            _ResponseSidechannelProcessor.Invoke(responseSideChannelContent);
          }

        }
      }

      return returnValue;
    }

  }

}
