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
    ref int httpReturnCode
  );

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

    //UNDER DEVELOPMENT...
    //https://stackoverflow.com/questions/17961564/wcf-exception-handling-using-ierrorhandler
    //https://www.c-sharpcorner.com/UploadFile/b182bf/centralize-exception-handling-in-wcf-part-10/
    //public static Action<MethodInfo,Exception> BlExceptionHandler { get; set; } = null;

    /// <summary>
    /// will be invoked for exceptions that have been thrown during host creation (when WCF is using our factory)
    /// </summary>
    public static Action<Exception> FactoryExceptionVisitor { get; set; } = (ex)=> Trace.TraceError(ex.Message);

    private static IncommingRequestSideChannelConfigurationMethod _RequestSideChannelConfigurator { get; set; } = null;
    private static OutgoingResponseSideChannelConfigurationMethod _ResponseSideChannelConfigurator { get; set; } = null;

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

    public static ServiceContractInterfaceSelectorMethod ContractSelector { get; set; } = (
      (Type serviceImplementationType, string url, out Type serviceContractInterfaceType) => {

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

        if (contractInterfaces.Length > 1) {
          string[] urlTokens = url.Split('/');
          string versionFromUrl = null;
          for (int i = urlTokens.Length - 1; i > 0; i--) {
            if (Regex.IsMatch(urlTokens[i], "^([vV][0-9]{1,})$")) {
              versionFromUrl = urlTokens[i].ToLower();
              break;
            }
          }
          if (versionFromUrl != null) {
            var versionMatchingInterface = contractInterfaces.Where((i) => ("." + i.FullName.ToLower() + ".").Contains(versionFromUrl)).FirstOrDefault();
            if (versionMatchingInterface != null) {
              serviceContractInterfaceType = versionMatchingInterface;
              //prefer a interface (with a 'ServiceContractAttribute') wich is matching the version
              return true;
            }
          }
        }

        if (contractInterfaces.Length > 0) {
          //prefer the first interface (with a 'ServiceContractAttribute')
          serviceContractInterfaceType = contractInterfaces.First();
          return true;
        }
        else {
          //otherwise use the first interface (without a 'ServiceContractAttribute')
          serviceContractInterfaceType = allInterfaces.First();
          return false;
        }

      }
    );

    public static AuthHeaderEvaluatorMethod AuthHeaderEvaluator { get; set; } = (
      (string rawAuthHeader, MethodInfo calledContractMethod, string callingMachine, ref int httpReturnCode) => {
        return true;
      }
    );

    public static bool ForceHttps { get; set; } = false;

    /// <summary>
    /// Usually WCF requires at least any authentication schema.
    /// Disableing this is only recommended when you're using any
    /// other secirity framework instead (may-be access-token based).
    /// Please be aware, that working without the build-in wcf security features
    /// could require to enable 'Anonymous' authentication for the Web-Application... 
    /// </summary>
    public static bool DiableNtlm { get; set; } = false;

  }

}
