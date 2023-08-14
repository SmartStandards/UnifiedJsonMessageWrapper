using System;
using System.Collections.Generic;

namespace System.Web.UJMW {


  public delegate void RequestSidechannelCaptureMethod(IDictionary<string, string> requestSidechannelContainer);
  public delegate void ResponseSidechannelRestoreMethod(IEnumerable<KeyValuePair<string, string>> responseSidechannelContainer);

  public interface IUjmwSideChannelHook {

  }

}