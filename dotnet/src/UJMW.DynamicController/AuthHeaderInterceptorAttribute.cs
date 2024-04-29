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
        MethodInfo calledContractMethod = GetMethodInfoFromContext(context);

        int httpReturnCode = 200;
        if (!UjmwHostConfiguration.AuthHeaderEvaluator.Invoke(rawHeader, calledContractMethod, apiCaller.Host, ref httpReturnCode)) {
         
          if (httpReturnCode == 200) {
            httpReturnCode = 401;
          }

          context.Result = new ContentResult() {
            StatusCode = 401,
            Content = "Forbidden!"
          };

          return;
        };
        await next();

      }
      catch (Exception ex) {
        context.Result = new ContentResult() {
          StatusCode = 500,
          Content = "Error during token validation: " + ex.Message
        };
      }

    }//OnActionExecutionAsync()

    private static Dictionary<string, MethodInfo> _MethodBuffer = new Dictionary<string, MethodInfo>();

    private static MethodInfo GetMethodInfoFromContext(ActionExecutingContext context) {
      string actionName = null;
      string controllerName = null;
      if (context.RouteData.Values.TryGetValue("action", out object actionUntyped)) {
        actionName = (string)actionUntyped;
      }
      if (context.RouteData.Values.TryGetValue("controller", out object controllerUntyped)) {
        controllerName = (string)controllerUntyped;
      }
      if (actionName == null) {
        return null;
      }
      string key = controllerName + "." + actionName;
      lock (_MethodBuffer) {
        if (_MethodBuffer.TryGetValue(key, out MethodInfo mth)) {
          return mth;
        }
        Type coType = context.Controller.GetType();

        //special convention, to allow referring to an explicit contractType
        PropertyInfo contractProp = coType.GetProperty("ContractType");
        if (contractProp != null) {
          coType = (Type)contractProp.GetValue(context.Controller);
        }

        mth = coType.GetMethod(actionName);
        _MethodBuffer.Add(key, mth);
        return mth;
      }

    }

  }//AuthHeaderInterceptorAttribute

}
