using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace System.Web.UJMW {

  public delegate bool ServiceContractInterfaceSelectorMethod(
    Type serviceImplementationType,
    string url,
    out Type serviceContractInterfaceType
  );

  public delegate bool AuthHeaderEvaluatorMethod(
    string rawAuthHeader,
    MethodInfo calledContractMethod,
    string callingMachine,
    ref int httpReturnCode,
    ref string failedReason
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

    public static AuthHeaderEvaluatorMethod AuthHeaderEvaluator { get; set; } = null;

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

    /// <summary></summary>
    /// <param name="logLevel">0:Trace|1:Verbose|2:Info|3:Warning|4:Error|5:Fatal</param>
    /// <param name="message"></param>
    public delegate void LoggingMethod(int logLevel, string message);
    /// <summary>
    /// A method to process logging output (LogLevel: 0:Trace|1:Verbose|2:Info|3:Warning|4:Error|5:Fatal)
    /// </summary>
    public static LoggingMethod LoggingHook { get; set; } = (logLevel, message) => {
      if (logLevel < 1) Trace.WriteLine(message);
      else if (logLevel == 1) Debug.WriteLine(message);
      else if (logLevel == 2) Trace.TraceInformation(message);
      else if (logLevel == 3) Trace.TraceWarning(message);
      else if (logLevel > 3) Trace.TraceError(message);
    };

  }

}
