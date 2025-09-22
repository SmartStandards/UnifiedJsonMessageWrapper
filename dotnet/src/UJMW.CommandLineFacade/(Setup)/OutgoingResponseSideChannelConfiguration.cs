using System;
using System.Collections.Generic;
using System.Reflection;

namespace UJMW.CommandLineFacade {

  public delegate void ResponseSidechannelCaptureMethod(
    MethodInfo calledContractMethod,
    IDictionary<string, string> responseSidechannelContainer
  );

  public class OutgoingResponseSideChannelConfiguration {
    internal OutgoingResponseSideChannelConfiguration() {
    }

    private ResponseSidechannelCaptureMethod _CaptureMethod = null;
    internal ResponseSidechannelCaptureMethod CaptureMethod { get { return _CaptureMethod; } }

    private List<string> _ChannelsToProvide = null;
    internal string[] ChannelsToProvide { get {
        if (_ChannelsToProvide == null) return null;
        return _ChannelsToProvide.ToArray(); 
    } }

    /// <summary>
    /// NOTE: this Overload is compatible to the signature of
    /// 'AmbienceHub.CaptureCurrentValuesTo' (from the 'SmartAmbience' Nuget Package).
    /// You can directly redirect to this method handle!
    /// </summary>
    public void CaptureDataVia(Action<IDictionary<string, string>> captureMethod) {
      this.ImmutableGuard();
      _CaptureMethod = (methodInfo, targetContainer) => captureMethod.Invoke(targetContainer);
    }

    public void CaptureDataVia(ResponseSidechannelCaptureMethod captureMethod) {
      this.ImmutableGuard();
      _CaptureMethod = captureMethod;
    }

    public void ProvideNoChannel() {
      this.ImmutableGuard();
      if(_ChannelsToProvide == null) {
        _ChannelsToProvide = new List<string>();
      }
      else {
        _ChannelsToProvide.Clear();
      }
    }
    /// <summary>
    /// Enables, that the outgoing message wrapper will contain ambient data within a property named '_'.
    /// This data will be collected using the configured CaptureMethod.
    /// NOTE: if you combine 'ProvideUjmwUnderlineProperty()' with ProvideHttpHeader(...), the data will be captured
    /// only once and provided to all channels.
    /// </summary>
    public void ProvideUjmwUnderlineProperty() {
      this.ImmutableGuard();
      if (_ChannelsToProvide == null) {
        _ChannelsToProvide = new List<string>();
      }
      _ChannelsToProvide.Add("_");
    }

    public bool UnderlinePropertyIsProvided { get { return _ChannelsToProvide.Contains("_"); } }

    /// <summary>
    /// Enables, that the http header of the outgoing request will contain ambient data within the given headerName.
    /// This data will be collected using the configured CaptureMethod.
    /// NOTE: if you combine 'ProvideUjmwUnderlineProperty()' with ProvideHttpHeader(...), the data will be captured
    /// only once and provided to all channels.
    /// </summary>
    public void ProvideHttpHeader(string headerName) {
      this.ImmutableGuard();
      if (headerName == "_") {
        throw new ArgumentException("headerName must not be '_' because this is a reserved magic value!");
      }
      if (_ChannelsToProvide == null) {
        _ChannelsToProvide = new List<string>();
      }
      _ChannelsToProvide.Add(headerName);
    }

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
