using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;

//using System.Runtime.Remoting.Messaging;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("UJMW.Tests")]

namespace System.Web.UJMW {

  public class UjmwClientConfiguration {

    private UjmwClientConfiguration() {
    }

    public delegate void OutgoingRequestSideChannelConfigurationMethod(
      Type serviceContractInterfaceType,
      OutgoingRequestSideChannelConfiguration sideChannel
    );

    public delegate void IncommingResponseBackChannelConfigurationMethod(
      Type serviceContractInterfaceType,
      IncommingResponseSideChannelConfiguration sideChannel
    );

    /// <summary>
    /// will be invoked for exceptions that have been thrown during host creation (when WCF is using our factory)
    /// </summary>
    public static Action<Exception> FactoryExceptionVisitor { get; set; } = (ex)=> Trace.TraceError(ex.Message);

    public delegate void FaultRepsonseHandlerMethod(
     string fullUrl, MethodInfo method, string faultMessage
    );

    public delegate string AuthHeaderGetterMethod(
      Type contractType
    );
    public static AuthHeaderGetterMethod DefaultAuthHeaderGetter { get; set; } = (t)=> null;

    /// <summary>
    /// When using a long living client instance,
    /// then this value is used to control, when the given getter will be invoked again.
    /// Default is 20sec.
    /// </summary>
    public static int AuthHeaderGetterCacheSec = 20;

    public delegate string UrlGetterMethod(
      Type contractType
    );
    public static UrlGetterMethod DefaultUrlGetter { get; set; } = (
      (t)=> throw new ApplicationException($"To initialize a DynamicUjmwClient whitout specifying an url explicitely, the '{nameof(UjmwClientConfiguration)}.{nameof(UjmwClientConfiguration.DefaultUrlGetter)}' must be initialized instead!")
    );

    /// <summary>
    /// When using a long living client instance,
    /// then this value is used to control, when the given getter will be invoked again.
    /// Default is 60sec.
    /// </summary>
    public static int UrlGetterCacheSec = 60;

    /// <summary>
    /// Returns true, if another attempt should me made!
    /// An sleep of 100ms is hardcoded internally, more can be placed inside of that hook.
    /// IMPORTANT: there is a hard limit of max 20 retries!
    /// </summary>
    /// <param name="contractType"></param>
    /// <param name="ex"></param>
    /// <param name="tryNumber"></param>
    /// <param name="url">NOTE: the url can also be modified sothat a fallback-url is used for the next try...</param>
    /// <returns></returns>
    public delegate bool RetryDecitionMethod(
      Type contractType,
      Exception ex,
      int tryNumber,
      ref string url
    );

    /// <summary>
    /// Returns true, if another attempt should me made!
    /// An sleep of 100ms is hardcoded internally, more can be placed inside of that hook.
    /// IMPORTANT: there is a hard limit of max 20 retries!
    /// </summary>
    public static RetryDecitionMethod RetryDecider { get; set; } = (
      (Type contractType, Exception ex, int tryNumber, ref string url) => { 
        if (ex is TimeoutException || ex.Message.Contains("Timeout")) {
          return (tryNumber == 1);//one retry per default...
        }
        return false;
      }
    );

    /// <summary>
    /// will be invoked for exceptions that have been thrown during host creation (when WCF is using our factory)
    /// </summary>
    public static FaultRepsonseHandlerMethod FaultRepsonseHandler { get; set; } = (
      (string fullUrl, MethodInfo method, string faultMessage) => throw new UjmwFaultException(fullUrl, method, faultMessage)
    );

    private static OutgoingRequestSideChannelConfigurationMethod _RequestSideChannelConfigurator { get; set; } = null;
    private static IncommingResponseBackChannelConfigurationMethod _ResponseBackChannelConfigurator { get; set; } = null;

    /// <summary>
    /// this is just a convenience method for calling 'ConfigureRequestSidechannel' and setting up only the UJMW '_'-property
    /// </summary>
    /// <param name="captureMethod"></param>
    public static void ConfigureStandardUjmwRequestSidechannel(Action<IDictionary<string, string>> captureMethod) {
      ConfigureRequestSidechannel((t,sc) => {
        sc.ProvideUjmwUnderlineProperty();
        sc.CaptureDataVia(captureMethod);
      });
    }

    public static void ConfigureRequestSidechannel(OutgoingRequestSideChannelConfigurationMethod requestSideChannelConfigurator) {
      _RequestSideChannelConfigurator = requestSideChannelConfigurator;
    }

    public static void ConfigureResponseBackchannel(IncommingResponseBackChannelConfigurationMethod responseBackChannelConfigurator ) {
      _ResponseBackChannelConfigurator = responseBackChannelConfigurator;
    }

    internal static OutgoingRequestSideChannelConfiguration GetRequestSideChannelConfiguration(Type contractType) {
      var cfg = new OutgoingRequestSideChannelConfiguration();
      if (_RequestSideChannelConfigurator != null) {
        _RequestSideChannelConfigurator.Invoke(contractType, cfg);
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

    internal static IncommingResponseSideChannelConfiguration GetResponseSideChannelConfiguration(Type contractType) {
      var cfg = new IncommingResponseSideChannelConfiguration();
      if( _ResponseBackChannelConfigurator != null) {
        _ResponseBackChannelConfigurator.Invoke(contractType, cfg);
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


    /// <summary>
    /// Creates an HttpClient and configures its default options
    /// </summary>
    /// <param name="customizingFlags">
    /// Will be provided by the callers of DynamicClientFactory.CreateInstance (optionally).
    /// You can use this to offer different customizing flavors in order to provide adjusted
    /// transport-layer configurations like special timouts or proxy-settings.
    /// This wont be evaluated by the UJMW framework in default, it is just a channel to 
    /// support extended customizing usecases.
    /// </param>
    /// <returns></returns>
    public delegate HttpClient HttpClientFactoryMethod(string[] customizingFlags = null);

    public static HttpClientFactoryMethod HttpClientFactory { get; set; } = (
      (string[] customizingFlags) => {
        HttpClientHandler httpClientHandler = new HttpClientHandler();

        httpClientHandler.UseProxy = false;
        //if (httpClientHandler.Proxy is WebProxy) {
        //  ((WebProxy)httpClientHandler.Proxy).BypassProxyOnLocal = true;
        //}
        //else {
        //  httpClientHandler.UseProxy = false;
        //}

        HttpClient httpClient = new HttpClient(httpClientHandler);
        httpClient.Timeout = TimeSpan.FromMinutes(10);

        return httpClient;
      }
    );

    /// <summary>
    /// EXPERIMENTAL: generate all proxy-classes in only one shared dynamic assembly to reduce memory footprint
    /// </summary>
    public static bool UseCombinedDynamicAssembly = false;

    public delegate void RequestErrorDiagnosticMethod(
      string fullUrl,MethodInfo method,string rawJsonRequest,int httpReturnCode,
      string reasonPhrase,string rawJsonResponse
    );

    /// <summary>
    /// will be invoked before throwing an exception back to the caller
    /// </summary>
    public static RequestErrorDiagnosticMethod RequestErrorDiagnosticHook { get; set; } = null;

  }

}
