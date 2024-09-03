using Logging.SmartStandards;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Hosting;

namespace System.Web.UJMW.SelfAnnouncement {

  /// <summary>
  /// A callback to dispatch the endpoint information to the target registry which collects all urls
  /// </summary>
  /// <param name="baseUrls">
  ///   Base URL(s) of the service application WITH trailing slash (technical ensured)
  ///   (can be directly concatinated with values of the 'UjmwEndpointInfo.RelativeRoute' properties)
  /// </param>
  /// <param name="endpoints"></param>
  /// <param name="active"> True=ANNOUNCE / False=UN-ANNOUNCE</param>
  /// <param name="additionalInfo"> can be set optionally - only relevant for diagnostics</param>
  public delegate void AnnouncementMethod(
    string[] baseUrls,
    EndpointInfo[] endpoints,
    bool active,
    ref string additionalInfo
  );

  public static class SelfAnnouncementHelper {

    private static string[] _BaseUrls = null;
    private static bool _DisableAutoEvaluatedBaseUrls = false;

    private static AnnouncementMethod _SelfAnnouncementMethod = null;
    private static int _AutoTriggerInterval = -1;

    private static List<EndpointInfo> _RegisteredEndpoints = new List<EndpointInfo>();

    private static bool _ApplicationReady = false;
    private static Task _AutoTriggerTask = null;
    private static DateTime _NextAutoAnnounce = DateTime.MinValue;

    /// <summary>
    ///  Configures the self announcement framework.
    ///  This overload enables automatic evaluation of the application's baseAddress.
    ///  This gives more convenience, but there are some traps in WCF!
    ///  The Auto-announce wont start until 'SelfAnnouncementHelper.OnApplicationStarted() has been called - 
    ///  BUT you should NOT do this directly from a IHttpModule, because under IISExpress you will louse your
    ///  Port-Information! If youre using external triggers (via 'SelfAnnouncementTriggerEndpoint') just skip any
    ///  call to 'OnApplicationStarted' - it will be done implicitely. If not, you should add the followig code as
    ///  FIRST statement within your IHttpModule: 
    ///  context.BeginRequest += (s, a) => SelfAnnouncementHelper.OnApplicationStarted();
    /// </summary>
    /// <param name="selfAnnouncementMethod">
    ///  a callback to dispatch the endpoint information to the target registry which collects all urls
    /// </param>
    /// <param name="autoTriggerInterval">
    ///  Auto trigger interval in Minutes.
    ///  If set to 0, the self announnce will be triggered only once (after the webapplication has booted initially).
    ///  If set to -1, the self announce must be triggered manually ->
    ///  either by calling the 'TriggerSelfAnnouncement()' method or
    ///  from external via http (requires, that the SelfAnnouncementTriggerEndpoint has been initialized).
    /// </param>
    public static void Configure(
      AnnouncementMethod selfAnnouncementMethod,
      int autoTriggerInterval = 0
    ) {

      if (_SelfAnnouncementMethod != null) {
        throw new Exception("The 'Configure' method has already been called!");
      }
      if (selfAnnouncementMethod == null) {
        throw new Exception("The 'selfAnnouncementMethod' must not be null!");
      }

      ConfigureInternal(selfAnnouncementMethod, autoTriggerInterval);
    }

    /// <summary>
    ///  Configures the self announcement framework.
    ///  This overload disabled automatic evaluation of the application's baseAddress and uses the given baseUrls explicitely!
    /// </summary>
    /// <param name="baseUrls">
    /// </param>
    /// <param name="selfAnnouncementMethod">
    ///  a callback to dispatch the endpoint information to the target registry which collects all urls
    /// </param>
    /// <param name="autoTriggerInterval">
    ///  Auto trigger interval in Minutes.
    ///  If set to 0, the self announnce will be triggered only once (after the webapplication has booted initially).
    ///  If set to -1, the self announce must be triggered manually ->
    ///  either by calling the 'TriggerSelfAnnouncement()' method or
    ///  from external via http (requires, that the SelfAnnouncementTriggerEndpoint has been initialized).
    /// </param>
    public static void Configure(
      string[] baseUrls,
      AnnouncementMethod selfAnnouncementMethod,
      int autoTriggerInterval = 0
    ) {

      if (_SelfAnnouncementMethod != null) {
        throw new Exception("The 'Configure' method has already been called!");
      }
      if (selfAnnouncementMethod == null) {
        throw new Exception("The 'selfAnnouncementMethod' must not be null!");
      }
      if (baseUrls.Length == 0 || string.IsNullOrWhiteSpace(baseUrls[0])) {
        throw new ArgumentException("There is no baseUrl!");
      }

      _DisableAutoEvaluatedBaseUrls = true;
      _BaseUrls = baseUrls.Select((u) => {
        if (u.EndsWith("/")) {
          return u;
        }
        else {
          return u + "/";
        }
      }).ToArray();

      ConfigureInternal(selfAnnouncementMethod, autoTriggerInterval);

    }

