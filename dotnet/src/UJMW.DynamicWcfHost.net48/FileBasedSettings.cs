using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//special Assembly-Inspired Namespace für the My.Settings
using UJMW.DynamicWcfHost;

namespace System.Web.UJMW {

  internal static class FileBasedSettings {

    private static Settings _Settings = new Settings();

    public static int GracetimeForSetupPhase {
      get {
        return _Settings.GracetimeForSetupPhase;
      }
    }

  }

}
