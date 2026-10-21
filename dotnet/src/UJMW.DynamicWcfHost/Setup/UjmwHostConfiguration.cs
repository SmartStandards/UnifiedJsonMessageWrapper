using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("UJMW.Tests")]
[assembly: AssemblyMetadata("SourceContext", "UJMW")]

namespace System.Web.UJMW {

  public delegate bool ServiceContractInterfaceSelectorMethod(
    Type serviceImplementationType,
    string relativeAddress,
    out Type serviceContractInterfaceType
  );

  /// <summary>
  /// LEGACY form of the auth header evaluator (please use 'AuthHeaderValidationMethod' instead).
  /// </summary>
  [Obsolete("Please use 'AuthHeaderValidationMethod' instead (the new form supports the API group name and response headers).")]
  public delegate bool AuthHeaderEvaluatorMethod(
    string rawAuthHeader,
    Type contractType,
    MethodInfo calledContractMethod,
    string callingMachine,
    ref int httpReturnCode,
    ref string failedReason
  );

  /// <summary>
  /// Evaluates the HTTP 'Authorization' header of an incoming UJMW call.
  /// Register an implementation via 'UjmwHostConfiguration.SetAuthHeaderEvaluator(...)'.
  /// </summary>
  /// <param name="rawAuthHeader">The raw value of the HTTP 'Authorization' header (null if missing).</param>
  /// <param name="contractType">The service contract interface type of the called endpoint.</param>
  /// <param name="targetContractMethod">The called contract method.</param>
  /// <param name="apiGroupName">
  /// A logical name of the called endpoint. WCF has no API groups, so it is derived from the relative endpoint path
  /// of the service activation: leading and trailing '/' trimmed, a trailing '.svc' removed (case-insensitive),
  /// '/' replaced with '-', casing preserved (example: '/api/v1/Calendar.svc' -> 'api-v1-Calendar').
  /// An empty string (never null) is passed, if no logical name can be determined.
  /// </param>
  /// <param name="callingMachine">The address of the calling machine.</param>
  /// <param name="httpReturnCode">The HTTP status code to be returned when rejecting (200 = use the default 403).</param>
  /// <param name="failedReason">A reason which will be returned when rejecting.</param>
  /// <param name="responseHeaders">
  /// Headers to be written into the HTTP response (when accepting AND when rejecting, e.g. 'WWW-Authenticate').
  /// Initialized with an empty array, never null. Preset values must be passed on when chaining evaluators.
  /// OFFICIAL CONVENTION (binding for both sides): header values may contain the literal placeholder '{RequestBaseUrl}'.
  /// Every UJMW host replaces each occurrence (plain, case-sensitive) with the base URL of its own current request
  /// ('scheme://host[:port]' plus path base, without trailing slash) before writing the header.
  /// </param>
  /// <returns>true, if the call is authorized</returns>
  public delegate bool AuthHeaderValidationMethod(
    string rawAuthHeader,
    Type contractType,
    MethodInfo targetContractMethod,
    string apiGroupName,
    string callingMachine,
    ref int httpReturnCode,
    ref string failedReason,
    ref KeyValuePair<string, string>[] responseHeaders
  );

  public delegate void ArgumentPreEvaluatorMethod(
    Type contractType,
    MethodInfo calledContractMethod,
    object[] arguments
  );

  /// <summary> (specific to WCF) </summary>
  public class UjmwHostConfiguration {

    private UjmwHostConfiguration() {
    }

    public delegate void IncommingRequestSideChannelConfigurationMethod(
      Type serviceContractInterfaceType,
      IncommingRequestSideChannelConfiguration sideChannel
    );

    public delegate void OutgoingResponseSideChannelConfigurationMethod(
      Type serviceContractInterfaceType,
      OutgoingResponseSideChannelConfiguration sideChannel
    );

    private static IncommingRequestSideChannelConfigurationMethod _RequestSideChannelConfigurator { get; set; } = null;
    private static OutgoingResponseSideChannelConfigurationMethod _ResponseSideChannelConfigurator { get; set; } = null;
  
    public static Action<ServiceModel.WebHttpBinding> HttpBindingCustomizingHook { get; set; } = null;

    /// <summary>
    /// this is just a convenience method for calling 'ConfigureRequestSidechannel' and setting up only the UJMW '_'-property
    /// </summary>
    /// <param name="processingMethod"></param>
    public static void ConfigureStandardUjmwRequestSidechannel(RequestSidechannelProcessingMethod processingMethod) {
      ConfigureRequestSidechannel((t, sc) => {
        sc.AcceptUjmwUnderlineProperty();
        sc.ProcessDataVia(processingMethod);
      });
    }