    private static void ConfigureInternal(
      AnnouncementMethod selfAnnouncementMethod,
      int autoTriggerInterval = 0
    ) {

      _SelfAnnouncementMethod = selfAnnouncementMethod;
      _AutoTriggerInterval = autoTriggerInterval;

      lock (_RegisteredEndpoints) {
        EndpointInfo[] configuredEndpoints = EndpointEnumerationHelper.EnumerateWcfEndpoints();
        foreach (EndpointInfo ep in configuredEndpoints) {
          _RegisteredEndpoints.Add(ep);
        }
      }

      if (_ApplicationReady) {
        StartAutoAnnounce();
      }

    }

    internal static string[] BaseUrls {
      get {
        if (_BaseUrls == null) {
          EnsureBaseUrlsPresent();
        }
        lock (_BaseUrls) {
          return _BaseUrls;
        }
      }
    }

    public static EndpointInfo[] RegisteredEndpoints {
      get {
        lock (_RegisteredEndpoints) {
          return _RegisteredEndpoints.ToArray();
        }
      }
    }

    public static void RegisterEndpoint(
      string contractIdentifyingName,
      string serviceTitle,
      string relativeRoute,
      EndpointCategory endpointCategory
    ) {

      lock (_RegisteredEndpoints) {

        _RegisteredEndpoints.Add(
          new EndpointInfo(
             null, contractIdentifyingName,
             serviceTitle, relativeRoute, endpointCategory
          )
        );

      }
    }

    public static void RegisterEndpoint(
      Type contractType,
      string controllerTitle,
      string relativeRoute,
      EndpointCategory endpointCategory
    ) {

      lock (_RegisteredEndpoints) {

        _RegisteredEndpoints.Add(
          new EndpointInfo(
             contractType, SelfAnnouncementHelper.BuildContractidentifyingName(contractType),
             controllerTitle, relativeRoute, endpointCategory
          )
        );

      }
    }

    private static void StartAutoAnnounce() {

      if(_AutoTriggerInterval == 0) {
        TriggerSelfAnnouncement();

      }
      else if (_AutoTriggerInterval > 0) {

        _AutoTriggerTask = Task.Run(() => {
          while(_ApplicationReady) {
            if(DateTime.Now > _NextAutoAnnounce) {
              _NextAutoAnnounce = DateTime.Now.AddMinutes(_AutoTriggerInterval);
              TriggerSelfAnnouncement();
            }
            Threading.Thread.Sleep(1000);
          }
        });

        //TODO: prüfen ab man das via 'HostingEnvironment.QueueBackgroundWorkItem' tun kann

      }

    }

    public static bool TriggerSelfAnnouncement() {
      return TriggerSelfAnnouncement(out string dummyBuffer);
    }

    internal static bool TriggerSelfAnnouncement(out string addInfo, bool catchExceptions = true) {

      if(_SelfAnnouncementMethod == null) {
        throw new Exception("Calling this method is not allowed before the 'Configure' method has been called!");
      }
      OnApplicationStarted();

      LastAction = "announce";
      LastActionTime = DateTime.Now;
      LastAddInfo = null;

      addInfo = "";
      string epInfoLines = string.Join(Environment.NewLine, RegisteredEndpoints.Select(ep => ep.ToString()));
      try {

        _SelfAnnouncementMethod.Invoke(_BaseUrls, RegisteredEndpoints, true, ref addInfo);
        LastAddInfo = addInfo;

        string msg = $"Self-Announcement completed for {RegisteredEndpoints.Count()} endpoints with base-url '{_BaseUrls}'. {addInfo}\n{epInfoLines}";
        DevToTraceLogger.LogInformation(72007, msg);

        LastFault = null;
      }
      catch(Exception ex) {
        LastFault = ex.Message;
        string msg = $"Self-Announcement failed for {RegisteredEndpoints.Count()} endpoints with base-url '{_BaseUrls}'. {addInfo}\n{epInfoLines}";
        DevToTraceLogger.LogError(72007, new Exception(msg, ex));

        if (!catchExceptions) {
          throw;
        }

        return false;
      }

      return true;
    }

    public static void TriggerUnAnnouncement() {

      if (_SelfAnnouncementMethod == null) {
        throw new Exception("Calling this method is not allowed before the 'Configure' method has been called!");
      }
      OnApplicationStarted();

      LastAction = "unannounce";
      LastActionTime = DateTime.Now;
      LastAddInfo = null;

      string addInfo = "";
      string epInfoLines = string.Join(Environment.NewLine, RegisteredEndpoints.Select(ep => ep.ToString()));
      try {
        
        _SelfAnnouncementMethod.Invoke(_BaseUrls, RegisteredEndpoints, false, ref addInfo);
        LastAddInfo = addInfo;

        string msg = $"Self-Unannouncement completed for {RegisteredEndpoints.Count()} endpoints with base-url '{_BaseUrls}'. {addInfo}\n{epInfoLines}";
        DevToTraceLogger.LogInformation(72007, msg);

        LastFault = null;
      }
      catch (Exception ex) {
        LastFault = ex.Message;
        string msg = $"Self-Unannouncement failed for {RegisteredEndpoints.Count()} endpoints with base-url '{_BaseUrls}'. {addInfo}\n{epInfoLines}";
        DevToTraceLogger.LogError(72007, new Exception(msg, ex));
      }
    }

