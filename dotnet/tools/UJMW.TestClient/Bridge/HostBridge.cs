using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using UJMW.TestClient.Auth;
using UJMW.TestClient.Inspection;
using UJMW.TestClient.Invocation;
using UJMW.TestClient.Model;
using UJMW.TestClient.Persistence;

namespace UJMW.TestClient.Bridge {

  /// <summary>
  /// Dispatches all requests of the WebView2 UI ('{id, cmd, args}') and replies with '{id, ok, result, error}'.
  /// All commands are executed on the UI thread (outside of the WebView2 event handler);
  /// the UJMW invocation itself runs on a worker thread.
  /// </summary>
  internal sealed class HostBridge {

    private readonly Form _Owner;
    private readonly WebView2 _WebView;
    private readonly SettingsStore _Store;
    private readonly JsonSerializer _Serializer;

    public HostBridge(Form owner, WebView2 webView, SettingsStore store) {
      _Owner = owner;
      _WebView = webView;
      _Store = store;
      JsonSerializerSettings serializerSettings = new JsonSerializerSettings();
      serializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
      _Serializer = JsonSerializer.Create(serializerSettings);
    }

    private AppSettings Settings {
      get {
        return _Store.Current;
      }
    }

    /// <summary>
    /// Entry point for messages posted by the UI.
    /// </summary>
    public void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e) {
      JObject message = JObject.Parse(e.WebMessageAsJson);
      long id = message.Value<long>("id");
      string command = message.Value<string>("cmd");
      JObject args = message["args"] as JObject;
      if (args == null) {
        args = new JObject();
      }

      //additional objects (dropped files) are only accessible within this event handler
      string[] droppedPaths = new string[0];
      if (command == "dropFiles") {
        droppedPaths = ReadDroppedPaths(e);
      }

      _Owner.BeginInvoke(new Action(() => this.Dispatch(id, command, args, droppedPaths)));
    }

    /// <summary>
    /// Executes a command and posts the reply.
    /// </summary>
    private void Dispatch(long id, string command, JObject args, string[] droppedPaths) {
      if (command == "execute") {
        this.BeginExecute(id, args);
        return;
      }
      JToken result;
      try {
        result = this.ExecuteCommand(command, args, droppedPaths);
      } catch (Exception ex) {
        //bridge boundary: every failure is reported to the UI (instead of terminating the host)
        this.PostReply(id, false, null, ex.Message);
        return;
      }
      this.PostReply(id, true, result, null);
    }

    /// <summary>
    /// Executes all synchronous commands.
    /// </summary>
    private JToken ExecuteCommand(string command, JObject args, string[] droppedPaths) {
      switch (command) {
        case "getState":
          return this.BuildState();
        case "addDllsViaDialog":
          this.AddDllsViaDialog();
          return this.BuildState();
        case "dropFiles":
          this.AddDlls(droppedPaths);
          return this.BuildState();
        case "removeDll":
          this.RemoveDll(args.Value<string>("path"));
          return this.BuildState();
        case "getDllTypes":
          return JToken.FromObject(AssemblyInspector.GetNamespaces(args.Value<string>("path")), _Serializer);
        case "toggleType":
          this.ToggleType(args.Value<string>("path"), args.Value<string>("fullName"), args.Value<string>("kind"));
          return this.BuildState();
        case "getTypeDetail":
          return this.GetTypeDetail(args.Value<string>("path"), args.Value<string>("typeFullName"));
        case "touchUrl":
          this.TouchUrl(args.Value<string>("typeFullName"), args.Value<string>("url"));
          return this.BuildUrlHistory(args.Value<string>("typeFullName"));
        case "openBrowser":
          OpenBrowser(args.Value<string>("url"));
          return JValue.CreateNull();
        case "savePreset":
          this.SavePreset(args["preset"].ToObject<Preset>(_Serializer));
          return this.BuildState();
        case "renamePreset":
          this.RenamePreset(args.Value<string>("typeFullName"), args.Value<string>("oldName"), args.Value<string>("newName"));
          return this.BuildState();
        case "renameFolder":
          this.RenameFolder(args.Value<string>("typeFullName"), args.Value<string>("oldPath"), args.Value<string>("newPath"));
          return this.BuildState();
        case "deletePreset":
          this.DeletePresets(args.Value<string>("typeFullName"), (p) => p.Name == NormalizePresetName(args.Value<string>("name")));
          return this.BuildState();
        case "deleteFolder":
          string folderPrefix = NormalizePresetName(args.Value<string>("path")) + "/";
          this.DeletePresets(args.Value<string>("typeFullName"), (p) => p.Name.StartsWith(folderPrefix, StringComparison.Ordinal));
          return this.BuildState();
        case "saveSideChannelTemplate":
          this.SaveSideChannelTemplate(args.Value<string>("name"), args.Value<string>("content"));
          return this.BuildState();
        case "deleteSideChannelTemplate":
          this.DeleteSideChannelTemplate(args.Value<string>("name"));
          return this.BuildState();
        case "setSideChannelText":
          this.Settings.SideChannelText = args.Value<string>("text");
          _Store.Save();
          return JValue.CreateNull();
        case "setLayout":
          this.Settings.LeftPanelWidth = args.Value<int>("leftPanelWidth");
          _Store.Save();
          return JValue.CreateNull();
        case "saveZones":
          this.SaveZones(args["zones"].ToObject<InfrastructureZone[]>(_Serializer));
          return this.BuildState();
        case "generateJwt":
          return this.GenerateJwt(args["jwt"].ToObject<JwtGeneratorSettings>(_Serializer));
        case "startOAuth":
          return this.StartOAuth(args["oauth"].ToObject<OAuthSettings>(_Serializer));
        case "resolveUrls":
          return this.ResolveUrls(args.Value<string>("zoneName"), args.Value<string>("typeFullName"));
        default:
          throw new NotSupportedException($"Unknown command '{command}'.");
      }
    }

