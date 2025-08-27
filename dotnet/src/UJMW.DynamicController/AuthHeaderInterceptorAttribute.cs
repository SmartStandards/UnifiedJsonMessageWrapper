using Logging.SmartStandards;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace System.Web.UJMW {

  [AttributeUsage(validOn: AttributeTargets.Class)]
  public class AuthHeaderInterceptorAttribute : Attribute, IAsyncActionFilter {

    public AuthHeaderInterceptorAttribute() {
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next) {

      if (UjmwHostConfiguration.AuthHeaderEvaluator == null) {
        await next();
        return;
      }

      try {

        string rawHeader = null;

        if (context.HttpContext.Request.Headers.TryGetValue("Authorization", out var extractedAuthHeader)) {
          rawHeader = extractedAuthHeader.ToString();
          if (String.IsNullOrWhiteSpace(rawHeader)) {
            rawHeader = null;
          }
        }

        HostString apiCaller = context.HttpContext.Request.Host;
        //MethodInfo calledContractMethod = GetMethodInfoFromContext(context);

        Type contractType = GetContractTypeFromContext(context);
        MethodInfo calledContractMethod = null;
        if (context.RouteData.Values.TryGetValue("action", out object actionUntyped)) {
          DynamicUjmwControllerFactory.TryGetContractMethod(
            contractType, (string)actionUntyped, out calledContractMethod
          );

          if(calledContractMethod == null &&
            context.HttpContext.Request.Method.Equals("GET", StringComparison.CurrentCultureIgnoreCase) &&
            DynamicUjmwControllerFactory.RenderInfoSiteMethodName.Equals((string)actionUntyped, StringComparison.CurrentCultureIgnoreCase)
          ) {
            //this info-site can be displayed without any auth-header (especially for GET-requests)
            await next();
            return;
          }

        }

        int httpReturnCode = 200;
        string failedReason = string.Empty;
        if (!UjmwHostConfiguration.AuthHeaderEvaluator.Invoke(
          rawHeader, contractType, calledContractMethod, apiCaller.Host, ref httpReturnCode, ref failedReason
        )) {
         
          if (httpReturnCode == 200) {
            httpReturnCode = 403;
          }

          if (string.IsNullOrWhiteSpace(failedReason)) {
            failedReason = "Forbidden";
          }

          context.Result = new ContentResult() {
            StatusCode = 401,
            Content = failedReason
          };

          return;
        };
        await next();

      }
      catch (Exception ex) {
        DevLogger.LogCritical(ex);
        context.Result = new ContentResult() {
          StatusCode = 500,
          Content = "Error during token validation: " + ex.Message
        };
      }

    }//OnActionExecutionAsync()

    //private static Dictionary<string, MethodInfo> _MethodBuffer = new Dictionary<string, MethodInfo>();

    //private static MethodInfo GetMethodInfoFromContext(ActionExecutingContext context) {
    //  string actionName = null;
    //  string controllerName = null;
    //  if (context.RouteData.Values.TryGetValue("action", out object actionUntyped)) {
    //    actionName = (string)actionUntyped;
    //  }
    //  if (context.RouteData.Values.TryGetValue("controller", out object controllerUntyped)) {
    //    controllerName = (string)controllerUntyped;
    //  }
    //  if (actionName == null) {
    //    return null;
    //  }
    //  string key = controllerName + "." + actionName;
    //  lock (_MethodBuffer) {
    //    if (_MethodBuffer.TryGetValue(key, out MethodInfo mth)) {
    //      return mth;
    //    }
    //    Type coType = context.Controller.GetType();

    //    //special convention, to allow referring to an explicit contractType
    //    PropertyInfo contractProp = coType.GetProperty("ContractType");
    //    if (contractProp != null) {
    //      coType = (Type)contractProp.GetValue(context.Controller);
    //    }

    //    mth = coType.GetMethod(actionName);
    //    _MethodBuffer.Add(key, mth);
    //    return mth;
    //  }
    //}

    private static Dictionary<string, Type> _ContractTypesPerController = new Dictionary<string, Type>();
    private static Type GetContractTypeFromContext(ActionExecutingContext context) {
      string controllerName = null;
      if (context.RouteData.Values.TryGetValue("controller", out object controllerUntyped)) {
        controllerName = (string)controllerUntyped;
      }
      string key = controllerName;
      lock (_ContractTypesPerController) {
        if (_ContractTypesPerController.TryGetValue(key, out Type t)) {
          return t;
        }
        Type coType = context.Controller.GetType();

        //special convention, to allow referring to an explicit contractType
        PropertyInfo contractProp = coType.GetProperty("ContractType");
        if (contractProp != null) {
          coType = (Type)contractProp.GetValue(context.Controller);
        }
        _ContractTypesPerController.Add(key, coType);
        return coType;
      }
    }

  }//AuthHeaderInterceptorAttribute

}