    private static void EnsureBaseUrlsPresent() { 
      if(_BaseUrls == null) {
        if (_DisableAutoEvaluatedBaseUrls) {
          //wird durch die configure-methode überklatscht
          //und vorher findet sowieso kein annunce statt
          _BaseUrls = new string[] { };
        }
        else {
          _BaseUrls = EndpointEnumerationHelper.GetBaseAddressesFromConfig();
          string baseAddressFromCurrentRequest = EndpointEnumerationHelper.GetBaseAddressFromCurrentRequest();
          if (!string.IsNullOrWhiteSpace(baseAddressFromCurrentRequest) && !_BaseUrls.Contains(baseAddressFromCurrentRequest)) {
            Array.Resize(ref _BaseUrls, _BaseUrls.Length + 1);
            _BaseUrls[_BaseUrls.Length - 1] = baseAddressFromCurrentRequest;
          }
        }
      }
    }

    /// <summary>
    ///  WARNING: you should NOT call this directly from a IHttpModule, because under IISExpress you will louse your
    ///  Port-Information! If youre using external triggers (via 'SelfAnnouncementTriggerEndpoint') just skip any
    ///  call to 'OnApplicationStarted' - it will be done implicitely. If not, you should add the followig code as
    ///  FIRST statement within your IHttpModule: 
    ///  context.BeginRequest += (s, a) => SelfAnnouncementHelper.OnApplicationStarted();
    /// </summary>
    public static void OnApplicationStarted() {

      if (_ApplicationReady) {
        return;
      }
      _ApplicationReady = true;

      EnsureBaseUrlsPresent();
     
      //only if were alredy configured...
      if (_SelfAnnouncementMethod != null) {
        StartAutoAnnounce();
      }

      HostingEnvironment.RegisterObject(
        new HostingEnvironmentShutdownNotifier(
          SelfAnnouncementHelper.OnApplicationStopping
        )
      );

    }

    /// <summary>
    /// If Auto announce is used, then this
    /// needs to be called from the main method
    /// when the application is shutting down.
    /// (you can easyly do this by wireing up the
    /// 'IHostApplicationLifetime.ApplicationStopping'-Event)
    /// </summary>
    public static void OnApplicationStopping() {
      _ApplicationReady = false;

      if(_BaseUrls != null) {
        Task.Run(TriggerUnAnnouncement);
      }

      if(_AutoTriggerTask != null) {
        _AutoTriggerTask.Wait();
        _AutoTriggerTask.Dispose();
        _AutoTriggerTask = null;
      }
    }

    /// <summary>
    /// Generates a type name, where potentially existing generic-arguments are separated by a '_' character
    /// and completely without special chars except '_' and '.' (for namespaces)
    /// </summary>
    /// <param name="t"></param>
    /// <param name="level"></param>
    /// <param name="skipNamespace">skips the namespace and builds a simplified alias</param>
    /// <returns></returns>
    public static string BuildContractidentifyingName(Type t, int level = 1, bool skipNamespace = false) {

      if (t.IsGenericType) {

        string[] paramNames = t.GenericTypeArguments.Select(
          (ta) => {
            if (ta.IsValueType && !ta.IsGenericType) {
              return ta.Name; //hier ohne NS
            }
            else {
              //RECURSE!
              return BuildContractidentifyingName(ta, level + 1, skipNamespace);
            }
          }
        ).ToArray();

        string separator = new string('_', level);
        string nameWithoutGpPrefix = t.Name.Substring(0, t.Name.IndexOf('`'));

        if (string.IsNullOrWhiteSpace(t.Namespace) || t.Namespace == "System" || skipNamespace) {
          return $"{nameWithoutGpPrefix}{separator}{String.Join(separator, paramNames)}";
        }
        else {
          return $"{t.Namespace}.{nameWithoutGpPrefix}{separator}{String.Join(separator, paramNames)}";
        }

      }
      else {

        if (string.IsNullOrWhiteSpace(t.Namespace) || t.Namespace == "System" || skipNamespace) {
          return t.Name;
        }
        else {
          return $"{t.Namespace}.{t.Name}";
        }

      }
    }

    //DEBUG
    internal static string LastAction { get; private set; } = "none";
    internal static DateTime LastActionTime { get; private set; } = DateTime.MinValue;
    internal static string LastAddInfo { get; private set; } = "";
    internal static string LastFault { get; private set; } = "";

  }

  internal class HostingEnvironmentShutdownNotifier : IRegisteredObject {

    private Action _CallbackOnShutdown;

    public HostingEnvironmentShutdownNotifier(Action callbackOnShutdown) {
      _CallbackOnShutdown = callbackOnShutdown;
    }

    public void Stop(bool immediate) {
      if (immediate) return;
      _CallbackOnShutdown.Invoke();
    }

  }

}