    public static void ConfigureRequestSidechannel(IncommingRequestSideChannelConfigurationMethod requestSideChannelConfigurator) {
      _RequestSideChannelConfigurator = requestSideChannelConfigurator;
    }

    public static void ConfigureResponseSidechannel(OutgoingResponseSideChannelConfigurationMethod responseSideChannelConfigurator) {
      _ResponseSideChannelConfigurator = responseSideChannelConfigurator;
    }

    internal static IncommingRequestSideChannelConfiguration GetRequestSideChannelConfiguration(Type contractType) {
      var cfg = new IncommingRequestSideChannelConfiguration();
      if (_RequestSideChannelConfigurator != null) {
        _RequestSideChannelConfigurator.Invoke(contractType, cfg);
        if (cfg.AcceptedChannels == null) {
          throw new Exception("When configuring the SideChannel, you need to call 'AcceptNoChannelProvided()' or another 'Accept...' method explicitely!");
        }
        if (cfg.ProcessingMethod == null && (cfg.AcceptedChannels.Count() > 0 || cfg.DefaultsGetterOnSkip != null)) {
          throw new Exception("The given SideChannel configuration requires a delegate method to process incomming data! Please use 'ProcessDataVia(...)' in your setup to specify one.");
        }
      }
      else {
        cfg.AcceptNoChannelProvided();
      }
      cfg.MakeImmutable();
      return cfg;
    }

    internal static OutgoingResponseSideChannelConfiguration GetResponseSideChannelConfiguration(Type contractType) {
      var cfg = new OutgoingResponseSideChannelConfiguration();
      if (_ResponseSideChannelConfigurator != null) {
        _ResponseSideChannelConfigurator.Invoke(contractType, cfg);
        if (cfg.ChannelsToProvide == null) {
          throw new Exception("When configuring the SideChannel, you need to call 'ProvideNoChannel()' or another 'Provide...' method explicitely!");
        }
        if (cfg.CaptureMethod == null && (cfg.ChannelsToProvide.Count() > 0)) {
          throw new Exception("The given SideChannel configuration requires a delegate method to capture outgoing data! Please use 'CaptureDataVia(...)' in your setup to specify one.");
        }
      }
      else {
        cfg.ProvideNoChannel();
      }
      cfg.MakeImmutable();
      return cfg;
    }

    public static ServiceContractInterfaceSelectorMethod ContractSelector { get; set; } = (
      (Type serviceImplementationType, string relativeAddress, out Type serviceContractInterfaceType) => {

        Type[] allInterfaces = serviceImplementationType.GetInterfaces().Where(
         (i) => (i != typeof(IDisposable))
        ).ToArray();

        if (allInterfaces.Length == 0) {
          //use the concrete impl. type only if there is relly no interface implemented
          serviceContractInterfaceType = serviceImplementationType;
          return false;
        }

        Type[] contractInterfaces = allInterfaces.Where(
          (i) => i.GetCustomAttributes(true).Where((a) => a.GetType() == typeof(System.ServiceModel.ServiceContractAttribute)).Any()
        ).ToArray();

        if(contractInterfaces.Length == 1) {
          serviceContractInterfaceType = contractInterfaces[0];
          return true;
        }
        if(contractInterfaces.Length == 0) {
          //otherwise use the first interface (without a 'ServiceContractAttribute')
          serviceContractInterfaceType = allInterfaces.First();
          return false;
        }

        string[] urlTokens = relativeAddress.Split('/');
        string endpointNameInUrl = urlTokens[urlTokens.Length - 1];
        if (endpointNameInUrl.EndsWith(".svc")) {
          endpointNameInUrl = endpointNameInUrl.Substring(0, endpointNameInUrl.Length - 4);
        }

        Type[] contractInterfacesWithNameMatch = contractInterfaces.Where(
          (i) => i.Name.Equals(endpointNameInUrl, StringComparison.CurrentCultureIgnoreCase) ||
                 i.Name.Equals("I" + endpointNameInUrl, StringComparison.CurrentCultureIgnoreCase)
        ).ToArray();

        if(contractInterfacesWithNameMatch.Length == 1) {
          serviceContractInterfaceType = contractInterfacesWithNameMatch[0];
          return true;
        }

        string versionFromUrl = null;
        for (int i = urlTokens.Length - 1; i > 0; i--) {
          if (Regex.IsMatch(urlTokens[i], "^([vV][0-9]{1,})$")) {
            versionFromUrl = urlTokens[i].ToLower();
            break;
          }
        }

        //if versioned services are used
        if (versionFromUrl != null) {
          if (contractInterfacesWithNameMatch.Length > 0) {
            //prefer that interfaces with name-match
            var versionMatchingInterface = contractInterfacesWithNameMatch.Where((i) => (i.Namespace.ToLower() + ".").Contains(versionFromUrl + ".")).FirstOrDefault();
            if (versionMatchingInterface != null) {
              serviceContractInterfaceType = versionMatchingInterface;
              return true;
            }
          }
          else {
            //fallback to interfaces without name-match
            var versionMatchingInterface = contractInterfaces.Where((i) => (i.Namespace.ToLower() + ".").Contains(versionFromUrl + ".")).FirstOrDefault();
            if (versionMatchingInterface != null) {
              serviceContractInterfaceType = versionMatchingInterface;
              return true;
            }
          }
        }

        //noversioning needed
        if (contractInterfacesWithNameMatch.Length > 0) {
          serviceContractInterfaceType = contractInterfacesWithNameMatch[0];
        }
        else {
          serviceContractInterfaceType = contractInterfaces[0];
        }
        return true;
      }
    );