    #region " State "

    /// <summary>
    /// Builds the complete UI state snapshot.
    /// </summary>
    private JObject BuildState() {
      JArray dlls = new JArray();
      foreach (DllEntry dll in this.Settings.Dlls) {
        JObject item = new JObject();
        item["path"] = dll.Path;
        item["displayName"] = AssemblyInspector.GetDisplayName(dll.Path);
        item["exists"] = File.Exists(dll.Path);
        item["checkedTypes"] = JToken.FromObject(dll.CheckedTypes, _Serializer);
        dlls.Add(item);
      }
      JObject state = new JObject();
      state["dlls"] = dlls;
      state["presets"] = JToken.FromObject(this.Settings.Presets, _Serializer);
      state["sideChannelTemplates"] = JToken.FromObject(this.Settings.SideChannelTemplates, _Serializer);
      state["zones"] = JToken.FromObject(this.Settings.Zones, _Serializer);
      state["jwt"] = JToken.FromObject(this.Settings.Jwt, _Serializer);
      state["oauth"] = JToken.FromObject(this.Settings.OAuth, _Serializer);
      state["sideChannelText"] = this.Settings.SideChannelText;
      state["leftPanelWidth"] = this.Settings.LeftPanelWidth;
      return state;
    }

    #endregion

    #region " DLLs and types "

    /// <summary>
    /// Shows the file open dialog and adds the selected assemblies.
    /// </summary>
    private void AddDllsViaDialog() {
      using (OpenFileDialog dialog = new OpenFileDialog()) {
        dialog.Title = "Add assemblies";
        dialog.Filter = "Assemblies (*.dll;*.exe)|*.dll;*.exe|All files (*.*)|*.*";
        dialog.Multiselect = true;
        if (dialog.ShowDialog(_Owner) == DialogResult.OK) {
          this.AddDlls(dialog.FileNames);
        }
      }
    }

