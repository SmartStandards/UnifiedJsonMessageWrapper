using Logging.SmartStandards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using System.Runtime.ConstrainedExecution;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace System.Web.UJMW {

  partial class DynamicUjmwControllerFactory {

    public class DynamicControllerBase<TServiceInterface> : Controller {

      private TServiceInterface _ServiceInstance;
      private DynamicUjmwControllerOptions _Options;

      private delegate void ContextualArgumentCollectorDelegate(ref IDictionary<string, string> targetDict, string[] includedArgNames);
      private ContextualArgumentCollectorDelegate _ContextualArgumentCollector;

      public DynamicControllerBase(TServiceInterface serviceInstance) {
        if (serviceInstance == null) {
          throw new Exception($"The dynamic asp controller base requires to get an '{typeof(TServiceInterface)}' injected - but got null!");
        }

        _ServiceInstance = serviceInstance;

        _Options = DynamicUjmwControllerFactory.GetDynamicUjmwControllerOptions(this.GetType());

        KeyValuePair<string, Func<string>>[] contextualArgumentGetters = _Options.GetUniformedContextualArgumentGetters(
          (headerName) => this.HttpContext.Request.Headers[headerName].FirstOrDefault(),
          (routeSegmentAlias) => this.RouteData.Values[routeSegmentAlias]?.ToString()
        ).ToArray(); //NOTE: ToArray in combination with KVP ist used to freeze the collection and avoid threading issues!

        _ContextualArgumentCollector = (ref IDictionary<string, string> targetDict, string[] includedArgNames) => {
          if(includedArgNames == null) {
            return;
          }
          if(targetDict == null) {
            targetDict = new Dictionary<string, string>();
          }
          //invoke all getters and fill the target dict
          foreach (KeyValuePair<string, Func<string>> nameAndGetter in contextualArgumentGetters) {
            if (includedArgNames.Length > 0 && !includedArgNames.Contains(nameAndGetter.Key)) {
              continue;
            }
            try {
              targetDict[nameAndGetter.Key] = nameAndGetter.Value?.Invoke();
            }
            catch (Exception ex) {
              throw new ApplicationException($"Contextual Argument Getter for '{nameAndGetter.Key}' has thrown an Exception: " + ex.Message, ex);
            }
          }
        };

      }

      /// <summary>
      /// Follows the convention to expose infomration about a related contract...
      /// (this is also used by AuthTokenhandling to evaluate target methods)
      /// </summary>
      public Type ContractType { 
        get {
          return typeof(TServiceInterface);
        }
      }

      //WILL BE INVOKED VIA EMITED CODE FROM DYNAMIC PROXY-FACADE WHIch IS INHERITING FROM US!
      protected IActionResult RenderInfoSite() {
        return Content($"<html>\n  <head>\n    <title>{this.ContractType.Name} (UJMW-Endpoint)</title>\n  </head>\n  <body style=\"font-family: system-ui;\r\n    font-size: 12px;\"><h1>UJMW-Endpoint</h1>\n    <b>Contract:</b> {this.ContractType.FullName}<br>\n    <b>Instance:</b> {_ServiceInstance.GetType().FullName}\n  </body>\n</html>", "text/html");    
      }

      //WILL BE INVOKED VIA EMITED CODE FROM DYNAMIC PROXY-FACADE WHIch IS INHERITING FROM US!
      protected object InvokeMethod(string methodName, object requestDto) {
        try {

          RedirectorMethodDelegate redirector = GetOrCreateRedirectorMethod(methodName, this);

          object responseDto = redirector.Invoke(
            _ServiceInstance,
            requestDto,
            this.HttpContext.Request.Headers,
            this.HttpContext.Response.Headers,
            _Options,
            _ContextualArgumentCollector
          );        

          return responseDto;
        }
        catch (Exception ex) {
          //LAST DEFENCE - SHOULD NEVER HAPPEN
          DevLogger.LogCritical(ex.Wrap(72500, $"UJMW Operation '{methodName}' failed: {ex.Message}"));
          this.HttpContext.Response.StatusCode = 500;
          throw;
        }
      }

      private delegate object RedirectorMethodDelegate(
        TServiceInterface serviceInstance,
        object requestDto,
        IHeaderDictionary requestHeaders,
        IHeaderDictionary responseHeaders,
        DynamicUjmwControllerOptions options,
        ContextualArgumentCollectorDelegate contextualArgumentCollector
      );

      /// A Cache for the redirector methods
      private static Dictionary<string, RedirectorMethodDelegate> _RedirectorMethods = new Dictionary<string, RedirectorMethodDelegate>();

      private static RedirectorMethodDelegate GetOrCreateRedirectorMethod(string methodName, DynamicControllerBase<TServiceInterface> controller) {
        lock (_RedirectorMethods) {

          if (_RedirectorMethods.ContainsKey(methodName)) {
            return _RedirectorMethods[methodName];
          } //doing the expensive creaction of mapping code only once (on demand) and give the handles into a lambda:

          Type contractType = typeof(TServiceInterface);
          MethodInfo controllerMethod = controller.GetType().GetMethod(methodName);
          MethodInfo contractMethod = contractType.GetMethod(methodName);
          if (!DynamicUjmwControllerFactory.TryGetContractMethod(
            contractType, methodName, out contractMethod
          )) {
            throw new Exception($"Cannot find a Method named '{methodName}' on Contract Interface '{contractType.FullName}'!");
          }

          Type requestDtoType = controllerMethod.GetParameters().Single().ParameterType;
          Type responseDtoType = controllerMethod.ReturnType;

          var requestDtoValueMappers = new List<DtoValueMapper>();
          var responseDtoValueMappers = new List<DtoValueMapper>();

          ParameterInfo[] serviceMethodParams = contractMethod.GetParameters();
          int paramCount = serviceMethodParams.Length;
          for (int idx = 0; idx < paramCount; idx++) {
            ParameterInfo param = serviceMethodParams[idx];
            if (!param.IsOut) {
              requestDtoValueMappers.Add(new DtoValueMapper(requestDtoType, responseDtoType, param.Name, idx));
            }
            if (param.IsOut || param.ParameterType.IsByRef) {
              responseDtoValueMappers.Add(new DtoValueMapper(requestDtoType, responseDtoType, param.Name, idx));
            }
          }

          PropertyInfo returnProp = null;
          if (contractMethod.ReturnType != null && contractMethod.ReturnType != typeof(void)) {
            returnProp = responseDtoType.GetProperty(UjmwReturnPropertyName);
          }

          PropertyInfo faultProp = responseDtoType.GetProperty(UjmwFaultPropertyName);

          PropertyInfo requestSidechannelProp = requestDtoType.GetProperty(UjmwSideChannelPropertyName);
          PropertyInfo responseSidechannelProp = responseDtoType.GetProperty(UjmwSideChannelPropertyName);

          IncommingRequestSideChannelConfiguration inboundSideChannelCfg = UjmwHostConfiguration.GetRequestSideChannelConfiguration(typeof(TServiceInterface));
          OutgoingResponseSideChannelConfiguration outboundSideChannelCfg = UjmwHostConfiguration.GetResponseSideChannelConfiguration(typeof(TServiceInterface));

          /////////// lambda (mth) ////////////////////////////////////////////
          RedirectorMethodDelegate mth = (
            (svc, requestDto, requestHeaders, responseHeaders, options, contextualArgumentCollector) => {
              object responseDto = Activator.CreateInstance(responseDtoType);
              object[] serviceMethodParams = new object[paramCount];

              try {
       
                if(requestDto == null) {
                  throw new Exception($"The request DTO is null or could not be deserialized!");
                }

                //map the in-args
                foreach (DtoValueMapper requestDtoValueMapper in requestDtoValueMappers) {
                  requestDtoValueMapper.MapRequestDtoToParam(requestDto, serviceMethodParams);
                }

                ///// RESTORE INCOMMING SIDECHANNEL /////

                bool sideChannelReceived = false;
                IDictionary<string, string> sideChannelContent = null;
                foreach (string acceptedChannel in inboundSideChannelCfg.AcceptedChannels) {
                  if (acceptedChannel == "_") {
                    if (requestSidechannelProp != null) {
                      var container = (IDictionary<string, string>)requestSidechannelProp.GetValue(requestDto);
                      if (container != null) {
                        sideChannelReceived = true;
                        //overlay the endpoint-individual 'contextualArgument's
                        contextualArgumentCollector.Invoke(ref container, inboundSideChannelCfg.ContextualArgumentsToOverlay);
                        //now invoke the propagation into the ambience-room
                        inboundSideChannelCfg.ProcessingMethod.Invoke(contractMethod, container);
                        break;
                      }
                    }
                  }
                  else { //lets look into the http header
                    if(requestHeaders.TryGetValue(acceptedChannel,out string rawSideChannelContent)) {
                      var serializer = new Newtonsoft.Json.JsonSerializer();
                      sideChannelContent = JsonConvert.DeserializeObject<Dictionary<string, string>>(rawSideChannelContent);
                      sideChannelReceived = true;
                      //overlay the endpoint-individual 'contextualArgument's
                      contextualArgumentCollector.Invoke(ref sideChannelContent, inboundSideChannelCfg.ContextualArgumentsToOverlay);
                      //now invoke the propagation into the ambience-room
                      inboundSideChannelCfg.ProcessingMethod.Invoke(contractMethod, sideChannelContent);
                      break;
                    }
                  }
                }

                if (!sideChannelReceived && inboundSideChannelCfg.AcceptedChannels.Length > 0) {
                  if (inboundSideChannelCfg.SkipAllowed) {
                    //if the whole getter is null, then (and only in this case) it will be a 'silent skip'
                    if (inboundSideChannelCfg.DefaultsGetterOnSkip != null) {
                      sideChannelContent = new Dictionary<string, string>();
                      //also null (when the DefaultsGetterOnSkip sets the ref handle to null) can be passed to the processing method...
                      inboundSideChannelCfg.DefaultsGetterOnSkip.Invoke(ref sideChannelContent);
                      //overlay the endpoint-individual 'contextualArgument's
                      contextualArgumentCollector.Invoke(ref sideChannelContent, inboundSideChannelCfg.ContextualArgumentsToOverlay);
                      //now invoke the propagation into the ambience-room
                      inboundSideChannelCfg.ProcessingMethod.Invoke(contractMethod, sideChannelContent);
                    }
                  }
                  else {
                    string msg = "Rejected incomming request because of missing side channel";
                    DevLogger.LogWarning(0, 72003, msg);
                    throw new Exception(msg);
                  }
                }

                DevLogger.LogTrace(0, 72000, $"Invoking UJMW call to UJMW Operation '{contractMethod.Name}'");

                ///// (end) RESTORE INCOMMING SIDECHANNEL /////

                bool innerInvokeWasCalled = false;

                //this encapsulation is only to support contextualization hooks exactly arround this closure... 
                Action innerInvokeContextual = ()=> {
                  innerInvokeWasCalled = true;

                  if (UjmwHostConfiguration.ArgumentPreEvaluator != null) {
                    try {
                      UjmwHostConfiguration.ArgumentPreEvaluator.Invoke(
                        contractType, contractMethod, serviceMethodParams
                      );
                    }
                    catch (TargetInvocationException ex) {
                      throw new ApplicationException($"ArgumentPreEvaluator for '{contractMethod.Name}' has thrown an Exception: " + ex.InnerException.Message + " #72001", ex.InnerException);
                    }
                    catch (Exception ex) {
                      throw new ApplicationException($"ArgumentPreEvaluator for '{contractMethod.Name}' has thrown an Exception: " + ex.Message + " #72001", ex);
                    }
                  }

                  //invoke the service method
                  object returnVal;
                  try {
                    returnVal = contractMethod.Invoke(svc, serviceMethodParams);
                  }
                  catch (TargetInvocationException ex) {
                    throw new ApplicationException($"BL-Method '{contractMethod.Name}' has thrown an Exception: " + ex.InnerException.Message + " #72000", ex.InnerException);
                  }
                  catch (Exception ex) {
                    throw new ApplicationException($"BL-Method '{contractMethod.Name}' has thrown an Exception: " + ex.Message + " #72000", ex);
                  }

                  //map the return value
                  foreach (DtoValueMapper responseDtoValueMapper in responseDtoValueMappers) {
                    responseDtoValueMapper.MapParamToResponseDto(serviceMethodParams, responseDto);
                  }

                  //map the out-/ref-args
                  if (returnProp != null) {
                    returnProp.SetValue(responseDto, returnVal);
                  }

                };

                //routing over contextualization hook around the internal invoke...
                if (options.ContextualizationHook != null) {
                  IDictionary<string, string> contextualArguments = new Dictionary<string, string>();
                  contextualArgumentCollector.Invoke(ref contextualArguments, Array.Empty<string>());
                  options.ContextualizationHook.Invoke(contextualArguments, innerInvokeContextual);
                  if (!innerInvokeWasCalled) {
                    throw new Exception("The UJMW ContextualizationHook MUST NOT skip invoking the given inner action!");
                  }
                } else {
                  innerInvokeContextual.Invoke();
                }

              }
              catch (Exception ex) {

                DevLogger.LogError(ex);

                if (faultProp != null) {
                  if (UjmwHostConfiguration.HideExeptionMessageInFaultProperty) {
                    faultProp.SetValue(responseDto, "UJMW Invocation Error");
                  }
                  else {
                    faultProp.SetValue(responseDto, ex.Message);
                  }
                }

              }

              ///// CAPTURE OUTGOING BACKCHANNEL /////
              
              if (outboundSideChannelCfg.ChannelsToProvide.Any()) {
                var backChannelContainer = new Dictionary<string, string>();
                outboundSideChannelCfg.CaptureMethod.Invoke(contractMethod, backChannelContainer);

                //COLLECT THE DATA
                string serializedSnapshot = null;
                foreach (string channelName in outboundSideChannelCfg.ChannelsToProvide) {
                  if (channelName == "_") {
                    if (responseSidechannelProp != null) {
                      responseSidechannelProp.SetValue(responseDto, backChannelContainer);
                    }
                  }
                  else {
                    if (serializedSnapshot == null) { //on-demand, but bufferred...
                      serializedSnapshot = JsonConvert.SerializeObject(backChannelContainer);
                    }
                    responseHeaders.Add(channelName, serializedSnapshot);
                  }
                }

              }

              ///// (end) CAPTURE OUTGOING BACKCHANNEL /////

              return responseDto;
            }
          );/////// end of lambda (mth) ////////////////////////////////////////

          _RedirectorMethods[methodName] = mth;
          return mth;
        }
      }

    }

  }

}
