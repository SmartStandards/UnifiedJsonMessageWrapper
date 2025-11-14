using System.Collections.Generic;
using System.Reflection;

namespace System.Web.UJMW {

  public delegate void RequestSidechannelProcessingMethod(
    MethodInfo calledContractMethod,
    IEnumerable<KeyValuePair<string, string>> requestSidechannelContainer
  );

  public class IncommingRequestSideChannelConfiguration {

    internal IncommingRequestSideChannelConfiguration() {
    }

    public delegate void SideChannelDefaultsGetterMethod(ref IDictionary<string, string> defaultsToUse);

    private List<string> _AcceptedChannels = null;
    internal string[] AcceptedChannels { get {
        if (_AcceptedChannels == null) return null;
        return _AcceptedChannels.ToArray();
    } }

    private RequestSidechannelProcessingMethod _ProcessingMethod = null;
    internal RequestSidechannelProcessingMethod ProcessingMethod { get { return _ProcessingMethod; } } 

    /// <summary>
    /// Enables, that the incomming message wrapper can contain ambient data within a property named '_'.
    /// These data will be deserialized an processed.
    /// NOTE: if you combine 'AcceptUjmwUnderlineProperty()' with AcceptHttpHeader(...), the order during
    /// setup will take affect! The channel, which was set up first, will be processed first. If it is
    /// containing any entries, then the other channels will be skipped...
    /// </summary>
    public void AcceptUjmwUnderlineProperty() {
      this.ImmutableGuard();
      if (_AcceptedChannels == null) {
        _AcceptedChannels = new List<string>();
      }
      _AcceptedChannels.Add("_");
    }

    public bool UnderlinePropertyIsAccepted { get { return _AcceptedChannels.Contains("_"); } }

    /// <summary>
    /// Enables, that the http header of the incomming request can contain ambient data within the given headerName.
    /// These data will be deserialized an processed.
    /// NOTE: if you combine 'AcceptUjmwUnderlineProperty()' with AcceptHttpHeader(...), the order during
    /// setup will take affect! The channel, which was set up first, will be processed first. If it is
    /// containing any entries, then the other channels will be skipped...
    /// </summary>
    public void AcceptHttpHeader(string headerName) {
      this.ImmutableGuard();
      if (headerName == "_") {
        throw new ArgumentException("headerName must not be '_' because this is a reserved magic value!");
      }
      if (_AcceptedChannels == null) {
        _AcceptedChannels = new List<string>();
      }
      _AcceptedChannels.Add(headerName);
    }

    private string[] _ContextualArgumentsToOverlay = null;

    /// <summary>
    /// Enables, that some ambient data can be provided individually from endpoint-specific contextual sources
    /// (like for example certain url/route segments etc.).
    /// NOTE: if you combine this with the global side-channels 'AcceptUjmwUnderlineProperty()' or AcceptHttpHeader(...), 
    /// this more individual arguments will override them, so this one is NOT reliieing on the order of setup!
    /// </summary>
    /// <param name="explicitArgumentNames">Cherry-pick only certain arguments OR leave this empty to include all</param>
    /// <exception cref="ArgumentException"></exception>
    public void AcceptContextualArguments(params string[] explicitArgumentNames) {
      this.ImmutableGuard();
      if(explicitArgumentNames == null){
        //if this methosd is called with null, we treat this as empty-array,
        //because the caller intendet to enable the contextual arguments!
        _ContextualArgumentsToOverlay = Array.Empty<string>();
      }
      else {
        _ContextualArgumentsToOverlay = explicitArgumentNames;
      }
    }

    /// <summary>
    /// null: accept no contextual arguments /
    /// empty-array: accept all contextual arguments
    /// </summary>
    internal string[] ContextualArgumentsToOverlay {
      get {
        return _ContextualArgumentsToOverlay;
      }
    }

    /// <summary>
    /// NOTE: this Overload is compatible to the signature of
    /// 'AmbienceHub.RestoreValuesFrom' (from the 'SmartAmbience' Nuget Package).
    /// You can directly redirect to this method handle!
    /// </summary>
    public void ProcessDataVia(Action<IEnumerable<KeyValuePair<string, string>>> processingMethod) {
      this.ImmutableGuard();
      _ProcessingMethod = (methodInfo, data) => processingMethod(data);
    }
    public void ProcessDataVia(RequestSidechannelProcessingMethod processingMethod) {
      this.ImmutableGuard();
      _ProcessingMethod = processingMethod;
    }

    /// <summary>
    /// If 'AcceptUjmwUnderlineProperty' and/or 'AcceptHttpHeader' has been activated,
    /// it will become mandatory, that at least one of these channels is provided for
    /// the incomming request. Use this method to make this optional. In order to that
    /// you can also provide default data to restore in that case.
    /// </summary>
    /// <param name="defaultsGetter">Will be used only, if there was no data received for any
    /// configured sidechannel. If you let the method handle unspecified (null) than
    /// this will skip any restore silently.
    /// Note: this quite different from restoring an empty set of entries
    /// - in the first case the ProcessingMethod WONT GET ANY TRIGGER!!!
    /// For the special case that you want to explicitely pass (null) into the
    /// ProcessingMethod, youll need to provide an valid handle to a method which sets only
    /// the defaultsToUse=null.
    /// PLEASE BE AWARE that you will also need to configure 'ProcessDataVia' when
    /// youre providing detaults to restore!
    /// </param>
    public void AcceptNoChannelProvided(SideChannelDefaultsGetterMethod defaultsGetter = null) {
      this.ImmutableGuard();
      if (_AcceptedChannels == null) {
        _AcceptedChannels = new List<string>();
      }
      _SkipAllowed = true;
      _DefaultsGetterOnSkip = defaultsGetter;
    }

    private bool _SkipAllowed = false;
    internal bool SkipAllowed { get { return _SkipAllowed; } }

    private SideChannelDefaultsGetterMethod _DefaultsGetterOnSkip = null;
    internal SideChannelDefaultsGetterMethod DefaultsGetterOnSkip { get { return _DefaultsGetterOnSkip; } }

    #region " Immutable "

    private bool _IsImmutable = false;

    internal void MakeImmutable() {
      _IsImmutable = true;
    }
    private void ImmutableGuard() {
      if (_IsImmutable) {
        throw new InvalidOperationException("This Configuration is now Immutable! It can only be modified during the setup phase.");
      }
    }

  #endregion

  }

}