    /// <summary>
    /// Adds assemblies which are not yet registered.
    /// </summary>
    private void AddDlls(string[] paths) {
      List<DllEntry> dlls = this.Settings.Dlls.ToList();
      foreach (string path in paths) {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".dll" && extension != ".exe") {
          continue;
        }
        string fullPath = Path.GetFullPath(path);
        if (dlls.Any((d) => string.Equals(d.Path, fullPath, StringComparison.OrdinalIgnoreCase))) {
          continue;
        }
        DllEntry entry = new DllEntry();
        entry.Path = fullPath;
        dlls.Add(entry);
      }
      this.Settings.Dlls = dlls.ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Removes an assembly from the list.
    /// </summary>
    private void RemoveDll(string path) {
      this.Settings.Dlls = this.Settings.Dlls.Where((d) => !string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)).ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Checks or unchecks a type of an assembly (static classes can not be checked).
    /// </summary>
    private void ToggleType(string path, string fullName, string kind) {
      if (kind == AssemblyInspector._KindStatic) {
        throw new InvalidOperationException("Static classes can not be selected.");
      }
      DllEntry dll = this.FindDll(path);
      List<CheckedType> checkedTypes = dll.CheckedTypes.ToList();
      CheckedType existing = checkedTypes.FirstOrDefault((t) => t.FullName == fullName);
      if (existing != null) {
        checkedTypes.Remove(existing);
      } else {
        CheckedType checkedType = new CheckedType();
        checkedType.FullName = fullName;
        checkedType.Kind = kind;
        checkedTypes.Add(checkedType);
      }
      dll.CheckedTypes = checkedTypes.OrderBy((t) => t.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Returns the methods of a type and its URL history.
    /// </summary>
    private JObject GetTypeDetail(string path, string typeFullName) {
      JObject detail = new JObject();
      if (File.Exists(path)) {
        detail["methods"] = JToken.FromObject(AssemblyInspector.GetMethods(path, typeFullName), _Serializer);
        detail["warning"] = null;
      } else {
        detail["methods"] = new JArray();
        detail["warning"] = $"The file '{path}' does not exist (anymore) - methods can not be listed.";
      }
      detail["urlHistory"] = this.BuildUrlHistory(typeFullName);
      return detail;
    }

    /// <summary>
    /// Returns the registered DLL entry or throws.
    /// </summary>
    private DllEntry FindDll(string path) {
      DllEntry dll = this.Settings.Dlls.FirstOrDefault((d) => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
      if (dll == null) {
        throw new InvalidOperationException($"The DLL '{path}' is not registered.");
      }
      return dll;
    }

    /// <summary>
    /// Reads the file system paths of files which were dropped onto the UI.
    /// </summary>
    private static string[] ReadDroppedPaths(CoreWebView2WebMessageReceivedEventArgs e) {
      List<string> paths = new List<string>();
      if (e.AdditionalObjects == null) {
        return paths.ToArray();
      }
      foreach (object additionalObject in e.AdditionalObjects) {
        CoreWebView2File file = additionalObject as CoreWebView2File;
        if (file != null) {
          paths.Add(file.Path);
        }
      }
      return paths.ToArray();
    }

    #endregion

    #region " URL history "

    /// <summary>
    /// Adds the URL to the history of the type or refreshes its last-used timestamp.
    /// </summary>
    private void TouchUrl(string typeFullName, string url) {
      if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(typeFullName)) {
        return;
      }
      url = url.Trim();
      UrlHistoryEntry entry = this.Settings.UrlHistory.FirstOrDefault((h) => h.TypeFullName == typeFullName && h.Url == url);
      if (entry == null) {
        entry = new UrlHistoryEntry();
        entry.TypeFullName = typeFullName;
        entry.Url = url;
        List<UrlHistoryEntry> history = this.Settings.UrlHistory.ToList();
        history.Add(entry);
        this.Settings.UrlHistory = history.ToArray();
      }
      entry.LastUsedUtc = DateTime.UtcNow;
      _Store.Save();
    }

    /// <summary>
    /// Returns the URL history of a type ordered by last usage (descending).
    /// </summary>
    private JArray BuildUrlHistory(string typeFullName) {
      IEnumerable<string> urls = this.Settings.UrlHistory
        .Where((h) => h.TypeFullName == typeFullName)
        .OrderByDescending((h) => h.LastUsedUtc)
        .Select((h) => h.Url);
      return new JArray(urls);
    }

    /// <summary>
    /// Opens the URL in the default browser.
    /// </summary>
    private static void OpenBrowser(string url) {
      Uri uri;
      if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
        throw new InvalidOperationException($"'{url}' is no valid http(s) URL.");
      }
      ProcessStartInfo startInfo = new ProcessStartInfo(uri.AbsoluteUri);
      startInfo.UseShellExecute = true;
      Process.Start(startInfo);
    }

    #endregion

    #region " Invocation "

    /// <summary>
    /// Persists the sidechannel text and invokes the UJMW operation on a worker thread.
    /// </summary>
    private void BeginExecute(long id, JObject args) {
      string url = args.Value<string>("url");
      string methodName = args.Value<string>("methodName");
      string authHeader = args.Value<string>("authHeader");
      string sideChannel = args.Value<string>("sideChannel");
      string requestText = args.Value<string>("requestText");

      if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(methodName)) {
        this.PostReply(id, false, null, "URL and method are required.");
        return;
      }
      if (requestText == null) {
        requestText = string.Empty;
      }

      this.Settings.SideChannelText = sideChannel;
      _Store.Save();

      Task.Run(() => {
        InvocationResult invocationResult = UjmwInvoker.Invoke(url, methodName, authHeader, sideChannel, requestText);
        _Owner.BeginInvoke(new Action(() => this.PostReply(id, true, JToken.FromObject(invocationResult, _Serializer), null)));
      });
    }