    #region " AuthHeaderEvaluator "

    /// <summary>
    /// The literal placeholder, which is allowed within the values of response headers returned by the AuthHeaderEvaluator.
    /// </summary>
    private const string _RequestBaseUrlPlaceholder = "{RequestBaseUrl}";

    /// <summary>
    /// The one and only storage for the evaluator (always in the new form - legacy delegates are stored wrapped).
    /// </summary>
    private static AuthHeaderValidationMethod _AuthHeaderEvaluator = null;

    /// <summary>
    /// Registers the evaluator for the HTTP 'Authorization' header of incoming calls.
    /// The evaluator receives a logical name (derived from the endpoint path) and can return response headers.
    /// OFFICIAL CONVENTION: the values of returned response headers may contain the literal placeholder '{RequestBaseUrl}',
    /// which will be replaced with the base URL of the current request ('scheme://host[:port]' plus application
    /// virtual path, without trailing slash) before the header is written.
    /// </summary>
    /// <param name="authHeaderEvaluator">The evaluator (null to disable the evaluation).</param>
    public static void SetAuthHeaderEvaluator(AuthHeaderValidationMethod authHeaderEvaluator) {
      _AuthHeaderEvaluator = authHeaderEvaluator;
    }

    /// <summary>
    /// Registers a LEGACY evaluator for the HTTP 'Authorization' header of incoming calls
    /// (it will not receive the logical name and cannot return response headers).
    /// </summary>
    /// <param name="legacyAuthHeaderEvaluator">The evaluator (null to disable the evaluation).</param>
    [Obsolete("Please use the overload accepting an 'AuthHeaderValidationMethod' instead (the new form supports the API group name and response headers).")]
    public static void SetAuthHeaderEvaluator(AuthHeaderEvaluatorMethod legacyAuthHeaderEvaluator) {
      if (legacyAuthHeaderEvaluator == null) {
        _AuthHeaderEvaluator = null;
        return;
      }
      LegacyAuthHeaderEvaluatorAdapter adapter = new LegacyAuthHeaderEvaluatorAdapter(legacyAuthHeaderEvaluator);
      _AuthHeaderEvaluator = adapter.Invoke;
    }

    /// <summary>
    /// LEGACY access to the evaluator for the HTTP 'Authorization' header.
    /// The getter returns the originally assigned legacy delegate, or (when the new form was registered)
    /// a wrapper which passes an empty logical name and drops the response headers.
    /// </summary>
    [Obsolete("Please use 'SetAuthHeaderEvaluator(...)' instead (the new 'AuthHeaderValidationMethod' form supports the API group name and response headers).")]
    public static AuthHeaderEvaluatorMethod AuthHeaderEvaluator {
      get {
        AuthHeaderValidationMethod currentEvaluator = _AuthHeaderEvaluator;
        if (currentEvaluator == null) {
          return null;
        }
        LegacyAuthHeaderEvaluatorAdapter legacyAdapter = currentEvaluator.Target as LegacyAuthHeaderEvaluatorAdapter;
        if (legacyAdapter != null) {
          return legacyAdapter.LegacyAuthHeaderEvaluator;
        }
        ReverseAuthHeaderEvaluatorAdapter reverseAdapter = new ReverseAuthHeaderEvaluatorAdapter(currentEvaluator);
        return reverseAdapter.Invoke;
      }
      set {
        SetAuthHeaderEvaluator(value);
      }
    }

