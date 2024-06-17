using Logging.SmartStandards;
using Microsoft.AspNetCore.Authorization;
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

      public DynamicControllerBase(TServiceInterface serviceInstance) {
        if (serviceInstance == null) {
          throw new Exception($"The dynamic asp controller base requires to get an '{typeof(TServiceInterface)}' injected - but got null!");
        }
        _ServiceInstance = serviceInstance;
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

      protected object InvokeMethod(string methodName, object requestDto) {

        Func<TServiceInterface, object, IHeaderDictionary, IHeaderDictionary, object> redirector =
          GetOrCreateRedirectorMethod(methodName, this);

        object responseDto = redirector.Invoke(
          _ServiceInstance,
          requestDto,
          this.HttpContext.Request.Headers,
          this.HttpContext.Response.Headers
        );        

        return responseDto;
      }

      private static Dictionary<string, Func<TServiceInterface, object, IHeaderDictionary, IHeaderDictionary, object>> _RedirectorMethods =
        new Dictionary<string, Func<TServiceInterface, object, IHeaderDictionary, IHeaderDictionary, object>>();

      private static Func<TServiceInterface, object, IHeaderDictionary, IHeaderDictionary, object> GetOrCreateRedirectorMethod(string methodName, DynamicControllerBase<TServiceInterface> controller) {
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
          Func<TServiceInterface, object, IHeaderDictionary, IHeaderDictionary, object> mth = (
            (svc, requestDto, requestHeaders, responseHeaders) => {
              object responseDto = Activator.CreateInstance(responseDtoType);
              object[] serviceMethodParams = new object[paramCount];

              //map the in-args
              foreach (DtoValueMapper requestDtoValueMapper in requestDtoValueMappers) {
                requestDtoValueMapper.MapRequestDtoToParam(requestDto, serviceMethodParams);
              }
          
              try {

                ///// RESTORE INCOMMING SIDECHANNEL /////

                bool sideChannelReceived = false;
                IDictionary<string, string> sideChannelContent = null;
                foreach (string acceptedChannel in inboundSideChannelCfg.AcceptedChannels) {
                  if (acceptedChannel == "_") {
                    if (requestSidechannelProp != null) {
                      var container = (Dictionary<string, string>)requestSidechannelProp.GetValue(requestDto);
                      if (container != null) {
                        sideChannelReceived = true;
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
                      inboundSideChannelCfg.DefaultsGetterOnSkip.Invoke(ref sideChannelContent);
                      //also null (when the DefaultsGetterOnSkip sets the ref handle to null) can be
                      //passed to the processing method...
                      inboundSideChannelCfg.ProcessingMethod.Invoke(contractMethod, sideChannelContent);
                    }
                  }
                  else {
                    string msg = "Rejected incomming request because of missing side channel";
                    LogToTraceAdapter.LogWarning(msg);
                    throw new Exception(msg);
                  }
                }

                LogToTraceAdapter.LogTrace($"Invoking UJMW call to UJMW Operation '{contractMethod.Name}'");

                ///// (end) RESTORE INCOMMING SIDECHANNEL /////

                if (UjmwHostConfiguration.ArgumentPreEvaluator != null) {
                  try {
                    UjmwHostConfiguration.ArgumentPreEvaluator.Invoke(
                      contractType, contractMethod, serviceMethodParams
                    );
                  }
                  catch (TargetInvocationException ex) {
                    throw new ApplicationException($"ArgumentPreEvaluator for '{contractMethod.Name}' has thrown an Exception: " + ex.InnerException.Message, ex.InnerException);
                  }
                  catch (Exception ex) {
                    throw new ApplicationException($"ArgumentPreEvaluator for '{contractMethod.Name}' has thrown an Exception: " + ex.Message, ex);
                  }
                }

                //invoke the service method
                object returnVal;
                try {
                  returnVal = contractMethod.Invoke(svc, serviceMethodParams);
                }
                catch (TargetInvocationException ex) {
                  throw new ApplicationException($"BL-Method '{contractMethod.Name}' has thrown an Exception: " + ex.InnerException.Message, ex.InnerException);
                }
                catch (Exception ex) {
                  throw new ApplicationException($"BL-Method '{contractMethod.Name}' has thrown an Exception: " + ex.Message, ex);
                }

                //map the return value
                foreach (DtoValueMapper responseDtoValueMapper in responseDtoValueMappers) {
                  responseDtoValueMapper.MapParamToResponseDto(serviceMethodParams, responseDto);
                }

                //map the out-/ref-args
                if (returnProp != null) {
                  returnProp.SetValue(responseDto, returnVal);
                }

              }
              catch (Exception ex) {
                LogToTraceAdapter.LogError(ex);
                //UjmwHostConfiguration.LoggingHook.Invoke(4, $"UJMW Operation has thrown Exception: {ex.Message}");
                if (faultProp != null) {
                  if (UjmwHostConfiguration.HideExeptionMessageInFaultProperty) {
                    faultProp.SetValue(responseDto, "BL-Exception");
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
