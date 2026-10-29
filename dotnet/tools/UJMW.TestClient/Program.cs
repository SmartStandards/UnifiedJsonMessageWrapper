using System;
using System.Windows.Forms;

namespace UJMW.TestClient {

  internal static class Program {

    /// <summary>
    /// Entry point of the UJMW Test Client.
    /// </summary>
    [STAThread]
    private static void Main() {
      ApplicationConfiguration.Initialize();
      Application.Run(new MainForm());
    }

  }

}