    /// <summary>
    /// The currently registered evaluator (in the new form) - for internal usage by the host.
    /// </summary>
    internal static AuthHeaderValidationMethod CurrentAuthHeaderEvaluator {
      get {
        return _AuthHeaderEvaluator;
      }
    }

    /// <summary>
    /// Replaces every occurrence of the literal placeholder '{RequestBaseUrl}' (plain, case-sensitive)
    /// within the given header value with the given base URL of the current request.
    /// </summary>
    /// <param name="headerValue">The header value which may contain the placeholder.</param>
    /// <param name="requestBaseUrl">'scheme://host[:port]' plus path base, without trailing slash.</param>
    /// <returns>The header value with the placeholder replaced.</returns>
    public static string ResolveRequestBaseUrlPlaceholder(string headerValue, string requestBaseUrl) {
      if (headerValue == null) {
        return null;
      }
      if (requestBaseUrl == null) {
        requestBaseUrl = string.Empty;
      }
      return headerValue.Replace(_RequestBaseUrlPlaceholder, requestBaseUrl);
    }

    /// <summary>
    /// Derives the logical name (passed as 'apiGroupName' to the AuthHeaderEvaluator) from the relative endpoint path
    /// of the service activation: leading and trailing '/' trimmed, a trailing '.svc' removed (case-insensitive),
    /// '/' replaced with '-', casing preserved (example: '/api/v1/Calendar.svc' -> 'api-v1-Calendar').
    /// </summary>
    /// <param name="relativeEndpointPath">The endpoint path relative to the application.</param>
    /// <returns>The logical name or an empty string, if no name can be derived.</returns>
    internal static string DeriveApiGroupNameFromRelativeEndpointPath(string relativeEndpointPath) {
      if (string.IsNullOrWhiteSpace(relativeEndpointPath)) {
        return string.Empty;
      }
      string apiGroupName = relativeEndpointPath.Trim('/');
      if (apiGroupName.EndsWith(".svc", StringComparison.OrdinalIgnoreCase)) {
        apiGroupName = apiGroupName.Substring(0, apiGroupName.Length - 4);
        apiGroupName = apiGroupName.TrimEnd('/');
      }
      return apiGroupName.Replace('/', '-');
    }

    /// <summary>
    /// Wraps a legacy evaluator into the new form.
    /// </summary>
    [Obsolete("Only used to support the legacy 'AuthHeaderEvaluatorMethod'.")]
    private sealed class LegacyAuthHeaderEvaluatorAdapter {

      private AuthHeaderEvaluatorMethod _LegacyAuthHeaderEvaluator;

      public LegacyAuthHeaderEvaluatorAdapter(AuthHeaderEvaluatorMethod legacyAuthHeaderEvaluator) {
        _LegacyAuthHeaderEvaluator = legacyAuthHeaderEvaluator;
      }

      public AuthHeaderEvaluatorMethod LegacyAuthHeaderEvaluator {
        get {
          return _LegacyAuthHeaderEvaluator;
        }
      }

      /// <summary>
      /// Invokes the legacy evaluator (the logical name and the response headers are not supported by it).
      /// </summary>
      public bool Invoke(
        string rawAuthHeader, Type contractType, MethodInfo targetContractMethod, string apiGroupName,
        string callingMachine, ref int httpReturnCode, ref string failedReason,
        ref KeyValuePair<string, string>[] responseHeaders
      ) {
        return _LegacyAuthHeaderEvaluator.Invoke(
          rawAuthHeader, contractType, targetContractMethod, callingMachine, ref httpReturnCode, ref failedReason
        );
      }

    }

    /// <summary>
    /// Wraps an evaluator of the new form into the legacy form (to keep chained legacy hooks working).
    /// </summary>
    private sealed class ReverseAuthHeaderEvaluatorAdapter {

      private AuthHeaderValidationMethod _AuthHeaderEvaluator;

      public ReverseAuthHeaderEvaluatorAdapter(AuthHeaderValidationMethod authHeaderEvaluator) {
        _AuthHeaderEvaluator = authHeaderEvaluator;
      }

