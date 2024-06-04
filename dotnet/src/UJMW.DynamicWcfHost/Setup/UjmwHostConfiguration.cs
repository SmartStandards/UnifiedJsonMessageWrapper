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
    MethodInfo targetContractMethod,
    string callingMachine,
    ref int httpReturnCode,
    ref string failedReason
  );

  public delegate void ArgumentPreEvaluatorMethod(
    MethodInfo targetContractMethod,
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

        if(contractInterfaces.Length == 1) {
          serviceContractInterfaceType = contractInterfaces[0];
          return true;
        }
        if(contractInterfaces.Length == 0) {
          //otherwise use the first interface (without a 'ServiceContractAttribute')
          serviceContractInterfaceType = allInterfaces.First();
          return false;
        }

        string[] urlTokens = url.Split('/');
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

    public static AuthHeaderEvaluatorMethod AuthHeaderEvaluator { get; set; } = null;

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

    /// <summary></summary>
    /// <param name="logLevel">0:Trace|1:Verbose|2:Info|3:Warning|4:Error|5:Fatal</param>
    /// <param name="message"></param>
    public delegate void LoggingMethod(int logLevel, string message);
    /// <summary>
    /// A method to process logging output (LogLevel: 0:Trace|1:Verbose|2:Info|3:Warning|4:Error|5:Fatal)
    /// </summary>
    public static LoggingMethod LoggingHook { get; set; } = (logLevel, message) => {
      if (logLevel < 1)       Trace.WriteLine(message);
      else if (logLevel == 1) Debug.WriteLine(message);      
      else if (logLevel == 2) Trace.TraceInformation(message);  
      else if (logLevel == 3) Trace.TraceWarning(message);    
      else if (logLevel > 3)  Trace.TraceError(message);   
    };
    
  }

}
