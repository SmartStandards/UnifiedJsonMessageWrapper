using Logging.SmartStandards;
using Logging.SmartStandards.CopyForUJMW;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace System.Web.UJMW {

  [AttributeUsage(validOn: AttributeTargets.Class)]
  public class AuthHeaderInterceptorAttribute : Attribute, IAsyncActionFilter {

    public AuthHeaderInterceptorAttribute() {
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next) {

      AuthHeaderValidationMethod authHeaderEvaluator = UjmwHostConfiguration.CurrentAuthHeaderEvaluator;
      if (authHeaderEvaluator == null) {
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
    
        string apiCaller = context.HttpContext.Connection.RemoteIpAddress?.ToString();
        if (apiCaller == null) {
          apiCaller = string.Empty;
        }

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

        string apiGroupName = GetApiGroupNameFromContext(context);

        int httpReturnCode = 200;
        string failedReason = string.Empty;
        KeyValuePair<string, string>[] responseHeaders = new KeyValuePair<string, string>[0];
        bool authSuccess = authHeaderEvaluator.Invoke(
          rawHeader, contractType, calledContractMethod, apiGroupName, apiCaller,
          ref httpReturnCode, ref failedReason, ref responseHeaders
        );

        WriteResponseHeaders(context.HttpContext, responseHeaders);

        if (!authSuccess) {
         
          if (httpReturnCode == 200) {
            httpReturnCode = 403;
          }

          if (string.IsNullOrWhiteSpace(failedReason)) {
            if(httpReturnCode == 401) {
              failedReason = "Unauthorized";
            }
            else {
              failedReason = "Forbidden";
            }
          }

          context.Result = new ContentResult() {
            StatusCode = httpReturnCode,
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

    /// <summary>
    /// Writes the response headers returned by the AuthHeaderEvaluator, after replacing the
    /// placeholder '{RequestBaseUrl}' with the base URL of the current request.
    /// </summary>
    private static void WriteResponseHeaders(HttpContext httpContext, KeyValuePair<string, string>[] responseHeaders) {
      if (responseHeaders == null || responseHeaders.Length == 0) {
        return;
      }
      string requestBaseUrl = GetRequestBaseUrl(httpContext.Request);
      foreach (KeyValuePair<string, string> responseHeader in responseHeaders) {
        if (string.IsNullOrWhiteSpace(responseHeader.Key)) {
          continue;
        }
        string headerValue = UjmwHostConfiguration.ResolveRequestBaseUrlPlaceholder(responseHeader.Value, requestBaseUrl);
        httpContext.Response.Headers.Append(responseHeader.Key, headerValue);
      }
    }

    /// <summary>
    /// Builds 'scheme://host[:port]' plus path base (without trailing slash) from the incoming request.
    /// </summary>
    private static string GetRequestBaseUrl(HttpRequest request) {
      string requestBaseUrl = request.Scheme + "://" + request.Host.ToUriComponent() + request.PathBase.ToUriComponent();
      return requestBaseUrl.TrimEnd('/');
    }

    private static Dictionary<Type, string> _ApiGroupNamesPerControllerType = new Dictionary<Type, string>();

    /// <summary>
    /// Returns the API group name (from '[ApiExplorerSettings(GroupName)]' of the controller class)
    /// or an empty string, if no group name is declared.
    /// </summary>
    private static string GetApiGroupNameFromContext(ActionExecutingContext context) {
      Type controllerType = context.Controller.GetType();
      lock (_ApiGroupNamesPerControllerType) {
        string apiGroupName;
        if (_ApiGroupNamesPerControllerType.TryGetValue(controllerType, out apiGroupName)) {
          return apiGroupName;
        }
        apiGroupName = string.Empty;
        ApiExplorerSettingsAttribute apiExplorerSettings = controllerType.GetCustomAttribute<ApiExplorerSettingsAttribute>(true);
        if (apiExplorerSettings != null && !string.IsNullOrWhiteSpace(apiExplorerSettings.GroupName)) {
          apiGroupName = apiExplorerSettings.GroupName;
        }
        _ApiGroupNamesPerControllerType.Add(controllerType, apiGroupName);
        return apiGroupName;
      }
    }

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
