using Logging.SmartStandards;
using Logging.SmartStandards.CopyForUJMW;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

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
    private static AnnouncementMethod _SelfAnnouncementMethod = null;
    private static int _AutoTriggerInterval = -1;

    private static List<EndpointInfo> _RegisteredEndpoints = new List<EndpointInfo>();

    private static bool _ApplicationReady = false;
    private static Task _AutoTriggerTask = null;
    private static DateTime _NextAutoAnnounce = DateTime.MinValue;

    #region " FULL Convenience w. auto-event wire-up "

    /// <summary>
    ///  Configures the self announcement framework.
    ///  This overload allows to choose between automatic announcement (in default),
    ///  recurring automatic announcement and manual announcement (see the 'autoTriggerInterval').
    /// </summary>
    /// <param name="hostApplicationLifetime">
    ///   can be requested to be injected in the 'Configure'-method
    /// </param>
    /// <param name="featureCollection">
    ///   can be retrieved from your IApplicationBuilder like this: app.ServerFeatures
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
      IHostApplicationLifetime hostApplicationLifetime,
      IFeatureCollection featureCollection,
      AnnouncementMethod selfAnnouncementMethod,
      int autoTriggerInterval = 0
    ) {
      Configure(
        hostApplicationLifetime, featureCollection, selfAnnouncementMethod, null, autoTriggerInterval
      );
    }

    /// <summary>
    ///  Configures the self announcement framework.
    ///  This overload allows to choose between automatic announcement (in default),
    ///  recurring automatic announcement and manual announcement (see the 'autoTriggerInterval').
    /// </summary>
    /// <param name="hostApplicationLifetime">
    ///   can be requested to be injected in the 'Configure'-method
    /// </param>
    /// <param name="featureCollection">
    ///   can be retrieved from your IApplicationBuilder like this: app.ServerFeatures
    /// </param>
    /// <param name="selfAnnouncementMethod">
    ///  a callback to dispatch the endpoint information to the target registry which collects all urls
    /// </param>
    /// <param name="overrideBaseUrls">
    ///  Normally the urls will be enumerated using the 'IServerAddressesFeature' from the given featureCollection.
    ///  Use this param to define explicitely which application base-urls should be populated.
    /// </param>
    /// <param name="autoTriggerInterval">
    ///  Auto trigger interval in Minutes.
    ///  If set to 0, the self announnce will be triggered only once (after the webapplication has booted initially).
    ///  If set to -1, the self announce must be triggered manually ->
    ///  either by calling the 'TriggerSelfAnnouncement()' method or
    ///  from external via http (requires, that the SelfAnnouncementTriggerEndpoint has been initialized).
    /// </param>
    public static void Configure(
      IHostApplicationLifetime hostApplicationLifetime,
      IFeatureCollection featureCollection,
      AnnouncementMethod selfAnnouncementMethod,
      string[] overrideBaseUrls,
      int autoTriggerInterval = 0
    ) {

      //register an eventhandler to wait at the right moment...
      hostApplicationLifetime.ApplicationStarted.Register(() => {

        //... when the 'IServerAddressesFeature' will be available:
        IServerAddressesFeature addressFeature = featureCollection.Get<IServerAddressesFeature>();

        if (overrideBaseUrls == null || overrideBaseUrls.Length == 0) {
          overrideBaseUrls = addressFeature.Addresses.Where( //filter out non-http(s) bindings like 'net.tcp://...' or 'net.pipe://...'
            (uglyBaseUrlBindingPattern)=> uglyBaseUrlBindingPattern.StartsWith("http", StringComparison.CurrentCultureIgnoreCase)
          ).Select(
            (uglyBaseUrlBindingPattern) => {

              string baseUrl;

              if (uglyBaseUrlBindingPattern.Contains("//*")) {
                string thisHostName = Environment.MachineName;
                try {
                  IPHostEntry entry = Dns.GetHostEntry(Dns.GetHostName());
                  if (!string.IsNullOrWhiteSpace(entry?.HostName)) {
                    thisHostName = entry.HostName;
                  }
                }
                catch {
                }
                baseUrl = uglyBaseUrlBindingPattern.Replace("//*", "//" + thisHostName).Replace(":*", "");
              }
              else {
                baseUrl = uglyBaseUrlBindingPattern.Replace(":*", "");
              }

              if (baseUrl.StartsWith("http://", StringComparison.CurrentCultureIgnoreCase)) {
                baseUrl = baseUrl.Replace(":80", ""); //remove unnecessary port specification for http
              }
              else if (baseUrl.StartsWith("https://", StringComparison.CurrentCultureIgnoreCase)) {
                baseUrl = baseUrl.Replace(":443", ""); //remove unnecessary port specification for https
              }

              return baseUrl;
            }
          ).ToArray();
        }

        //auto evaluate the current hosting address
        if (overrideBaseUrls.Any()) {

          //configure
          SelfAnnouncementHelper.Configure(
            overrideBaseUrls,
            selfAnnouncementMethod,
            autoTriggerInterval
          );

          //if were not in maunal mode...
          if (autoTriggerInterval >= 0) {

            //prepare wireup for auto de-announce at application stop
            hostApplicationLifetime.ApplicationStopping.Register(
              SelfAnnouncementHelper.OnApplicationStopping
            );

          }

          //now were done - lets notify that were ready
          //(this will implicitely start auto-announce, if configured)
          SelfAnnouncementHelper.OnApplicationStarted();

        }

      });

    }

    #endregion

    /// <summary>
    ///  Configures the self announcement framework for manual usage
    ///  Either by calling the 'TriggerSelfAnnouncement()' method or
    ///  from external via http (requires, that the SelfAnnouncementTriggerEndpoint has been initialized).
    /// </summary>
    /// <param name="baseUrls"></param>
    /// <param name="selfAnnouncementMethod">
    ///  a callback to dispatch the endpoint information to the target registry which collects all urls
    /// </param>
    public static void Configure(
      string[] baseUrls,
      AnnouncementMethod selfAnnouncementMethod
    ) {

      Configure(
        baseUrls,
        selfAnnouncementMethod,
        -1 //the public method will not offer enablement funktionality for the autoTrigger,
           //because it would only work if additinal application-event wirepups will be done...
           //if this is the goal, then the other convenience overload should be used (see below) 
      );

    }

    /// <summary>
    ///  Configures the self announcement framework.
    /// </summary>
    /// <param name="baseUrls"></param>
    /// <param name="selfAnnouncementMethod">
    ///  a callback to dispatch the endpoint information to the target registry which collects all urls
    /// </param>
    /// <param name="autoTriggerInterval">
    ///  Auto trigger interval in Minutes.
    ///  If set to 0, the self announnce will be triggered only once (after the webapplication has booted initially).
    ///  If set to -1, the self announce must be triggered manually ->
    ///  either by calling the 'TriggerSelfAnnouncement()' method or
    ///  from external via http (requires, that the SelfAnnouncementTriggerEndpoint has been initialized).
    ///  IMPORTANT: to use auto announce (and un-announce), youll need to wire-up or call the 
    ///  'OnApplicationStarted' and 'OnApplicationStopping' methods (see comments, how to do so)
    /// </param>
    private static void Configure(
      string[] baseUrls,
      AnnouncementMethod selfAnnouncementMethod,
      int autoTriggerInterval
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

      _BaseUrls = baseUrls.Select((u) => {
        if (u.EndsWith("/")) {
          return u;
        }
        else {
          return u + "/";
        }
      }).ToArray();

      _SelfAnnouncementMethod = selfAnnouncementMethod;
      _AutoTriggerInterval = autoTriggerInterval;

      if (_ApplicationReady) {
        StartAutoAnnounce();
      }

    }

    internal static string[] BaseUrls {
      get {
        return _BaseUrls;
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
      string controllerTitle,
      string relativeRoute,
      EndpointCategory endpointCategory,
      string apiGroupName = null
    ) {

      lock (_RegisteredEndpoints) {

        _RegisteredEndpoints.Add(
          new EndpointInfo(
             null, contractIdentifyingName,
             controllerTitle, relativeRoute, endpointCategory, null, apiGroupName
          )
        );

      }
    }

    public static void RegisterEndpoint(
      Type contractType,
      string controllerTitle,
      string relativeRoute,
      EndpointCategory endpointCategory,
      DynamicUjmwControllerOptions ujmwOptions = null,
      string apiGroupName = null
    ) {

      lock (_RegisteredEndpoints) {

        _RegisteredEndpoints.Add(
          new EndpointInfo(
             contractType, SelfAnnouncementHelper.BuildContractidentifyingName(contractType),
             controllerTitle, relativeRoute, endpointCategory, ujmwOptions, apiGroupName
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

      }

    }

    public static bool TriggerSelfAnnouncement() {
      return TriggerSelfAnnouncement(out string dummyBuffer);
    }

    internal static bool TriggerSelfAnnouncement(out string addInfo, bool catchExceptions = true) {

      if(_BaseUrls == null) {
        throw new Exception("Calling this method is not allowed before the 'Configure' method has been called!");
      }

      LastAction = "announce";
      LastActionTime = DateTime.Now;
      LastAddInfo = null;

      addInfo = "";
      string epInfoLines = string.Join(Environment.NewLine, RegisteredEndpoints.Select(ep => ep.ToString()));
      try {

        _SelfAnnouncementMethod.Invoke(_BaseUrls, RegisteredEndpoints, true, ref addInfo);
        LastAddInfo = addInfo;

        string msg = $"Self-Announcement completed for {RegisteredEndpoints.Count()} endpoints with base-url(s) '{string.Join("'+'",_BaseUrls)}'. {addInfo}\n{epInfoLines}";
        DevLogger.LogInformation(0, 72007, msg);

        LastFault = null;
      }
      catch(Exception ex) {
        LastFault = ex.Message;
        string msg = $"Self-Announcement failed for {RegisteredEndpoints.Count()} endpoints with base-url(s) '{string.Join("'+'", _BaseUrls)}'. {addInfo}\n{epInfoLines}";
        DevLogger.LogError(0, 72007, new Exception(msg, ex));

        if (!catchExceptions) {
          throw;
        }

        return false;
      }

      return true;
    }

    public static void TriggerUnAnnouncement() {

      if (_BaseUrls == null) {
        throw new Exception("Calling this method is not allowed before the 'Configure' method has been called!");
      }

      LastAction = "unannounce";
      LastActionTime = DateTime.Now;
      LastAddInfo = null;

      string addInfo = "";
      string epInfoLines = string.Join(Environment.NewLine, RegisteredEndpoints.Select(ep => ep.ToString()));
      try {
        
        _SelfAnnouncementMethod.Invoke(_BaseUrls, RegisteredEndpoints, false, ref addInfo);
        LastAddInfo = addInfo;

        string msg = $"Self-Unannouncement completed for {RegisteredEndpoints.Count()} endpoints with base-url(s) '{string.Join("'+'", _BaseUrls)}'. {addInfo}\n{epInfoLines}";
        DevLogger.LogInformation(0, 72007, msg);

        LastFault = null;
      }
      catch (Exception ex) {
        LastFault = ex.Message;
        string msg = $"Self-Unannouncement failed for {RegisteredEndpoints.Count()} endpoints with base-url(s) '{string.Join("'+'", _BaseUrls)}'. {addInfo}\n{epInfoLines}";
        DevLogger.LogError(0, 72007, new Exception(msg, ex));
      }
    }

    /// <summary>
    /// If Auto announce is used, then this
    /// needs to be called from the main method 
    /// when the application startup phase has been completed.
    /// (you can easyly do this by wireing up the
    /// 'IHostApplicationLifetime.ApplicationStarted'-Event)
    /// </summary>
    public static void OnApplicationStarted() {

      if (_ApplicationReady) {
        return;
      }
      _ApplicationReady = true;

      //only if were alredy configured...
      if (_SelfAnnouncementMethod != null) {
        StartAutoAnnounce();
      }

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

    #region " Convenience for getting an 'Origin' name "

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static string GetOriginNameGuess() {
      return GetApplicationNameGuess(
        Assembly.GetCallingAssembly()
      ).Replace(" ", "") + "@" + System.Environment.MachineName;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static string GetApplicationNameGuess() {
      return GetApplicationNameGuess(Assembly.GetCallingAssembly());
    }

    /// <summary></summary>
    /// <param name="applicationRepresentingAssembly">WILL ONLY BE USED AS FALLBACK!</param>
    /// <returns></returns>
    public static string GetApplicationNameGuess(Assembly applicationRepresentingAssembly) {

      Assembly entryAssembly = Assembly.GetEntryAssembly();
      if (entryAssembly != null) {
        return entryAssembly.GetName().Name;
      }

      return applicationRepresentingAssembly.GetName().Name;
    }

    #endregion

  }

}
