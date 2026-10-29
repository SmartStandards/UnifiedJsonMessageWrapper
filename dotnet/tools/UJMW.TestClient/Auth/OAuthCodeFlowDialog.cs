using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using UJMW.TestClient.Model;
using UJMW.TestClient.Persistence;

namespace UJMW.TestClient.Auth {

  /// <summary>
  /// Executes an OAuth authorization code flow (optionally with PKCE) inside an embedded WebView2
  /// and exchanges the code for an access token.
  /// </summary>
  internal sealed class OAuthCodeFlowDialog : Form {

    private static readonly HttpClient _HttpClient = new HttpClient();

    private readonly OAuthSettings _Settings;
    private readonly WebView2 _WebView;
    private readonly string _State;
    private readonly string _CodeVerifier;
    private string _AccessToken;
    private string _ErrorMessage;
    private bool _RedirectHandled;

    public OAuthCodeFlowDialog(OAuthSettings settings) {
      _Settings = settings;
      _State = CreateRandomToken(16);
      _CodeVerifier = CreateRandomToken(32);

      this.Text = "OAuth authorization code flow";
      this.Width = 900;
      this.Height = 760;
      this.StartPosition = FormStartPosition.CenterParent;
      this.ShowInTaskbar = false;

      _WebView = new WebView2();
      _WebView.Dock = DockStyle.Fill;
      _WebView.CreationProperties = new CoreWebView2CreationProperties();
      _WebView.CreationProperties.UserDataFolder = Path.Combine(SettingsStore.LocalDataDirectory, "WebView2");
      _WebView.CoreWebView2InitializationCompleted += this.OnCoreWebView2InitializationCompleted;
      this.Controls.Add(_WebView);

      this.Load += this.OnDialogLoad;
    }

    /// <summary>
    /// The issued access token (only set if DialogResult is OK).
    /// </summary>
    public string AccessToken {
      get {
        return _AccessToken;
      }
    }

    /// <summary>
    /// Error description (only set if the flow failed).
    /// </summary>
    public string ErrorMessage {
      get {
        return _ErrorMessage;
      }
    }

    /// <summary>
    /// Validates the settings and starts the WebView2 initialization.
    /// </summary>
    private void OnDialogLoad(object sender, EventArgs e) {
      if (string.IsNullOrWhiteSpace(_Settings.AuthorizeUrl) || string.IsNullOrWhiteSpace(_Settings.ClientId) || string.IsNullOrWhiteSpace(_Settings.RedirectUri)) {
        this.Fail("Authorize URL, client id and redirect URI are required.");
        return;
      }
      _WebView.EnsureCoreWebView2Async(null);
    }

    /// <summary>
    /// Navigates to the authorize endpoint as soon as WebView2 is ready.
    /// </summary>
    private void OnCoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e) {
      if (!e.IsSuccess) {
        this.Fail("WebView2 could not be initialized: " + e.InitializationException.Message);
        return;
      }
      _WebView.CoreWebView2.NavigationStarting += this.OnNavigationStarting;
      _WebView.CoreWebView2.Navigate(this.BuildAuthorizeUrl());
    }

