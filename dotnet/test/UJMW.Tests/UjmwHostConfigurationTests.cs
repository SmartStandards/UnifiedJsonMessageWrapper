using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Reflection;

namespace System.Web.UJMW {

  [TestClass]
  public class UjmwHostConfigurationTests {

    /// <summary>
    /// Resets the global evaluator after each test (static state).
    /// </summary>
    [TestCleanup]
    public void ResetEvaluator() {
      UjmwHostConfiguration.SetAuthHeaderEvaluator((AuthHeaderValidationMethod)null);
    }

    /// <summary>
    /// The placeholder must be replaced (every occurrence, case-sensitive, plain string replacement).
    /// </summary>
    [TestMethod]
    public void ResolveRequestBaseUrlPlaceholder_ReplacesAllOccurrencesCaseSensitive() {

      string result = UjmwHostConfiguration.ResolveRequestBaseUrlPlaceholder(
        "Bearer resource_metadata=\"{RequestBaseUrl}/.well-known/x\", a=\"{RequestBaseUrl}\", b=\"{requestbaseurl}\"",
        "https://host:8443/app"
      );

      Assert.AreEqual(
        "Bearer resource_metadata=\"https://host:8443/app/.well-known/x\", a=\"https://host:8443/app\", b=\"{requestbaseurl}\"",
        result
      );
      Assert.IsNull(UjmwHostConfiguration.ResolveRequestBaseUrlPlaceholder(null, "https://host"));
    }

    /// <summary>
    /// A legacy evaluator assigned via the obsolete property must be returned unchanged by its getter.
    /// </summary>
    [TestMethod]
    public void LegacyAuthHeaderEvaluator_RoundTripsUnchanged() {

      AuthHeaderEvaluatorMethod legacyEvaluator = (
        (string rawAuthHeader, Type contractType, MethodInfo targetContractMethod, string callingMachine, ref int httpReturnCode, ref string failedReason) => {
          return rawAuthHeader == "ok";
        }
      );

#pragma warning disable CS0618
      UjmwHostConfiguration.AuthHeaderEvaluator = legacyEvaluator;
      AuthHeaderEvaluatorMethod readBack = UjmwHostConfiguration.AuthHeaderEvaluator;
#pragma warning restore CS0618

      Assert.AreSame(legacyEvaluator, readBack);
    }

    /// <summary>
    /// A new-form evaluator must still be callable via the obsolete legacy property (reverse adapter).
    /// </summary>
    [TestMethod]
    public void NewFormAuthHeaderEvaluator_IsReachableViaLegacyProperty() {

      string receivedApiGroupName = "not-called";

      UjmwHostConfiguration.SetAuthHeaderEvaluator(
        (string rawAuthHeader, Type contractType, MethodInfo targetContractMethod, string apiGroupName, string callingMachine, ref int httpReturnCode, ref string failedReason, ref KeyValuePair<string, string>[] responseHeaders) => {
          receivedApiGroupName = apiGroupName;
          if (rawAuthHeader != "ok") {
            httpReturnCode = 401;
            failedReason = "denied";
            return false;
          }
          return true;
        }
      );

#pragma warning disable CS0618
      AuthHeaderEvaluatorMethod legacyView = UjmwHostConfiguration.AuthHeaderEvaluator;
#pragma warning restore CS0618

      Assert.IsNotNull(legacyView);

      int returnCode = 200;
      string reason = string.Empty;
      bool result = legacyView.Invoke("wrong", typeof(object), null, "machine", ref returnCode, ref reason);

      Assert.IsFalse(result);
      Assert.AreEqual(401, returnCode);
      Assert.AreEqual("denied", reason);
      Assert.AreEqual(string.Empty, receivedApiGroupName);
    }

#if NET5
    /// <summary>
    /// ASP.NET Core: the new-form evaluator must receive the API group name declared via
    /// '[ApiExplorerSettings(GroupName)]' on the controller, and the returned response headers must be
    /// written with the '{RequestBaseUrl}' placeholder resolved (also for rejected requests).
    /// </summary>
    [TestMethod]
    public void AuthHeaderInterceptor_PassesApiGroupNameAndWritesResolvedResponseHeaders() {

      string receivedApiGroupName = null;

      UjmwHostConfiguration.SetAuthHeaderEvaluator(
        (string rawAuthHeader, Type contractType, MethodInfo targetContractMethod, string apiGroupName, string callingMachine, ref int httpReturnCode, ref string failedReason, ref KeyValuePair<string, string>[] responseHeaders) => {
          receivedApiGroupName = apiGroupName;
          responseHeaders = new KeyValuePair<string, string>[] {
            new KeyValuePair<string, string>("WWW-Authenticate", "Bearer resource_metadata=\"{RequestBaseUrl}/.well-known/oauth-protected-resource\"")
          };
          httpReturnCode = 401;
          return false;
        }
      );

      Microsoft.AspNetCore.Http.DefaultHttpContext httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
      httpContext.Request.Scheme = "https";
      httpContext.Request.Host = new Microsoft.AspNetCore.Http.HostString("myhost:8443");
      httpContext.Request.PathBase = new Microsoft.AspNetCore.Http.PathString("/app");
      httpContext.Request.Method = "POST";

      Microsoft.AspNetCore.Routing.RouteData routeData = new Microsoft.AspNetCore.Routing.RouteData();
      routeData.Values["controller"] = nameof(GroupedTestController);
      routeData.Values["action"] = nameof(GroupedTestController.DoSomething);

      Microsoft.AspNetCore.Mvc.ActionContext actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
        httpContext, routeData, new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()
      );
      Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext executingContext = new Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext(
        actionContext,
        new List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>(),
        new Dictionary<string, object>(),
        new GroupedTestController()
      );