      /// <summary>
      /// Invokes the evaluator of the new form with an empty logical name and drops the response headers.
      /// </summary>
      public bool Invoke(
        string rawAuthHeader, Type contractType, MethodInfo calledContractMethod,
        string callingMachine, ref int httpReturnCode, ref string failedReason
      ) {
        KeyValuePair<string, string>[] droppedResponseHeaders = new KeyValuePair<string, string>[0];
        return _AuthHeaderEvaluator.Invoke(
          rawAuthHeader, contractType, calledContractMethod, string.Empty,
          callingMachine, ref httpReturnCode, ref failedReason, ref droppedResponseHeaders
        );
      }

    }

    #endregion

    public static ArgumentPreEvaluatorMethod ArgumentPreEvaluator { get; set; } = null;

    public static bool ForceHttps { get; set; } = false;

    /// <summary>
    /// A convenience method to quickly setup cors in this way:
    ///   CorsAllowOrigin="{origin}" /
    ///   CorsAllowMethod="POST,OPTIONS" /
    ///   CorsAllowHeaders="*"
    /// </summary>
    public static void EnableCorsGeneric() {
      CorsAllowOrigin = "{origin}";
      CorsAllowMethod = "POST,OPTIONS";
      CorsAllowHeaders = "*";
    }

    /// <summary>
    /// If set to null (which is the default), then NO header will be written.
    /// To enable the "Access-Control-Allow-Origin" header, you can set it to "*"
    /// (or to "{origin}" to use the origin from the request!)
    /// </summary>
    public static string CorsAllowOrigin { get; set; } = null;

    /// <summary>
    /// If set to null (which is the default), then NO header will be written.
    /// To enable the "Access-Control-Allow-Method" header, you can set it to "POST,OPTIONS".
    /// </summary>
    public static string CorsAllowMethod { get; set; } = null;

    /// <summary>
    /// If set to null (which is the default), then NO header will be written.
    /// To enable the "Access-Control-Allow-Headers" header, you can set it to "*".
    /// (or for example 'X-Requested-With,Content-Type')
    /// </summary>
    public static string CorsAllowHeaders { get; set; } = null;

    /// <summary>
    /// Usually WCF requires at least any authentication schema.
    /// Disableing this is only recommended when you're using any
    /// other secirity framework instead (may-be access-token based).
    /// Please be aware, that working without the build-in wcf security features
    /// could require to enable 'Anonymous' authentication for the Web-Application... 
    /// </summary>
    public static bool RequireNtlm { get; set; } = true;

    // SEMAPHORES against WCF multithreading problems: sometimes the
    // custom IHttpModules (as configured within the web.config)
    // will be invoked to late caused by multi-threading. Therefore
    // we will need to wait a little bit, to give time to any
    // potential exisiting external code to do the configuration...
    internal static DateTime _SetupCompletedTime;

    static UjmwHostConfiguration(){
      if(FileBasedSettings.GracetimeForSetupPhase < 0) {
        _SetupCompletedTime = DateTime.MaxValue; //wait forever until 'SetupCompleted' has been called
      }
      else {
        _SetupCompletedTime = DateTime.Now.AddSeconds(FileBasedSettings.GracetimeForSetupPhase);
      }
    }

    internal static void WaitForSetupCompleted() {
      while (DateTime.Now < _SetupCompletedTime) {
        Threading.Thread.Sleep(100);
      }
    }

    /// <summary>
    /// Call this, if youre finished your adjustments to the UjmwHostConfiguration.
    /// Otherwise the ServiceHosts will not be created until a wait-time names 'SetupDelaySeconds'
    /// (can be modified via web.config)
    /// seconds are expired!
    /// </summary>
    public static void SetupCompleted() {
      _SetupCompletedTime = DateTime.Now;
    }

    /// <summary>
    /// will be invoked for exceptions that have been thrown during host creation (when WCF is using our factory)
    /// </summary>
    public static Action<Exception> FactoryExceptionVisitor { get; set; } = (ex) => Trace.TraceError(ex.Message);
    
    public static bool HideExeptionMessageInFaultProperty { get; set; } = false;

    /// <summary>
    /// Enables a trace message (EventId 72000) for every incomming request (default: false, to avoid log spam).
    /// </summary>
    public static bool EnableTraceMessagesForEveryIncommingRequest { get; set; } = false;

  }

}