    /// <summary>
    /// Intercepts the navigation to the redirect URI and evaluates code / error / token.
    /// </summary>
    private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e) {
      if (_RedirectHandled || !e.Uri.StartsWith(_Settings.RedirectUri, StringComparison.OrdinalIgnoreCase)) {
        return;
      }
      e.Cancel = true;
      _RedirectHandled = true;

      Uri redirect = new Uri(e.Uri);
      Dictionary<string, string> parameters = ParseParameters(redirect.Query);
      foreach (KeyValuePair<string, string> fragmentParameter in ParseParameters(redirect.Fragment)) {
        parameters[fragmentParameter.Key] = fragmentParameter.Value;
      }

      string error;
      if (parameters.TryGetValue("error", out error)) {
        string description;
        if (parameters.TryGetValue("error_description", out description)) {
          error = error + ": " + description;
        }
        this.Fail(error);
        return;
      }
      string state;
      if (parameters.TryGetValue("state", out state) && state != _State) {
        this.Fail("The returned 'state' does not match the requested one.");
        return;
      }
      string accessToken;
      if (parameters.TryGetValue("access_token", out accessToken)) {
        this.Succeed(accessToken);
        return;
      }
      string code;
      if (!parameters.TryGetValue("code", out code)) {
        this.Fail("The redirect did not contain a 'code'.");
        return;
      }
      this.Text = "Exchanging authorization code...";
      Task.Run(() => this.ExchangeCode(code));
    }

    /// <summary>
    /// Exchanges the authorization code at the token endpoint (runs on a worker thread).
    /// </summary>
    private void ExchangeCode(string code) {
      Dictionary<string, string> form = new Dictionary<string, string>();
      form["grant_type"] = "authorization_code";
      form["code"] = code;
      form["redirect_uri"] = _Settings.RedirectUri;
      form["client_id"] = _Settings.ClientId;
      if (!string.IsNullOrEmpty(_Settings.ClientSecret)) {
        form["client_secret"] = _Settings.ClientSecret;
      }
      if (_Settings.UsePkce) {
        form["code_verifier"] = _CodeVerifier;
      }

      string accessToken = null;
      string error = null;
      try {
        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _Settings.TokenUrl)) {
          request.Content = new FormUrlEncodedContent(form);
          using (HttpResponseMessage response = _HttpClient.Send(request)) {
            string body;
            using (StreamReader reader = new StreamReader(response.Content.ReadAsStream())) {
              body = reader.ReadToEnd();
            }
            if (!response.IsSuccessStatusCode) {
              error = $"Token endpoint returned HTTP {(int)response.StatusCode}: {body}";
            } else {
              JObject tokenResponse = JObject.Parse(body);
              accessToken = tokenResponse.Value<string>("access_token");
              if (string.IsNullOrEmpty(accessToken)) {
                error = "The token response did not contain an 'access_token': " + body;
              }
            }
          }
        }
      } catch (HttpRequestException ex) {
        error = "Token request failed: " + ex.Message;
      } catch (TaskCanceledException) {
        error = "Token request timed out.";
      } catch (UriFormatException ex) {
        error = "Invalid token URL: " + ex.Message;
      } catch (InvalidOperationException ex) {
        error = "Invalid token URL: " + ex.Message;
      } catch (JsonReaderException ex) {
        error = "The token response is no valid JSON: " + ex.Message;
      }

      this.BeginInvoke(new Action(() => {
        if (error != null) {
          this.Fail(error);
        } else {
          this.Succeed(accessToken);
        }
      }));
    }

    /// <summary>
    /// Builds the authorize URL including state and (optionally) the PKCE challenge.
    /// </summary>
    private string BuildAuthorizeUrl() {
      StringBuilder url = new StringBuilder(_Settings.AuthorizeUrl.Trim());
      if (_Settings.AuthorizeUrl.Contains('?')) {
        url.Append('&');
      } else {
        url.Append('?');
      }
      url.Append("response_type=code");
      url.Append("&client_id=").Append(Uri.EscapeDataString(_Settings.ClientId));
      url.Append("&redirect_uri=").Append(Uri.EscapeDataString(_Settings.RedirectUri));
      if (!string.IsNullOrWhiteSpace(_Settings.Scope)) {
        url.Append("&scope=").Append(Uri.EscapeDataString(_Settings.Scope));
      }
      url.Append("&state=").Append(Uri.EscapeDataString(_State));
      if (_Settings.UsePkce) {
        byte[] challengeHash = SHA256.HashData(Encoding.ASCII.GetBytes(_CodeVerifier));
        url.Append("&code_challenge=").Append(ToBase64Url(challengeHash));
        url.Append("&code_challenge_method=S256");
      }
      return url.ToString();
    }

    /// <summary>
    /// Completes the dialog successfully.
    /// </summary>
    private void Succeed(string accessToken) {
      _AccessToken = accessToken;
      this.DialogResult = DialogResult.OK;
    }

    /// <summary>
    /// Completes the dialog with an error.
    /// </summary>
    private void Fail(string errorMessage) {
      _ErrorMessage = errorMessage;
      this.DialogResult = DialogResult.Abort;
    }

    /// <summary>
    /// Parses a query string or fragment ('?a=1&amp;b=2' / '#a=1').
    /// </summary>
    private static Dictionary<string, string> ParseParameters(string text) {
      Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
      if (string.IsNullOrEmpty(text)) {
        return result;
      }
      foreach (string pair in text.TrimStart('?', '#').Split('&', StringSplitOptions.RemoveEmptyEntries)) {
        int separatorIndex = pair.IndexOf('=');
        if (separatorIndex < 0) {
          result[Uri.UnescapeDataString(pair)] = string.Empty;
        } else {
          string key = Uri.UnescapeDataString(pair.Substring(0, separatorIndex).Replace('+', ' '));
          string value = Uri.UnescapeDataString(pair.Substring(separatorIndex + 1).Replace('+', ' '));
          result[key] = value;
        }
      }
      return result;
    }

    /// <summary>
    /// Creates a cryptographically random base64url token.
    /// </summary>
    private static string CreateRandomToken(int byteCount) {
      return ToBase64Url(RandomNumberGenerator.GetBytes(byteCount));
    }

    /// <summary>
    /// Encodes bytes as base64url (without padding).
    /// </summary>
    private static string ToBase64Url(byte[] bytes) {
      return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

  }

}
