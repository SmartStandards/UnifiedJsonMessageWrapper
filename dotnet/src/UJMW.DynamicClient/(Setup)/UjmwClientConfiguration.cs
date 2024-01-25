using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace System.Web.UJMW {

  public class UjmwClientConfiguration {

    private UjmwClientConfiguration() {
    }

    public delegate void OutgoingRequestSideChannelConfigurationMethod(
      Type serviceContractInterfaceType,
      OutgoingRequestSideChannelConfiguration sideChannel
    );

    public delegate void IncommingResponseSideChannelConfigurationMethod(
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

    /// <summary>
    /// will be invoked for exceptions that have been thrown during host creation (when WCF is using our factory)
    /// </summary>
    public static FaultRepsonseHandlerMethod FaultRepsonseHandler { get; set; } = (
      (string fullUrl, MethodInfo method, string faultMessage) => throw new UjmwFaultException(fullUrl, method,faultMessage)
    );

    private static OutgoingRequestSideChannelConfigurationMethod _RequestSideChannelConfigurator { get; set; } = null;
    private static IncommingResponseSideChannelConfigurationMethod _ResponseSideChannelConfigurator { get; set; } = null;

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

    public static void ConfigureResponseSidechannel(IncommingResponseSideChannelConfigurationMethod responseSideChannelConfigurator ) {
      _ResponseSideChannelConfigurator = responseSideChannelConfigurator;
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
      if( _ResponseSideChannelConfigurator != null) {
        _ResponseSideChannelConfigurator.Invoke(contractType, cfg);
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

    //public static AuthHeaderEvaluatorMethod AuthHeaderEvaluator { get; set; } = (
    //  (string rawAuthHeader, MethodInfo calledContractMethod, string callingMachine, ref int httpReturnCode) => {
    //    return true;
    //  }
    //);

  }

}