    #endregion

    #region " Presets "

    /// <summary>
    /// Creates or overwrites a preset (the overwrite warning is shown by the UI).
    /// </summary>
    private void SavePreset(Preset preset) {
      preset.Name = NormalizePresetName(preset.Name);
      if (preset.Name.Length == 0) {
        throw new InvalidOperationException("The preset name must not be empty.");
      }
      if (string.IsNullOrWhiteSpace(preset.TypeFullName)) {
        throw new InvalidOperationException("The preset must be related to a type.");
      }
      List<Preset> presets = this.Settings.Presets.Where((p) => !(p.TypeFullName == preset.TypeFullName && p.Name == preset.Name)).ToList();
      presets.Add(preset);
      this.Settings.Presets = presets.ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Renames (moves) a single preset.
    /// </summary>
    private void RenamePreset(string typeFullName, string oldName, string newName) {
      oldName = NormalizePresetName(oldName);
      newName = NormalizePresetName(newName);
      if (newName.Length == 0) {
        throw new InvalidOperationException("The preset name must not be empty.");
      }
      if (newName == oldName) {
        return;
      }
      Preset preset = this.Settings.Presets.FirstOrDefault((p) => p.TypeFullName == typeFullName && p.Name == oldName);
      if (preset == null) {
        throw new InvalidOperationException($"The preset '{oldName}' does not exist.");
      }
      if (this.Settings.Presets.Any((p) => p.TypeFullName == typeFullName && p.Name == newName)) {
        throw new InvalidOperationException($"A preset named '{newName}' already exists.");
      }
      preset.Name = newName;
      _Store.Save();
    }

    /// <summary>
    /// Renames a virtual folder, which is applied to all contained presets.
    /// </summary>
    private void RenameFolder(string typeFullName, string oldPath, string newPath) {
      oldPath = NormalizePresetName(oldPath);
      newPath = NormalizePresetName(newPath);
      if (oldPath.Length == 0 || oldPath == newPath) {
        return;
      }
      string oldPrefix = oldPath + "/";
      Preset[] affectedPresets = this.Settings.Presets.Where((p) => p.TypeFullName == typeFullName && p.Name.StartsWith(oldPrefix, StringComparison.Ordinal)).ToArray();

      Dictionary<Preset, string> newNames = new Dictionary<Preset, string>();
      foreach (Preset preset in affectedPresets) {
        string relativeName = preset.Name.Substring(oldPrefix.Length);
        if (newPath.Length == 0) {
          newNames[preset] = relativeName;
        } else {
          newNames[preset] = newPath + "/" + relativeName;
        }
      }

      foreach (string newName in newNames.Values) {
        bool conflict = this.Settings.Presets.Any((p) => p.TypeFullName == typeFullName && p.Name == newName && !newNames.ContainsKey(p));
        if (conflict) {
          throw new InvalidOperationException($"A preset named '{newName}' already exists.");
        }
      }

      foreach (KeyValuePair<Preset, string> entry in newNames) {
        entry.Key.Name = entry.Value;
      }
      _Store.Save();
    }

    /// <summary>
    /// Deletes all presets of the type which match the filter.
    /// </summary>
    private void DeletePresets(string typeFullName, Func<Preset, bool> filter) {
      this.Settings.Presets = this.Settings.Presets.Where((p) => !(p.TypeFullName == typeFullName && filter(p))).ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Trims all segments of a preset name and removes empty segments ('a / /b/' -> 'a/b').
    /// </summary>
    private static string NormalizePresetName(string name) {
      if (name == null) {
        return string.Empty;
      }
      IEnumerable<string> segments = name.Split('/').Select((s) => s.Trim()).Where((s) => s.Length > 0);
      return string.Join("/", segments);
    }

    #endregion

    #region " SideChannel templates and zones "

    /// <summary>
    /// Creates or overwrites a sidechannel template.
    /// </summary>
    private void SaveSideChannelTemplate(string name, string content) {
      if (string.IsNullOrWhiteSpace(name)) {
        throw new InvalidOperationException("The template name must not be empty.");
      }
      name = name.Trim();
      List<SideChannelTemplate> templates = this.Settings.SideChannelTemplates.Where((t) => t.Name != name).ToList();
      SideChannelTemplate template = new SideChannelTemplate();
      template.Name = name;
      template.Content = content;
      templates.Add(template);
      this.Settings.SideChannelTemplates = templates.OrderBy((t) => t.Name, StringComparer.OrdinalIgnoreCase).ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Deletes a sidechannel template.
    /// </summary>
    private void DeleteSideChannelTemplate(string name) {
      this.Settings.SideChannelTemplates = this.Settings.SideChannelTemplates.Where((t) => t.Name != name).ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Replaces the list of infrastructure zones.
    /// </summary>
    private void SaveZones(InfrastructureZone[] zones) {
      if (zones == null) {
        zones = new InfrastructureZone[0];
      }
      this.Settings.Zones = zones.Where((z) => z != null && !string.IsNullOrWhiteSpace(z.Name)).ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Placeholder for the ApiCatalog based URL resolution (not specified yet).
    /// </summary>
    private JObject ResolveUrls(string zoneName, string typeFullName) {
      InfrastructureZone zone = this.Settings.Zones.FirstOrDefault((z) => z.Name == zoneName);
      if (zone == null) {
        throw new InvalidOperationException($"The infrastructure zone '{zoneName}' does not exist.");
      }
      JObject result = new JObject();
      result["urls"] = new JArray();
      result["message"] = $"The ApiCatalog based resolution is not specified yet. In a future version the ApiCatalog '{zone.ApiCatalogUrl}' of zone '{zone.Name}' will be queried for '{typeFullName}'.";
      return result;
    }

    #endregion

    #region " Auth "

    /// <summary>
    /// Persists the JWT generator inputs and creates a token.
    /// </summary>
    private JToken GenerateJwt(JwtGeneratorSettings jwtSettings) {
      this.Settings.Jwt = jwtSettings;
      _Store.Save();
      return new JValue(JwtTokenGenerator.Generate(jwtSettings));
    }

    /// <summary>
    /// Persists the OAuth inputs and runs the code flow within a modal WebView2 dialog.
    /// </summary>
    private JToken StartOAuth(OAuthSettings oauthSettings) {
      this.Settings.OAuth = oauthSettings;
      _Store.Save();
      using (OAuthCodeFlowDialog dialog = new OAuthCodeFlowDialog(oauthSettings)) {
        DialogResult dialogResult = dialog.ShowDialog(_Owner);
        JObject result = new JObject();
        if (dialogResult == DialogResult.OK) {
          result["token"] = dialog.AccessToken;
          result["cancelled"] = false;
          return result;
        }
        if (dialog.ErrorMessage != null) {
          throw new InvalidOperationException(dialog.ErrorMessage);
        }
        result["token"] = null;
        result["cancelled"] = true;
        return result;
      }
    }

    #endregion

    /// <summary>
    /// Posts a reply to the UI.
    /// </summary>
    private void PostReply(long id, bool ok, JToken result, string error) {
      if (_WebView.CoreWebView2 == null) {
        return;
      }
      JObject reply = new JObject();
      reply["id"] = id;
      reply["ok"] = ok;
      if (result == null) {
        reply["result"] = JValue.CreateNull();
      } else {
        reply["result"] = result;
      }
      reply["error"] = error;
      _WebView.CoreWebView2.PostWebMessageAsJson(reply.ToString(Formatting.None));
    }

  }

}
