using Logging.SmartStandards;
using Logging.SmartStandards.CopyForUJMW;
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
using System.Globalization;
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

    public abstract class DynamicControllerBase : Controller {

      protected DynamicUjmwControllerOptions _Options;
      protected string[] _ControllerRoutesFromAttribute;

      protected delegate void ContextualArgumentCollectorDelegate(ref IDictionary<string, string> targetDict, string[] includedArgNames, object requestDto);

      protected ContextualArgumentCollectorDelegate _ContextualArgumentCollector;

      /// <summary>
      /// Follows the convention to expose infomration about a related contract...
      /// (this is also used by AuthTokenhandling to evaluate target methods)
      /// </summary>
      public abstract Type ContractType { get; }

      public abstract Type ResolvedServiceImplementationType { get; }

      #region " Static "

      private static Dictionary<string, Dictionary<Type, DynamicUjmwControllerOptions>> _ControllerInstances = new();

      /// <summary></summary>
      /// <param name="controllerRoute"></param>
      /// <param name="contractType"></param>
      /// <param name="options"></param>
      /// <returns>True if the controller route was announced the very first time by the factory, false otherwise.</returns>
      internal static bool AnnounceControllerRouteAppliedByFactory(
        string controllerRoute, Type contractType, DynamicUjmwControllerOptions options
      ) {
        bool createdNew = false;
        Dictionary<Type, DynamicUjmwControllerOptions> controllersOnThisRoute = null;
        lock (_ControllerInstances) {
          if (!_ControllerInstances.TryGetValue(controllerRoute, out controllersOnThisRoute)) {
            controllersOnThisRoute = new Dictionary<Type, DynamicUjmwControllerOptions>();
            _ControllerInstances[controllerRoute] = controllersOnThisRoute;
            createdNew = true;
          }
        }
        lock (controllersOnThisRoute) {
          controllersOnThisRoute[contractType] = options;
        }
        return createdNew;
      }

      internal static bool IsControllerRouteAlreadyAppliedByFactory(string controllerRoute) {
        lock (_ControllerInstances) {
          bool routeAlreadyApplied = _ControllerInstances.ContainsKey(controllerRoute);
          return routeAlreadyApplied;
        }
      }

      #endregion

      protected static void GetRouteInfoResponse(string controllerRoute, out string content, out string mimeType) {
        Dictionary<string, object> infoMetadata = new Dictionary<string, object>();

        KeyValuePair<Type, DynamicUjmwControllerOptions>[] controllersOnThisRoute = null;

        if (!string.IsNullOrWhiteSpace(controllerRoute)) {
          lock (_ControllerInstances) {
            if (_ControllerInstances.TryGetValue(controllerRoute, out Dictionary<Type, DynamicUjmwControllerOptions> controllers)) {
              if (controllers != null) {
                lock (controllers) {
                  controllersOnThisRoute = controllers.ToArray();
                }
              }
            }
          }
        }

        List<Dictionary<string, object>> endpointMetadataEntries = new();
        if (controllersOnThisRoute != null) {
          foreach (KeyValuePair<Type, DynamicUjmwControllerOptions> controller in controllersOnThisRoute) {
            Dictionary<string, object> endpointMetadata = new();

            Type contract = controller.Key;
            string version = contract.Assembly.GetName()?.Version?.ToString(3);

            List<MethodInfo> allMethods = new();
            CollectAllMethodsForType(contract, allMethods);

            endpointMetadata["EndpointQualifyingName"] = $"UJMW:{contract.FullName}/{version}";
            endpointMetadata["ApiGroupName"] = controller.Value.ApiGroupName;
            //endpointMetadata["UJMW.ConcreteServiceInstanceTypeName"] = concreteServiceInstanceTypeName;
            endpointMetadata["UJMW.KnownMethods"] = allMethods.Where((mi)=> !mi.IsSpecialName).Select((mi) => mi.Name);

            if (UjmwHostConfiguration.EndpointInfoEnricher != null) {
              try {
                UjmwHostConfiguration.EndpointInfoEnricher.Invoke(contract, endpointMetadata);
              }
              catch (Exception ex) {
                DevLogger.LogError(ex);
              }
            }

            endpointMetadataEntries.Add(endpointMetadata);
          }
        }

        infoMetadata["ServiceEndpoints"] = endpointMetadataEntries;
        infoMetadata["EndpointIndexUrl"] = "";

        infoMetadata["Swagger.DefinitionUrls"] = Array.Empty<string>();
        infoMetadata["Swagger.UiEntryUrl"] = "";

        if(UjmwHostConfiguration.RouteInfoEnricher != null) {
          try {
            UjmwHostConfiguration.RouteInfoEnricher.Invoke(controllerRoute, infoMetadata);
          }
          catch (Exception ex) {
            DevLogger.LogError(ex);
          }
        }

        content = JsonConvert.SerializeObject(infoMetadata, Formatting.Indented);
        mimeType = "application/json";

        //old...
        //content = $"<html>\n  <head>\n    <title>{this.ContractType.Name} (UJMW-Endpoint)</title>\n  </head>\n  <body style=\"font-family: system-ui;\r\n    font-size: 12px;\"><h1>UJMW-Endpoint</h1>\n    <b>Contract:</b> {this.ContractType.FullName}<br>\n    <b>Instance:</b> {concreteServiceInstanceTypeName}\n  </body>\n</html>";
        //mimeType = "text/html";

      }

    }

    public partial class DynamicControllerBase<TServiceInterface> : DynamicControllerBase, IDisposable {

      protected Func<TServiceInterface> _ServiceInstanceGetter = null;

      public DynamicControllerBase(object injectedRootServiceInstance) {

        Type myDynamicControllerType = this.GetType();

        _Options = DynamicUjmwControllerFactory.GetDynamicUjmwControllerOptions(myDynamicControllerType);

        _ControllerRoutesFromAttribute = myDynamicControllerType.GetCustomAttributes().OfType<RouteAttribute>().Select(attr => attr.Template).ToArray();

        PropertyInfo[] navPath = _Options.SubServiceNavPath;

        Type rootType = typeof(TServiceInterface);
        if (navPath.Length > 0) {
          rootType = _Options.SubServiceNavPath[0].DeclaringType;
        }

        if (injectedRootServiceInstance == null) {
          throw new Exception($"The dynamic asp controller base requires to get an '{rootType.Name}' injected - but got null!");
        }

        if (navPath.Length == 0) {
          _ServiceInstanceGetter = () => (TServiceInterface)injectedRootServiceInstance;
        }
        else {
          _ServiceInstanceGetter = () => {
            object downNavigatedInstance = injectedRootServiceInstance;
            foreach (PropertyInfo navProp in navPath) {
              downNavigatedInstance = navProp.GetValue(downNavigatedInstance);
              if (downNavigatedInstance == null) {
                throw new NullReferenceException($"The dynamic asp controller got a null-reference during the 'sub-service' instance navigation when getting property value from '{navProp.DeclaringType}.{navProp.Name}'!");
              }
            }
            return (TServiceInterface)downNavigatedInstance;
          };
        }
    
        KeyValuePair<string, Func<object, string>>[] contextualArgumentGetters = _Options.GetUniformedContextualArgumentGetters(
          (headerName, requestDto) => this.HttpContext.Request.Headers[headerName].FirstOrDefault(),
          (routeSegmentAlias, requestDto) => this.RouteData.Values[routeSegmentAlias]?.ToString(),
          (dtoPropertyName, requestDto) => requestDto.GetType().GetProperty(dtoPropertyName)?.GetValue(requestDto)?.ToString()
        ).ToArray(); //NOTE: ToArray in combination with KVP ist used to freeze the collection and avoid threading issues!

        _ContextualArgumentCollector = (ref IDictionary<string, string> targetDict, string[] includedArgNames, object requestDto) => {
          if(includedArgNames == null) {
            return;
          }
          if(targetDict == null) {
            targetDict = new Dictionary<string, string>();
          }
          //invoke all getters and fill the target dict
          foreach (KeyValuePair<string, Func<object, string>> nameAndGetter in contextualArgumentGetters) {
            if (includedArgNames.Length > 0 && !includedArgNames.Contains(nameAndGetter.Key)) {
              continue;
            }
            try {
              targetDict[nameAndGetter.Key] = nameAndGetter.Value?.Invoke(requestDto);
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
      public override Type ContractType { 
        get {
          return typeof(TServiceInterface);
        }
      }

      public override Type ResolvedServiceImplementationType {
        get {
          return _ServiceInstanceGetter.Invoke()?.GetType();
        }   
      }

      //WILL BE INVOKED VIA EMITED CODE FROM DYNAMIC PROXY-FACADE WHIch IS INHERITING FROM US!
      //NOTE: only the first controller on a route will be used to render the info-site -
      //only this single one will provide a HTTP-GET method for a shared route. So will will have
      //the need to but all controllers on the same route will be able to handle the method invocations (and should behave the same regarding the info-site content, because they share the same contract and service instance)
      protected IActionResult RenderInfoSite() {
        GetRouteInfoResponse(_ControllerRoutesFromAttribute.FirstOrDefault(), out string content, out string mimeType);
        return Content(content, mimeType);
      }

      //WILL BE INVOKED VIA EMITED CODE FROM DYNAMIC PROXY-FACADE WHIch IS INHERITING FROM US!
      protected object InvokeMethod(string methodName, object requestDto) {
        try {

          MethodRedirector redirector = GetOrCreateRedirector(methodName, this);

          object responseDto = redirector.InvokeSynchronous(
            _ServiceInstanceGetter,
            requestDto,
            this.HttpContext.Request.Headers,
            this.HttpContext.Response.Headers,
            _Options,
            _ContextualArgumentCollector,
            this.HttpContext.RequestAborted
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

      /// <summary>
      /// Initiates a long-running operation (HTTP POST). Without the query parameter 'wait' the operation is
      /// executed synchronously (100% backward compatible), otherwise it is handed over to the operation store.
      /// WILL BE INVOKED VIA EMITED CODE FROM DYNAMIC PROXY-FACADE WHICH IS INHERITING FROM US!
      /// </summary>
      protected object InvokeLongRunningMethod(string methodName, object requestDto, string wait) {
        try {

          MethodRedirector redirector = GetOrCreateRedirector(methodName, this);

          TimeSpan waitTime;
          if (!TryParseWait(wait, out waitTime)) {

            return redirector.InvokeSynchronous(
              _ServiceInstanceGetter,
              requestDto,
              this.HttpContext.Request.Headers,
              this.HttpContext.Response.Headers,
              _Options,
              _ContextualArgumentCollector,
              this.HttpContext.RequestAborted
            );

          }

          return redirector.InitiateAsync(
            this.GetOperationScope(methodName),
            _ServiceInstanceGetter,
            requestDto,
            this.HttpContext.Request.Headers,
            this.HttpContext.Response.Headers,
            _Options,
            _ContextualArgumentCollector,
            waitTime
          );

        }
        catch (Exception ex) {
          //LAST DEFENCE - SHOULD NEVER HAPPEN
          DevLogger.LogCritical(ex.Wrap(72500, $"UJMW Operation '{methodName}' failed: {ex.Message}"));
          this.HttpContext.Response.StatusCode = 500;
          throw;
        }
      }

      /// <summary>
      /// Polls (HTTP GET) or requests the cancellation (HTTP DELETE) of a long-running operation.
      /// The regular UJMW auth hooks have already been evaluated (by the action filter) at this point,
      /// so only the 'pollingHash' verification is left (which is done by the operation store).
      /// WILL BE INVOKED VIA EMITED CODE FROM DYNAMIC PROXY-FACADE WHICH IS INHERITING FROM US!
      /// </summary>
      protected object InvokeAsyncControl(string methodName, int controlKind, string asyncId, string pollingSalt, string pollingHash, string wait) {
        try {

          MethodRedirector redirector = GetOrCreateRedirector(methodName, this);
          IUjmwOperationStore store = UjmwHostConfiguration.OperationStore;
          string operationScope = this.GetOperationScope(methodName);

          UjmwOperationSnapshot snapshot;
          bool authenticated;

          if (controlKind == (int)UjmwAsyncControlKind.Cancel) {

            TimeSpan waitTime;
            if (!TryParseWait(wait, out waitTime)) {
              waitTime = TimeSpan.Zero;
            }

            authenticated = store.TryCancel(operationScope, asyncId, pollingSalt, pollingHash, waitTime, out snapshot);

          }
          else {
            authenticated = store.TryPoll(operationScope, asyncId, pollingSalt, pollingHash, out snapshot);
          }

          if (!authenticated) {
            //no information about the existence of the operation must be exposed
            return redirector.CreateFaultResponse(UjmwAsyncProtocol._WrongPollingAuthFault);
          }

          return redirector.CreateResponseFromSnapshot(snapshot, this.HttpContext.Response.Headers);
        }
        catch (Exception ex) {
          //LAST DEFENCE - SHOULD NEVER HAPPEN
          DevLogger.LogCritical(ex.Wrap(72500, $"UJMW Operation '{methodName}' (async control) failed: {ex.Message}"));
          this.HttpContext.Response.StatusCode = 500;
          throw;
        }
      }

      /// <summary>
      /// Polls are only accepted for the same endpoint operation which has initiated the operation.
      /// </summary>
      private string GetOperationScope(string methodName) {
        return this.GetType().FullName + "." + methodName;
      }

      /// <summary>
      /// Parses the 'wait' query parameter (seconds, invariant culture). Returns false if not present or not parsable
      /// (which leads to a synchronous execution - the same behaviour as an older UJMW server).
      /// </summary>
      private static bool TryParseWait(string wait, out TimeSpan waitTime) {

        waitTime = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(wait)) {
          return false;
        }

        double seconds;
        if (!double.TryParse(wait, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) {
          return false;
        }

        if (seconds > _MaxWaitSeconds) {
          seconds = _MaxWaitSeconds;
        }

        if (seconds > 0) {
          waitTime = TimeSpan.FromSeconds(seconds);
        }

        return true;
      }

      private const double _MaxWaitSeconds = 3600;

      /// A Cache for the redirectors
      private static Dictionary<string, MethodRedirector> _Redirectors = new Dictionary<string, MethodRedirector>();

      private static MethodRedirector GetOrCreateRedirector(string methodName, DynamicControllerBase<TServiceInterface> controller) {

        lock (_Redirectors) {

          string uniqueMethodName = controller.GetType().FullName + "." + methodName;

          MethodRedirector redirector;
          if (_Redirectors.TryGetValue(uniqueMethodName, out redirector)) {
            return redirector;
          }

          //doing the expensive creation of the mapping code only once (on demand)
          redirector = new MethodRedirector(controller.GetType(), methodName);
          _Redirectors[uniqueMethodName] = redirector;

          return redirector;
        }

      }

    }

  }

}
