using Newtonsoft.Json;
using System;
using System.IO;
using UJMW.TestClient.Model;

namespace UJMW.TestClient.Persistence {

  /// <summary>
  /// Loads and saves the per-user settings file ('%APPDATA%\UJMW.TestClient\settings.json').
  /// </summary>
  internal sealed class SettingsStore {

    private static readonly string _SettingsDirectory = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UJMW.TestClient"
    );

    private static readonly string _LocalDataDirectory = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UJMW.TestClient"
    );

    private readonly string _FilePath;
    private AppSettings _Current;

    public SettingsStore() {
      _FilePath = Path.Combine(_SettingsDirectory, "settings.json");
      _Current = this.Load();
    }

    /// <summary>
    /// Directory for machine-local (non roaming) data such as the WebView2 user data folder.
    /// </summary>
    public static string LocalDataDirectory {
      get {
        return _LocalDataDirectory;
      }
    }

    public AppSettings Current {
      get {
        return _Current;
      }
    }

    /// <summary>
    /// Writes the current settings to disk.
    /// </summary>
    public void Save() {
      Directory.CreateDirectory(_SettingsDirectory);
      string json = JsonConvert.SerializeObject(_Current, Formatting.Indented);
      string tempFilePath = _FilePath + ".tmp";
      File.WriteAllText(tempFilePath, json);
      File.Move(tempFilePath, _FilePath, true);
    }

    /// <summary>
    /// Reads the settings file. A corrupt file is renamed to '*.corrupt' and replaced by default settings.
    /// </summary>
    private AppSettings Load() {
      if (!File.Exists(_FilePath)) {
        return new AppSettings();
      }
      string json = File.ReadAllText(_FilePath);
      AppSettings settings;
      try {
        settings = JsonConvert.DeserializeObject<AppSettings>(json);
      } catch (JsonException) {
        File.Copy(_FilePath, _FilePath + ".corrupt", true);
        settings = null;
      }
      if (settings == null) {
        settings = new AppSettings();
      }
      Normalize(settings);
      return settings;
    }

    /// <summary>
    /// Replaces null values (which can occur in manually edited files) by empty defaults.
    /// </summary>
    private static void Normalize(AppSettings settings) {
      if (settings.Dlls == null) {
        settings.Dlls = new DllEntry[0];
      }
      foreach (DllEntry dll in settings.Dlls) {
        if (dll.CheckedTypes == null) {
          dll.CheckedTypes = new CheckedType[0];
        }
      }
      if (settings.UrlHistory == null) {
        settings.UrlHistory = new UrlHistoryEntry[0];
      }
      if (settings.Presets == null) {
        settings.Presets = new Preset[0];
      }
      if (settings.SideChannelTemplates == null) {
        settings.SideChannelTemplates = new SideChannelTemplate[0];
      }
      if (settings.Zones == null) {
        settings.Zones = new InfrastructureZone[0];
      }
      if (settings.Jwt == null) {
        settings.Jwt = new JwtGeneratorSettings();
      }
      if (settings.OAuth == null) {
        settings.OAuth = new OAuthSettings();
      }
      if (settings.SideChannelText == null) {
        settings.SideChannelText = string.Empty;
      }
      if (settings.Infrastructure == null) {
        settings.Infrastructure = new InfrastructureSettings();
      }
      if (settings.Infrastructure.RegisteredProviders == null) {
        settings.Infrastructure.RegisteredProviders = new RegisteredProvider[0];
      }
      if (settings.Infrastructure.ProviderTypeName == null) {
        settings.Infrastructure.ProviderTypeName = string.Empty;
      }
      if (settings.Infrastructure.FileBasedStorePath == null) {
        settings.Infrastructure.FileBasedStorePath = string.Empty;
      }
      if (settings.Infrastructure.Mode != InfrastructureSettings._ModeExternal && settings.Infrastructure.Mode != InfrastructureSettings._ModeProvider) {
        //unknown or outdated modes (e.g. the former 'Local' or 'Reflection') fall back to the file based store
        settings.Infrastructure.Mode = InfrastructureSettings._ModeFileBased;
      }
      if (settings.PinnedEndpoints == null) {
        settings.PinnedEndpoints = new PinnedEndpoint[0];
      }
      if (settings.CurrentZone == null) {
        settings.CurrentZone = string.Empty;
      }
      if (settings.RequestTimeoutSeconds < 1) {
        settings.RequestTimeoutSeconds = 5;
      }
    }

  }

}
