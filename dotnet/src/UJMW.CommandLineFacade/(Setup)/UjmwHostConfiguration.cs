using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System;

[assembly: AssemblyMetadata("SourceContext", "UJMW")]

namespace UJMW.CommandLineFacade {

  public delegate bool ServiceContractInterfaceSelectorMethod(
    Type serviceImplementationType,
    string url,
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
  /// Evaluates the HTTP 'Authorization' header of an incoming UJMW call (currently without effect in this host).
  /// </summary>
  /// <param name="rawAuthHeader">The raw value of the HTTP 'Authorization' header (null if missing).</param>
  /// <param name="contractType">The service contract interface type of the called endpoint.</param>
  /// <param name="targetContractMethod">The called contract method.</param>
  /// <param name="apiGroupName">A logical name of the called endpoint, an empty string (never null) if unknown.</param>
  /// <param name="callingMachine">The address of the calling machine.</param>
  /// <param name="httpReturnCode">The HTTP status code to be returned when rejecting (200 = use the default 403).</param>
  /// <param name="failedReason">A reason which will be returned when rejecting.</param>
  /// <param name="responseHeaders">
  /// Headers to be written into the response. Initialized with an empty array, never null.
  /// OFFICIAL CONVENTION (binding for both sides): header values may contain the literal placeholder '{RequestBaseUrl}',
  /// which a host replaces (plain, case-sensitive) with the base URL of its own current request
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

  /// <summary> (specific to ASP.net core WebAPI) </summary>
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

    #region " AuthHeaderEvaluator (currently without effect in this host) "

    /// <summary>
    /// The one and only storage for the evaluator (always in the new form - legacy delegates are stored wrapped).
    /// </summary>
    private static AuthHeaderValidationMethod _AuthHeaderEvaluator = null;

    /// <summary>
    /// Registers the evaluator for the HTTP 'Authorization' header (currently without effect in this host).
    /// OFFICIAL CONVENTION: the values of returned response headers may contain the literal placeholder '{RequestBaseUrl}',
    /// which a host replaces with the base URL of its current request before writing the header.
    /// </summary>
    /// <param name="authHeaderEvaluator">The evaluator (null to disable the evaluation).</param>
    public static void SetAuthHeaderEvaluator(AuthHeaderValidationMethod authHeaderEvaluator) {
      _AuthHeaderEvaluator = authHeaderEvaluator;
    }

    /// <summary>
    /// Registers a LEGACY evaluator for the HTTP 'Authorization' header (currently without effect in this host).
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
    /// LEGACY access to the evaluator for the HTTP 'Authorization' header (currently without effect in this host).
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
    /// this is just a convenience method for calling 'ConfigureRequestSidechannel' and setting up only the UJMW '_'-property
    /// </summary>
    /// <param name="processingMethod"></param>
    public static void ConfigureStandardUjmwRequestSidechannel(RequestSidechannelProcessingMethod processingMethod) {
      ConfigureRequestSidechannel((t,sc) => {
        sc.AcceptUjmwUnderlineProperty();
        sc.ProcessDataVia(processingMethod);
      });
    }

    public static void ConfigureRequestSidechannel(IncommingRequestSideChannelConfigurationMethod requestSideChannelConfigurator) {
      _RequestSideChannelConfigurator = requestSideChannelConfigurator;
    }

    public static void ConfigureResponseSidechannel(OutgoingResponseSideChannelConfigurationMethod responseSideChannelConfigurator ) {
      _ResponseSideChannelConfigurator = responseSideChannelConfigurator;
    }

    internal static IncommingRequestSideChannelConfiguration GetRequestSideChannelConfiguration(Type contractType) {
      var cfg = new IncommingRequestSideChannelConfiguration();
      if(_RequestSideChannelConfigurator != null) {
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
        if(cfg.ChannelsToProvide == null) {
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

    /// <summary>
    /// will be invoked for exceptions that have been thrown during host creation (when WCF is using our factory)
    /// </summary>
    public static Action<Exception> FactoryExceptionVisitor { get; set; } = (ex) => Trace.TraceError(ex.Message);

    //UNDER DEVELOPMENT...
    //https://stackoverflow.com/questions/17961564/wcf-exception-handling-using-ierrorhandler
    //https://www.c-sharpcorner.com/UploadFile/b182bf/centralize-exception-handling-in-wcf-part-10/
    //public static Action<MethodInfo,Exception> BlExceptionHandler { get; set; } = null;

    public static bool HideExeptionMessageInFaultProperty { get; set; } = false;

    /// <summary>
    /// EXPERIMENTAL: generate all proxy-classes in only one shared dynamic assembly to reduce memory footprint
    /// </summary>
    public static bool UseCombinedDynamicAssembly = false;

  }

}
