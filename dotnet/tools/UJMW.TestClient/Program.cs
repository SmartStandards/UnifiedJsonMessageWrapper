using System;
using System.Windows.Forms;

namespace UJMW.TestClient {

  internal static class Program {

    /// <summary>
    /// Entry point of the UJMW Test Client. The first argument (if any) is a DLL which should be
    /// added and selected ('open with' support).
    /// </summary>
    [STAThread]
    private static void Main(string[] args) {
      ApplicationConfiguration.Initialize();
      string startupDllPath = null;
      if (args.Length > 0) {
        startupDllPath = args[0];
      }
      Application.Run(new MainForm(startupDllPath));
    }

  }

}
