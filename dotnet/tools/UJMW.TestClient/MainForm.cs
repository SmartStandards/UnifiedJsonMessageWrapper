using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using UJMW.TestClient.Bridge;
using UJMW.TestClient.Persistence;

namespace UJMW.TestClient {

  /// <summary>
  /// WinForms shell which hosts the complete UI (wwwroot) inside a WebView2 control.
  /// </summary>
  internal sealed class MainForm : Form {

    private const string _VirtualHostName = "ujmw-testclient.local";

    private readonly SettingsStore _Store;
    private readonly WebView2 _WebView;
    private readonly HostBridge _Bridge;

    public MainForm(string startupDllPath) {
      _Store = new SettingsStore();

      this.Text = "UJMW Test Client";
      this.Width = 1400;
      this.Height = 900;
      this.StartPosition = FormStartPosition.CenterScreen;

      _WebView = new WebView2();
      _WebView.Dock = DockStyle.Fill;
      _WebView.CreationProperties = new CoreWebView2CreationProperties();
      _WebView.CreationProperties.UserDataFolder = Path.Combine(SettingsStore.LocalDataDirectory, "WebView2");
      _WebView.CoreWebView2InitializationCompleted += this.OnCoreWebView2InitializationCompleted;
      this.Controls.Add(_WebView);

      _Bridge = new HostBridge(this, _WebView, _Store);
      if (!string.IsNullOrWhiteSpace(startupDllPath)) {
        _Bridge.SetStartupDll(startupDllPath);
      }

      this.Load += this.OnFormLoad;
    }

    /// <summary>
    /// Starts the WebView2 initialization.
    /// </summary>
    private void OnFormLoad(object sender, EventArgs e) {
      _WebView.EnsureCoreWebView2Async(null);
    }

    /// <summary>
    /// Maps the wwwroot folder to the virtual host, wires the bridge and loads the UI.
    /// </summary>
    private void OnCoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e) {
      if (!e.IsSuccess) {
        MessageBox.Show(
          this,
          "WebView2 could not be initialized (is the WebView2 runtime installed?):\r\n\r\n" + e.InitializationException.Message,
          this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error
        );
        return;
      }

      CoreWebView2 core = _WebView.CoreWebView2;
      core.SetVirtualHostNameToFolderMapping(
        _VirtualHostName, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Deny
      );
      core.Settings.IsStatusBarEnabled = false;
      core.Settings.IsZoomControlEnabled = true;
      core.WebMessageReceived += _Bridge.OnWebMessageReceived;
      core.NavigationStarting += this.OnNavigationStarting;
      core.NewWindowRequested += this.OnNewWindowRequested;
      core.Navigate("https://" + _VirtualHostName + "/index.html");
    }

    /// <summary>
    /// Prevents that the UI is replaced by foreign content (e.g. when a file is dropped outside of the handled area).
    /// </summary>
    private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e) {
      Uri target;
      if (Uri.TryCreate(e.Uri, UriKind.Absolute, out target) && target.Host == _VirtualHostName) {
        return;
      }
      e.Cancel = true;
    }

    /// <summary>
    /// Opens links which would create a new window in the default browser instead.
    /// </summary>
    private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e) {
      e.Handled = true;
      Uri target;
      if (Uri.TryCreate(e.Uri, UriKind.Absolute, out target) && (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps)) {
        ProcessStartInfo startInfo = new ProcessStartInfo(target.AbsoluteUri);
        startInfo.UseShellExecute = true;
        Process.Start(startInfo);
      }
    }

  }

}
