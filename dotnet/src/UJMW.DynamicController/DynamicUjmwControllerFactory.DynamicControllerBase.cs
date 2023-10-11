using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
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

      protected object InvokeMethod(string methodName, object requestDto) {
        Func<TServiceInterface, object, object> redirector = GetOrCreateRedirectorMethod(methodName, this);
        object responseDto = redirector.Invoke(_ServiceInstance, requestDto);
        return responseDto;
      }

      private static Dictionary<string, Func<TServiceInterface, object, object>> _RedirectorMethods =
        new Dictionary<string, Func<TServiceInterface, object, object>>();

      private static Func<TServiceInterface, object, object> GetOrCreateRedirectorMethod(string methodName, DynamicControllerBase<TServiceInterface> controller) {
        lock (_RedirectorMethods) {

          if (_RedirectorMethods.ContainsKey(methodName)) {
            return _RedirectorMethods[methodName];
          } //doing the expensive creaction of mapping code only once (on demand) and give the handles into a lambda:

          MethodInfo serviceMethod = typeof(TServiceInterface).GetMethod(methodName);
          MethodInfo controllerMethod = controller.GetType().GetMethod(methodName);

          Type requestDtoType = controllerMethod.GetParameters().Single().ParameterType;
          Type responseDtoType = controllerMethod.ReturnType;

          var requestDtoValueMappers = new List<DtoValueMapper>();
          var responseDtoValueMappers = new List<DtoValueMapper>();

          ParameterInfo[] serviceMethodParams = serviceMethod.GetParameters();
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
          if (serviceMethod.ReturnType != null && serviceMethod.ReturnType != typeof(void)) {
            returnProp = responseDtoType.GetProperty(UjmwReturnPropertyName);
          }

          PropertyInfo faultProp = responseDtoType.GetProperty(UjmwFaultPropertyName);

          PropertyInfo requestSidechannelProp = requestDtoType.GetProperty(UjmwSideChannelPropertyName);
          PropertyInfo responseSidechannelProp = responseDtoType.GetProperty(UjmwSideChannelPropertyName);

          /////////// lambda (mth) ////////////////////////////////////////////
          Func<TServiceInterface, object, object> mth = (
            (svc, requestDto) => {
              object responseDto = Activator.CreateInstance(responseDtoType);
              object[] serviceMethodParams = new object[paramCount];

              //map the in-args
              foreach (DtoValueMapper requestDtoValueMapper in requestDtoValueMappers) {
                requestDtoValueMapper.MapRequestDtoToParam(requestDto, serviceMethodParams);
              }

              try {

                if (UjmwServiceBehaviour.RequestSidechannelProcessor != null && requestSidechannelProp != null) {
                  var container = (Dictionary<string, string>)requestSidechannelProp.GetValue(requestDto);
                  UjmwServiceBehaviour.RequestSidechannelProcessor.Invoke(serviceMethod, container);
                }

                //invoke the service method
                object returnVal = serviceMethod.Invoke(svc, serviceMethodParams);

                //map the return value
                foreach (DtoValueMapper responseDtoValueMapper in responseDtoValueMappers) {
                  responseDtoValueMapper.MapParamToResponseDto(serviceMethodParams, responseDto);
                }

                //map the out-/ref-args
                if (returnProp != null) {
                  returnProp.SetValue(responseDto, returnVal);
                }

              }
              catch (TargetInvocationException ex) {
                if (faultProp != null) {
                  faultProp.SetValue(responseDto, ex.InnerException.Message);
                }
              }
              catch (Exception ex) {
                if (faultProp != null) {
                  faultProp.SetValue(responseDto, ex.Message);
                }
              }

              if (UjmwServiceBehaviour.ResponseSidechannelCapturer != null && responseSidechannelProp != null) {
                var container = new Dictionary<string, string>();
                UjmwServiceBehaviour.ResponseSidechannelCapturer.Invoke(serviceMethod, container);
                responseSidechannelProp.SetValue(responseDto, container);
              }

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