      bool nextCalled = false;
      AuthHeaderInterceptorAttribute interceptor = new AuthHeaderInterceptorAttribute();
      interceptor.OnActionExecutionAsync(
        executingContext,
        () => {
          nextCalled = true;
          return Threading.Tasks.Task.FromResult<Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext>(null);
        }
      ).GetAwaiter().GetResult();

      Assert.AreEqual("X", receivedApiGroupName);
      Assert.IsFalse(nextCalled);

      Microsoft.AspNetCore.Mvc.ContentResult result = executingContext.Result as Microsoft.AspNetCore.Mvc.ContentResult;
      Assert.IsNotNull(result);
      Assert.AreEqual(401, result.StatusCode);

      Assert.AreEqual(
        "Bearer resource_metadata=\"https://myhost:8443/app/.well-known/oauth-protected-resource\"",
        httpContext.Response.Headers["WWW-Authenticate"].ToString()
      );
    }

    [Microsoft.AspNetCore.Mvc.ApiExplorerSettings(GroupName = "X")]
    public class GroupedTestController {

      /// <summary>
      /// Dummy action (only used to resolve the called contract method).
      /// </summary>
      public void DoSomething() {
      }

    }
#endif

#if NET46
    /// <summary>
    /// WCF: the API group name
    /// (trim '/', remove trailing '.svc' case-insensitive, '/' -> '-', casing preserved, "" if unknown).
    /// </summary>
    [TestMethod]
    public void DeriveApiGroupNameFromRelativeEndpointPath_FollowsWcfNamingRule() {
      Assert.AreEqual("api-v1-Calendar", UjmwHostConfiguration.DeriveApiGroupNameFromRelativeEndpointPath("/api/v1/Calendar.svc"));
      Assert.AreEqual("api-v1-Calendar", UjmwHostConfiguration.DeriveApiGroupNameFromRelativeEndpointPath("api/v1/Calendar.SVC/"));
      Assert.AreEqual("MyService", UjmwHostConfiguration.DeriveApiGroupNameFromRelativeEndpointPath("/MyService.svc"));
      Assert.AreEqual(string.Empty, UjmwHostConfiguration.DeriveApiGroupNameFromRelativeEndpointPath(null));
      Assert.AreEqual(string.Empty, UjmwHostConfiguration.DeriveApiGroupNameFromRelativeEndpointPath("/"));
    }
#endif

  }

}
