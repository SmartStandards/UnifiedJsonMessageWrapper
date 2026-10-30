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
using System.Web.UJMW;
using UJMW.TestClient.Auth;
using UJMW.TestClient.Infrastructure;
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
    private string _StartupDllPath = null;

    public HostBridge(Form owner, WebView2 webView, SettingsStore store) {
      _Owner = owner;
      _WebView = webView;
      _Store = store;
      JsonSerializerSettings serializerSettings = new JsonSerializerSettings();
      serializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
      _Serializer = JsonSerializer.Create(serializerSettings);
    }

    private const string _DefaultOAuthRedirectUri = "http://localhost/oauth-callback";

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
      if (_InfrastructureCommands.Contains(command)) {
        this.BeginInfrastructureCommand(id, command, args);
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
        case "removeType":
          this.RemoveType(args.Value<string>("path"), args.Value<string>("fullName"));
          return this.BuildState();
        case "getEntityClass":
          return JToken.FromObject(AssemblyInspector.GetEntityClass(args.Value<string>("path"), args.Value<string>("fullName")), _Serializer);
        case "addRepository":
          this.AddRepository(
            args.Value<string>("path"),
            args.Value<string>("entityFullName"),
            args.Value<string>("keyTypeFullName"),
            args["keyPropertyNames"].ToObject<string[]>()
          );
          return this.BuildState();
        case "getTypeDetail":
          return this.GetTypeDetail(args.Value<string>("path"), args.Value<string>("typeFullName"));
        case "setClientSettings":
          this.SetClientSettings(args.Value<int>("requestTimeoutSeconds"));
          return this.BuildState();
        case "setCurrentZone":
          this.Settings.CurrentZone = args.Value<string>("zone");
          _Store.Save();
          return JValue.CreateNull();
        case "saveInfrastructureSettings":
          this.SaveInfrastructureSettings(args["infrastructure"].ToObject<InfrastructureSettings>(_Serializer));
          return this.BuildState();
        case "takeStartupDll":
          return this.TakeStartupDll();
        case "selectFileBasedStorePath":
          return this.SelectFileBasedStorePath(args.Value<string>("currentPath"));
        case "getDefaultFileBasedStorePath":
          return new JValue(InfrastructureProvider.DefaultFileBasedStorePath);
        case "registerProvider":
          this.RegisterProvider(args.Value<string>("path"), args.Value<string>("typeFullName"));
          return this.BuildState();
        case "unregisterProvider":
          this.UnregisterProvider(args.Value<string>("typeFullName"));
          return this.BuildState();
        case "getPinnedEndpoints":
          return this.BuildPinnedEndpoints(args.Value<string>("typeFullName"));
        case "savePinnedEndpoint":
          this.SavePinnedEndpoint(args["pin"].ToObject<PinnedEndpoint>(_Serializer), args.Value<string>("oldUrl"));
          return this.BuildPinnedEndpoints(args["pin"].Value<string>("typeFullName"));
        case "deletePinnedEndpoint":
          this.DeletePinnedEndpoint(args.Value<string>("typeFullName"), args.Value<string>("url"));
          return this.BuildPinnedEndpoints(args.Value<string>("typeFullName"));
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
        case "setContractSession":
          this.SetContractSession(args.Value<string>("typeFullName"), args.Value<string>("url"));
          return JValue.CreateNull();
        case "setSharedAuthHeader":
          this.Settings.SharedAuthHeader = args.Value<string>("authHeader");
          if (this.Settings.SharedAuthHeader == null) {
            this.Settings.SharedAuthHeader = string.Empty;
          }
          _Store.Save();
          return JValue.CreateNull();
        case "setSideChannelText":
          this.Settings.SideChannelText = args.Value<string>("text");
          _Store.Save();
          return JValue.CreateNull();
        case "setLayout":
          this.Settings.LeftPanelWidth = args.Value<int>("leftPanelWidth");
          _Store.Save();
          return JValue.CreateNull();
        case "generateJwt":
          return this.GenerateJwt(args["jwt"].ToObject<JwtGeneratorSettings>(_Serializer));
        case "startOAuth":
          return this.StartOAuth(args["oauth"].ToObject<OAuthSettings>(_Serializer));
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
      state["jwt"] = JToken.FromObject(this.Settings.Jwt, _Serializer);
      state["requestTimeoutSeconds"] = this.Settings.RequestTimeoutSeconds;
      state["currentZone"] = this.Settings.CurrentZone;
      state["infrastructureSettings"] = JToken.FromObject(this.Settings.Infrastructure, _Serializer);
      state["oauth"] = JToken.FromObject(this.Settings.OAuth, _Serializer);
      state["sideChannelText"] = this.Settings.SideChannelText;
      state["sharedAuthHeader"] = this.Settings.SharedAuthHeader;
      state["leftPanelWidth"] = this.Settings.LeftPanelWidth;
      return state;
    }

    #endregion

    #region " DLLs and types "

    /// <summary>
    /// Sets the DLL which was passed as command line argument ('open with').
    /// It is added to the list (if not yet present) and selected by the UI after startup.
    /// </summary>
    public void SetStartupDll(string dllPath) {
      if (string.IsNullOrWhiteSpace(dllPath)) {
        return;
      }
      string fullPath = Path.GetFullPath(dllPath.Trim().Trim('"'));
      this.AddDlls(new string[] { fullPath });
      _StartupDllPath = fullPath;
    }

    /// <summary>
    /// Returns the startup DLL path once (null afterwards), so that it is selected only after the start.
    /// </summary>
    private JToken TakeStartupDll() {
      string path = _StartupDllPath;
      _StartupDllPath = null;
      if (path == null) {
        return JValue.CreateNull();
      }
      DllEntry dll = this.Settings.Dlls.FirstOrDefault((d) => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
      if (dll == null) {
        return JValue.CreateNull();
      }
      return new JValue(dll.Path);
    }

    /// <summary>
    /// Registers a type of an added DLL as store provider (the registration survives the removal of the DLL).
    /// </summary>
    private void RegisterProvider(string path, string typeFullName) {
      if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(typeFullName)) {
        throw new InvalidOperationException("DLL path and type name are required.");
      }
      NamespaceDescriptor[] namespaces = AssemblyInspector.GetNamespaces(path);
      bool isProvider = namespaces.SelectMany((n) => n.Types).Any((t) => t.FullName == typeFullName && t.IsInfrastructureProvider);
      if (!isProvider) {
        throw new InvalidOperationException($"'{typeFullName}' is no usable IUjmwManagedInfrastructure implementation.");
      }
      List<RegisteredProvider> providers = this.Settings.Infrastructure.RegisteredProviders
        .Where((p) => p.TypeFullName != typeFullName)
        .ToList();
      RegisteredProvider provider = new RegisteredProvider();
      provider.DllPath = Path.GetFullPath(path);
      provider.TypeFullName = typeFullName;
      providers.Add(provider);
      this.Settings.Infrastructure.RegisteredProviders = providers.OrderBy((p) => p.TypeFullName, StringComparer.OrdinalIgnoreCase).ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Removes a provider registration (falls back to the local store when it was the active one).
    /// </summary>
    private void UnregisterProvider(string typeFullName) {
      InfrastructureSettings infrastructureSettings = this.Settings.Infrastructure;
      infrastructureSettings.RegisteredProviders = infrastructureSettings.RegisteredProviders
        .Where((p) => p.TypeFullName != typeFullName)
        .ToArray();
      if (infrastructureSettings.Mode == InfrastructureSettings._ModeProvider && infrastructureSettings.ProviderTypeName == typeFullName) {
        infrastructureSettings.Mode = InfrastructureSettings._ModeFileBased;
        infrastructureSettings.ProviderTypeName = string.Empty;
        lock (_InfrastructureLock) {
          _Infrastructure = null;
        }
      }
      _Store.Save();
    }

    /// <summary>
    /// Returns the user-specific pinned endpoints of a contract.
    /// </summary>
    private JArray BuildPinnedEndpoints(string typeFullName) {
      PinnedEndpoint[] pins = this.Settings.PinnedEndpoints.Where((p) => p.TypeFullName == typeFullName).ToArray();
      return (JArray)JToken.FromObject(pins, _Serializer);
    }

    /// <summary>
    /// Adds or replaces (when 'oldUrl' is given) a user-specific pinned endpoint.
    /// </summary>
    private void SavePinnedEndpoint(PinnedEndpoint pin, string oldUrl) {
      if (pin == null || string.IsNullOrWhiteSpace(pin.TypeFullName) || string.IsNullOrWhiteSpace(pin.Url)) {
        throw new InvalidOperationException("Contract and url are required.");
      }
      pin.Url = pin.Url.Trim();
      string replacedUrl = pin.Url;
      if (!string.IsNullOrWhiteSpace(oldUrl)) {
        replacedUrl = oldUrl.Trim();
      }
      List<PinnedEndpoint> pins = this.Settings.PinnedEndpoints.Where(
        (p) => !(p.TypeFullName == pin.TypeFullName && (string.Equals(p.Url, replacedUrl, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Url, pin.Url, StringComparison.OrdinalIgnoreCase)))
      ).ToList();
      pins.Add(pin);
      this.Settings.PinnedEndpoints = pins.ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Removes a user-specific pinned endpoint.
    /// </summary>
    private void DeletePinnedEndpoint(string typeFullName, string url) {
      this.Settings.PinnedEndpoints = this.Settings.PinnedEndpoints.Where(
        (p) => !(p.TypeFullName == typeFullName && string.Equals(p.Url, url, StringComparison.OrdinalIgnoreCase))
      ).ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Lets the user choose the JSON file of the file based store (e.g. on a network share).
    /// Returns the selected path or null if the dialog was cancelled.
    /// </summary>
    private JToken SelectFileBasedStorePath(string currentPath) {

      string initialPath = InfrastructureProvider.DefaultFileBasedStorePath;
      if (!string.IsNullOrWhiteSpace(currentPath)) {
        initialPath = Environment.ExpandEnvironmentVariables(currentPath.Trim());
      }

      using (SaveFileDialog dialog = new SaveFileDialog()) {
        dialog.Title = "Location of the file based infrastructure store";
        dialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        dialog.OverwritePrompt = false;
        dialog.CheckPathExists = true;
        dialog.FileName = Path.GetFileName(initialPath);
        string initialDirectory = Path.GetDirectoryName(initialPath);
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory)) {
          dialog.InitialDirectory = initialDirectory;
        }
        if (dialog.ShowDialog(_Owner) != DialogResult.OK) {
          return JValue.CreateNull();
        }
        return new JValue(dialog.FileName);
      }
    }

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
    /// Removes a checked type (also a synthetic repository) from a DLL. Unknown types are ignored.
    /// </summary>
    private void RemoveType(string path, string fullName) {

      DllEntry dll = this.FindDll(path);
      dll.CheckedTypes = dll.CheckedTypes.Where((t) => t.FullName != fullName).ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Adds a synthetic FUSE repository contract ('IRepository&lt;TEntity, TKey&gt;') for an entity class.
    /// The contract is built once for validation (e.g. unresolvable key types or a missing FUSE-fx contract assembly).
    /// </summary>
    private void AddRepository(string path, string entityFullName, string keyTypeFullName, string[] keyPropertyNames) {

      if (string.IsNullOrWhiteSpace(entityFullName)) {
        throw new InvalidOperationException("The entity type is required.");
      }
      if (keyPropertyNames == null) {
        keyPropertyNames = new string[0];
      }
      if (keyTypeFullName == null) {
        keyTypeFullName = string.Empty;
      }
      if (keyPropertyNames.Length > 1) {
        keyTypeFullName = string.Empty;
      }

      DllEntry dll = this.FindDll(path);
      ContractDescriptor contract = AssemblyInspector.GetRepositoryContract(dll.Path, entityFullName, keyTypeFullName, keyPropertyNames);
      if (contract.FuseEntity == null) {
        throw new InvalidOperationException($"The repository contract for '{entityFullName}' could not be built.");
      }

      string displayName = "IRepository<" + contract.FuseEntity.EntityName + ", " + contract.FuseEntity.KeyTypeName + ">";
      string fullName = "System.Data.Fuse.IRepository<" + entityFullName + ", " + contract.FuseEntity.KeyTypeName + ">";

      List<CheckedType> checkedTypes = dll.CheckedTypes.Where((t) => t.FullName != fullName).ToList();
      CheckedType checkedType = new CheckedType();
      checkedType.FullName = fullName;
      checkedType.Kind = AssemblyInspector._KindInterface;
      checkedType.DisplayName = displayName;
      checkedType.RepositoryEntityFullName = entityFullName;
      checkedType.RepositoryKeyTypeFullName = keyTypeFullName;
      checkedType.RepositoryKeyPropertyNames = keyPropertyNames;
      checkedTypes.Add(checkedType);

      dll.CheckedTypes = checkedTypes.OrderBy((t) => t.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
      _Store.Save();
    }

    /// <summary>
    /// Returns the methods of a type and its URL history.
    /// </summary>
    private JObject GetTypeDetail(string path, string typeFullName) {
      JObject detail = new JObject();
      CheckedType repositoryType = this.Settings.Dlls
        .SelectMany((d) => d.CheckedTypes)
        .FirstOrDefault((t) => t.FullName == typeFullName && !string.IsNullOrEmpty(t.RepositoryEntityFullName));
      if (File.Exists(path)) {
        ContractDescriptor contract;
        if (repositoryType != null) {
          contract = AssemblyInspector.GetRepositoryContract(path, repositoryType.RepositoryEntityFullName, repositoryType.RepositoryKeyTypeFullName, repositoryType.RepositoryKeyPropertyNames);
        } else {
          contract = AssemblyInspector.GetContract(path, typeFullName);
        }
        detail["methods"] = JToken.FromObject(contract.Methods, _Serializer);
        detail["summary"] = contract.Summary;
        detail["hasDocumentationFile"] = contract.HasDocumentationFile;
        detail["warning"] = contract.Warning;
        detail["contractQualifyingName"] = contract.QualifyingContractName;
        if (contract.FuseEntity != null) {
          detail["fuseEntity"] = JToken.FromObject(contract.FuseEntity, _Serializer);
        } else {
          detail["fuseEntity"] = JValue.CreateNull();
        }
      } else {
        detail["methods"] = new JArray();
        detail["summary"] = string.Empty;
        detail["hasDocumentationFile"] = false;
        detail["fuseEntity"] = JValue.CreateNull();
        detail["contractQualifyingName"] = string.Empty;
        detail["warning"] = $"The file '{path}' does not exist (anymore) - methods can not be listed.";
      }
      detail["urlHistory"] = this.BuildUrlHistory(typeFullName);
      ContractSession session = this.Settings.ContractSessions.FirstOrDefault((s) => s.TypeFullName == typeFullName);
      if (session != null) {
        detail["session"] = JToken.FromObject(session, _Serializer);
      } else {
        detail["session"] = JValue.CreateNull();
      }
      if (repositoryType != null) {
        detail["contractRouteName"] = UjmwContractNaming.GetContractName(repositoryType.RepositoryEntityFullName, false);
        return detail;
      }
      bool isInterface = this.Settings.Dlls
        .SelectMany((d) => d.CheckedTypes)
        .Any((t) => t.FullName == typeFullName && t.Kind == AssemblyInspector._KindInterface);
      detail["contractRouteName"] = UjmwContractNaming.GetContractName(typeFullName, isInterface);
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
    /// Stores the current URL of a contract, so that it is restored when the contract is selected again
    /// (also after a restart of the application).
    /// </summary>
    private void SetContractSession(string typeFullName, string url) {

      if (string.IsNullOrWhiteSpace(typeFullName)) {
        return;
      }

      if (url == null) {
        url = string.Empty;
      }

      ContractSession session = this.Settings.ContractSessions.FirstOrDefault((s) => s.TypeFullName == typeFullName);
      if (session == null) {
        session = new ContractSession();
        session.TypeFullName = typeFullName;
        List<ContractSession> sessions = this.Settings.ContractSessions.ToList();
        sessions.Add(session);
        this.Settings.ContractSessions = sessions.ToArray();
      }

      session.Url = url.Trim();
      _Store.Save();
    }

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
    /// Placeholder within the authorization textbox which refers to the shared (persisted) token.
    /// </summary>
    private const string _SharedAuthPlaceholder = "{shared}";

    /// <summary>
    /// Persists the sidechannel text and invokes the UJMW operation on a worker thread.
    /// </summary>
    private void BeginExecute(long id, JObject args) {
      string url = args.Value<string>("url");
      string methodName = args.Value<string>("methodName");
      string authHeader = args.Value<string>("authHeader");
      string sideChannel = args.Value<string>("sideChannel");
      string requestText = args.Value<string>("requestText");
      string zone = args.Value<string>("zone");
      int timeoutSeconds = this.Settings.RequestTimeoutSeconds;

      if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(methodName)) {
        this.PostReply(id, false, null, "URL and method are required.");
        return;
      }
      if (requestText == null) {
        requestText = string.Empty;
      }

      // the '{shared}' placeholder is replaced by the persisted shared authorization header
      if (authHeader != null && authHeader.Contains(_SharedAuthPlaceholder)) {
        authHeader = authHeader.Replace(_SharedAuthPlaceholder, this.Settings.SharedAuthHeader);
      }

      this.Settings.SideChannelText = sideChannel;
      if (zone != null) {
        this.Settings.CurrentZone = zone;
      }
      _Store.Save();

      Task.Run(() => {
        InvocationResult invocationResult = UjmwInvoker.Invoke(url, methodName, authHeader, sideChannel, zone, requestText, timeoutSeconds);
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

    #region " SideChannel templates "

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

    #endregion

    #region " Client settings and managed infrastructure "

    private static readonly HashSet<string> _InfrastructureCommands = new HashSet<string>(StringComparer.Ordinal) {
      "getInfrastructure",
      "saveInfrastructureZones",
      "saveInfrastructureHosts",
      "saveOAuthConfigurations",
      "getWellknownEndpoints",
      "saveWellknownEndpoint",
      "deleteWellknownEndpoint",
      "promotePinnedEndpoint",
      "resolveUrls"
    };

    private const string _SubRoutePlaceholder = "?????";

    private readonly object _InfrastructureLock = new object();
    private IUjmwManagedInfrastructure _Infrastructure = null;
    private InfrastructureSettings _InfrastructureCreatedFor = null;

    /// <summary>
    /// Applies the general client settings.
    /// </summary>
    private void SetClientSettings(int requestTimeoutSeconds) {
      if (requestTimeoutSeconds < 1) {
        throw new InvalidOperationException("The timeout must be at least 1 second.");
      }
      this.Settings.RequestTimeoutSeconds = requestTimeoutSeconds;
      _Store.Save();
    }

    /// <summary>
    /// Switches the source of the managed infrastructure (FileBased, External or a registered provider).
    /// </summary>
    private void SaveInfrastructureSettings(InfrastructureSettings infrastructureSettings) {
      if (infrastructureSettings == null) {
        infrastructureSettings = new InfrastructureSettings();
      }
      infrastructureSettings.LegacyDataMigrated = this.Settings.Infrastructure.LegacyDataMigrated;
      this.Settings.Infrastructure = infrastructureSettings;
      _Store.Save();
      lock (_InfrastructureLock) {
        _Infrastructure = null;
      }
    }

    /// <summary>
    /// Executes an infrastructure command on a worker thread (an external store is called via HTTP).
    /// </summary>
    private void BeginInfrastructureCommand(long id, string command, JObject args) {
      InfrastructureSettings infrastructureSettings = this.Settings.Infrastructure;
      this.MigrateLegacyInfrastructureData();
      Task.Run(() => {
        JToken result = null;
        string error = null;
        try {
          IUjmwManagedInfrastructure infrastructure = this.GetInfrastructure(infrastructureSettings);
          result = this.ExecuteInfrastructureCommand(infrastructure, command, args);
        } catch (Exception ex) {
          //bridge boundary: every failure (also of external/custom stores) is reported to the UI
          error = UnwrapMessage(ex);
        }
        _Owner.BeginInvoke(new Action(() => this.PostReply(id, error == null, result, error)));
      });
    }

    /// <summary>
    /// Returns the cached store instance (or creates it).
    /// </summary>
    private IUjmwManagedInfrastructure GetInfrastructure(InfrastructureSettings infrastructureSettings) {
      lock (_InfrastructureLock) {
        if (_Infrastructure == null || !ReferenceEquals(_InfrastructureCreatedFor, infrastructureSettings)) {
          _Infrastructure = InfrastructureProvider.Create(infrastructureSettings);
          _InfrastructureCreatedFor = infrastructureSettings;
        }
        return _Infrastructure;
      }
    }

    /// <summary>
    /// Dispatches the infrastructure commands.
    /// </summary>
    private JToken ExecuteInfrastructureCommand(IUjmwManagedInfrastructure infrastructure, string command, JObject args) {
      switch (command) {
        case "getInfrastructure":
          return this.BuildInfrastructureState(infrastructure);
        case "saveInfrastructureZones":
          EnsureWritable(GetCapabilities(infrastructure).ZonesReadOnly, "zones");
          infrastructure.SaveZones(args["zones"].ToObject<UjmwInfrastructureZone[]>(_Serializer));
          return this.BuildInfrastructureState(infrastructure);
        case "saveInfrastructureHosts":
          EnsureWritable(GetCapabilities(infrastructure).HostsReadOnly, "hosts");
          infrastructure.SaveHosts(args["hosts"].ToObject<UjmwInfrastructureHost[]>(_Serializer));
          return this.BuildInfrastructureState(infrastructure);
        case "saveOAuthConfigurations":
          EnsureWritable(GetCapabilities(infrastructure).OAuthConfigurationsReadOnly, "OAuth configurations");
          infrastructure.SaveOAuthConfigurations(args["configurations"].ToObject<UjmwOAuthConfiguration[]>(_Serializer));
          return this.BuildInfrastructureState(infrastructure);
        case "getWellknownEndpoints":
          return this.BuildWellknownEndpoints(infrastructure, args.Value<string>("contractName"));
        case "saveWellknownEndpoint":
          EnsureWritable(GetCapabilities(infrastructure).WellknownEndpointsReadOnly, "wellknown endpoints");
          UjmwWellknownEndpoint endpoint = args["endpoint"].ToObject<UjmwWellknownEndpoint>(_Serializer);
          infrastructure.SaveWellknownEndpoint(endpoint);
          return this.BuildWellknownEndpoints(infrastructure, endpoint.ContractName);
        case "deleteWellknownEndpoint":
          EnsureWritable(GetCapabilities(infrastructure).WellknownEndpointsReadOnly, "wellknown endpoints");
          string contractName = args.Value<string>("contractName");
          infrastructure.DeleteWellknownEndpoint(contractName, args.Value<string>("hostUrl"), args.Value<string>("webApplicationName"));
          return this.BuildWellknownEndpoints(infrastructure, contractName);
        case "promotePinnedEndpoint":
          UjmwInfrastructureCapabilities promoteCapabilities = GetCapabilities(infrastructure);
          EnsureWritable(promoteCapabilities.WellknownEndpointsReadOnly, "wellknown endpoints");
          string promotedContractName = args.Value<string>("contractName");
          JObject newHost = args["newHost"] as JObject;
          if (newHost != null) {
            EnsureWritable(promoteCapabilities.HostsReadOnly, "hosts");
            this.CreateOrMergeHost(
              infrastructure,
              newHost.Value<string>("hostUrl"),
              newHost.Value<string>("zone"),
              newHost.Value<string>("webApplicationName"),
              newHost.Value<string>("applicationBaseUrl")
            );
          }
          infrastructure.SaveWellknownEndpoint(this.BuildWellknownEndpointFromUrl(infrastructure, promotedContractName, args.Value<string>("url")));
          return this.BuildWellknownEndpoints(infrastructure, promotedContractName);
        case "resolveUrls":
          return this.ResolveUrls(infrastructure, args.Value<string>("contractName"), args.Value<string>("zone"));
        default:
          throw new NotSupportedException($"Unknown command '{command}'.");
      }
    }

    /// <summary>
    /// Returns capabilities, zones, hosts and OAuth configurations of the store.
    /// </summary>
    private JObject BuildInfrastructureState(IUjmwManagedInfrastructure infrastructure) {
      JObject state = new JObject();
      state["capabilities"] = BuildCapabilities(GetCapabilities(infrastructure));
      state["zones"] = JToken.FromObject(EmptyIfNull(infrastructure.GetZones()), _Serializer);
      state["hosts"] = JToken.FromObject(EmptyIfNull(infrastructure.GetHosts()), _Serializer);
      state["oauthConfigurations"] = JToken.FromObject(EmptyIfNull(infrastructure.GetOAuthConfigurations()), _Serializer);
      return state;
    }

    /// <summary>
    /// Returns the wellknown endpoints of a contract including the composed url
    /// ('&lt;host&gt;/&lt;applicationBaseUrl&gt;/&lt;endpointUrl&gt;').
    /// </summary>
    private JArray BuildWellknownEndpoints(IUjmwManagedInfrastructure infrastructure, string contractName) {
      UjmwInfrastructureHost[] hosts = EmptyIfNull(infrastructure.GetHosts());
      UjmwWellknownEndpoint[] endpoints = EmptyIfNull(infrastructure.GetWellknownEndpoints(contractName));
      JArray result = new JArray();
      foreach (UjmwWellknownEndpoint endpoint in endpoints) {
        string applicationBaseUrl = string.Empty;
        UjmwInfrastructureHost host = hosts.FirstOrDefault((h) => string.Equals(TrimUrl(h.HostUrl), TrimUrl(endpoint.HostUrl), StringComparison.OrdinalIgnoreCase));
        if (host != null && host.WebApplications != null) {
          UjmwWebApplication application = host.WebApplications.FirstOrDefault((a) => string.Equals(a.Name, endpoint.WebApplicationName, StringComparison.OrdinalIgnoreCase));
          if (application != null) {
            applicationBaseUrl = application.ApplicationBaseUrl;
          }
        }
        JObject item = (JObject)JToken.FromObject(endpoint, _Serializer);
        item["url"] = ComposeUrl(endpoint.HostUrl, applicationBaseUrl, endpoint.EndpointUrl);
        result.Add(item);
      }
      return result;
    }

    /// <summary>
    /// Splits a (pinned) url into host, web application and endpoint url by matching the longest
    /// known '&lt;host&gt;/&lt;applicationBaseUrl&gt;' prefix of the store.
    /// </summary>
    private UjmwWellknownEndpoint BuildWellknownEndpointFromUrl(IUjmwManagedInfrastructure infrastructure, string contractName, string url) {
      if (string.IsNullOrWhiteSpace(contractName) || string.IsNullOrWhiteSpace(url)) {
        throw new InvalidOperationException("Contract and url are required.");
      }
      if (url.Contains(_SubRoutePlaceholder)) {
        throw new InvalidOperationException($"Please replace the placeholder '{_SubRoutePlaceholder}' (sub route) before promoting the url.");
      }
      string trimmedUrl = TrimUrl(url);
      UjmwWellknownEndpoint bestMatch = null;
      int bestPrefixLength = -1;
      foreach (UjmwInfrastructureHost host in EmptyIfNull(infrastructure.GetHosts())) {
        List<KeyValuePair<string, string>> prefixes = new List<KeyValuePair<string, string>>();
        prefixes.Add(new KeyValuePair<string, string>(string.Empty, ComposeUrl(host.HostUrl)));
        if (host.WebApplications != null) {
          foreach (UjmwWebApplication application in host.WebApplications) {
            prefixes.Add(new KeyValuePair<string, string>(application.Name, ComposeUrl(host.HostUrl, application.ApplicationBaseUrl)));
          }
        }
        foreach (KeyValuePair<string, string> prefix in prefixes) {
          if (prefix.Value.Length == 0 || prefix.Value.Length <= bestPrefixLength) {
            continue;
          }
          bool isPrefix = string.Equals(trimmedUrl, prefix.Value, StringComparison.OrdinalIgnoreCase) ||
                          trimmedUrl.StartsWith(prefix.Value + "/", StringComparison.OrdinalIgnoreCase);
          if (!isPrefix) {
            continue;
          }
          UjmwWellknownEndpoint endpoint = new UjmwWellknownEndpoint();
          endpoint.ContractName = contractName;
          endpoint.HostUrl = host.HostUrl;
          endpoint.WebApplicationName = prefix.Key;
          endpoint.EndpointUrl = trimmedUrl.Substring(prefix.Value.Length).Trim('/');
          bestMatch = endpoint;
          bestPrefixLength = prefix.Value.Length;
        }
      }
      if (bestMatch == null) {
        throw new InvalidOperationException($"The url '{url}' does not belong to a host known by the infrastructure store.");
      }
      return bestMatch;
    }

    /// <summary>
    /// Creates a host (within the given zone) in the store, or merges the zone and web application
    /// into an already existing host with the same url.
    /// </summary>
    private void CreateOrMergeHost(IUjmwManagedInfrastructure infrastructure, string hostUrl, string zone, string webApplicationName, string applicationBaseUrl) {

      string trimmedHostUrl = TrimUrl(hostUrl);
      if (string.IsNullOrWhiteSpace(trimmedHostUrl)) {
        throw new InvalidOperationException("The host url is required.");
      }

      string normalizedZone = string.Empty;
      if (zone != null) {
        normalizedZone = zone.Trim().Trim('/');
      }

      List<UjmwInfrastructureHost> hosts = EmptyIfNull(infrastructure.GetHosts()).ToList();
      UjmwInfrastructureHost host = hosts.FirstOrDefault((h) => string.Equals(TrimUrl(h.HostUrl), trimmedHostUrl, StringComparison.OrdinalIgnoreCase));
      if (host == null) {
        host = new UjmwInfrastructureHost();
        host.HostUrl = trimmedHostUrl;
        hosts.Add(host);
      }

      if (string.IsNullOrWhiteSpace(host.Zone)) {
        host.Zone = normalizedZone;
      }
      if (host.WebApplications == null) {
        host.WebApplications = new UjmwWebApplication[0];
      }

      if (!string.IsNullOrWhiteSpace(webApplicationName)) {
        bool applicationExists = host.WebApplications.Any((a) => string.Equals(a.Name, webApplicationName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!applicationExists) {
          UjmwWebApplication application = new UjmwWebApplication();
          application.Name = webApplicationName.Trim();
          application.ApplicationBaseUrl = string.Empty;
          if (applicationBaseUrl != null) {
            application.ApplicationBaseUrl = applicationBaseUrl.Trim().Trim('/');
          }
          host.WebApplications = host.WebApplications.Concat(new UjmwWebApplication[] { application }).ToArray();
        }
      }

      infrastructure.SaveHosts(hosts.ToArray());
    }

    /// <summary>
    /// Resolves the urls of a contract within a zone (only if the store supports it).
    /// </summary>
    private JArray ResolveUrls(IUjmwManagedInfrastructure infrastructure, string contractName, string zone) {
      if (!GetCapabilities(infrastructure).CanResolve) {
        throw new InvalidOperationException("The current infrastructure store does not support url resolution.");
      }
      string[] urls = EmptyIfNull(infrastructure.Resolve(contractName, zone));
      return new JArray(urls.Where((u) => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Moves the former (client-local) zones and OAuth settings once into the local infrastructure store.
    /// </summary>
    private void MigrateLegacyInfrastructureData() {
      InfrastructureSettings infrastructureSettings = this.Settings.Infrastructure;
      if (infrastructureSettings.LegacyDataMigrated) {
        return;
      }
      FileBasedUjmwManagedInfrastructure localStore = new FileBasedUjmwManagedInfrastructure(InfrastructureProvider.GetFileBasedStorePath(infrastructureSettings));
      if (localStore.GetZones().Length == 0 && this.Settings.Zones.Length > 0) {
        UjmwInfrastructureZone[] zones = this.Settings.Zones.Select((z) => {
          UjmwInfrastructureZone zone = new UjmwInfrastructureZone();
          zone.Name = z.Name;
          zone.Description = z.Description;
          return zone;
        }).ToArray();
        localStore.SaveZones(zones);
      }
      OAuthSettings oauth = this.Settings.OAuth;
      if (localStore.GetOAuthConfigurations().Length == 0 && !string.IsNullOrWhiteSpace(oauth.AuthorizeUrl)) {
        UjmwOAuthConfiguration configuration = new UjmwOAuthConfiguration();
        configuration.Name = "Default";
        configuration.AuthorizeUrl = oauth.AuthorizeUrl;
        configuration.TokenUrl = oauth.TokenUrl;
        configuration.ClientId = oauth.ClientId;
        configuration.ClientSecret = oauth.ClientSecret;
        configuration.RedirectUri = oauth.RedirectUri;
        configuration.Scope = oauth.Scope;
        configuration.UsePkce = oauth.UsePkce;
        localStore.SaveOAuthConfigurations(new UjmwOAuthConfiguration[] { configuration });
      }
      infrastructureSettings.LegacyDataMigrated = true;
      this.Settings.Zones = new InfrastructureZone[0];
      _Store.Save();
    }

    /// <summary>
    /// Returns the capabilities (never null).
    /// </summary>
    private static UjmwInfrastructureCapabilities GetCapabilities(IUjmwManagedInfrastructure infrastructure) {
      UjmwInfrastructureCapabilities capabilities = infrastructure.GetCapabilities();
      if (capabilities == null) {
        capabilities = new UjmwInfrastructureCapabilities();
        capabilities.ZonesReadOnly = true;
        capabilities.HostsReadOnly = true;
        capabilities.WellknownEndpointsReadOnly = true;
        capabilities.OAuthConfigurationsReadOnly = true;
      }
      return capabilities;
    }

    /// <summary>
    /// Maps the capabilities to explicit UI property names (the camel case resolver would
    /// turn 'OAuthConfigurationsReadOnly' into 'oAuthConfigurationsReadOnly').
    /// </summary>
    private static JObject BuildCapabilities(UjmwInfrastructureCapabilities capabilities) {

      JObject result = new JObject();
      result["zonesReadOnly"] = capabilities.ZonesReadOnly;
      result["hostsReadOnly"] = capabilities.HostsReadOnly;
      result["wellknownEndpointsReadOnly"] = capabilities.WellknownEndpointsReadOnly;
      result["oauthConfigurationsReadOnly"] = capabilities.OAuthConfigurationsReadOnly;
      result["canResolve"] = capabilities.CanResolve;

      return result;
    }

    /// <summary>
    /// Throws if the given area of the store is read-only.
    /// </summary>
    private static void EnsureWritable(bool isReadOnly, string areaName) {

      if (isReadOnly) {
        throw new InvalidOperationException($"The {areaName} of the current infrastructure store are read-only.");
      }

    }

    /// <summary>
    /// Joins url segments with exactly one '/' (empty segments are skipped).
    /// </summary>
    private static string ComposeUrl(params string[] segments) {
      List<string> parts = new List<string>();
      for (int i = 0; i < segments.Length; i++) {
        string segment = segments[i];
        if (string.IsNullOrWhiteSpace(segment)) {
          continue;
        }
        segment = segment.Trim();
        if (parts.Count == 0) {
          parts.Add(segment.TrimEnd('/'));
        } else {
          parts.Add(segment.Trim('/'));
        }
      }
      return string.Join("/", parts.Where((p) => p.Length > 0));
    }

    /// <summary>
    /// Trims whitespace and trailing slashes.
    /// </summary>
    private static string TrimUrl(string url) {
      if (url == null) {
        return string.Empty;
      }
      return url.Trim().TrimEnd('/');
    }

    /// <summary>
    /// Returns an empty array instead of null.
    /// </summary>
    private static T[] EmptyIfNull<T>(T[] items) {
      if (items == null) {
        return new T[0];
      }
      return items;
    }

    /// <summary>
    /// Returns the message of the innermost relevant exception (reflection/proxy wrappers are skipped).
    /// </summary>
    private static string UnwrapMessage(Exception ex) {
      Exception current = ex;
      while ((current is System.Reflection.TargetInvocationException || current is AggregateException) && current.InnerException != null) {
        current = current.InnerException;
      }
      return current.Message;
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

      //configurations without redirect uri (e.g. from the infrastructure store) get the default on demand
      if (string.IsNullOrWhiteSpace(oauthSettings.RedirectUri)) {
        oauthSettings.RedirectUri = _DefaultOAuthRedirectUri;
      }

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
