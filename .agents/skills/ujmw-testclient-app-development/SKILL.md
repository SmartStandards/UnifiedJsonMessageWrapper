---
name: ujmw-testclient-app-development
description: Requirements, architecture and implementation rules for the UJMW Test Client (Windows desktop EXE with a WebView2-hosted UI) used to manually invoke UJMW service endpoints based on contract types loaded from arbitrary .NET DLLs. Use when extending, fixing or reviewing the test client under dotnet/tools/UJMW.TestClient.
---

# UJMW Test Client – App Development Skill

## 1. Purpose

The UJMW Test Client is a developer tool (Windows EXE) that allows to invoke UJMW endpoints manually:

- contract types (interfaces / classes) are discovered by **metadata inspection** of arbitrary .NET DLLs,
- requests are generated as editable JSON (UJMW wire format, see skill `ujmw`),
- authentication headers and sidechannel data can be provided or issued,
- complete request setups can be persisted as **presets**.

Location: `dotnet/tools/UJMW.TestClient` (solution folder `tools` inside `dotnet/UJMW.sln`).

## 2. Architecture decisions

| Decision | Rationale |
|---|---|
| WinForms shell (`MainForm`) hosting a single **WebView2** control which renders the whole UI (`wwwroot/index.html`, `app.js`, `app.css`) | Requirement "WinForms client switched to WebView": modern, flexible UI while staying a plain Windows EXE |
| UI files are served via `SetVirtualHostNameToFolderMapping` (`https://ujmw-testclient.local/`) | no local web server needed, no file:// restrictions |
| JS <-> host communication via `chrome.webview.postMessage` (request: `{id, cmd, args}`, reply: `{id, ok, result, error}`), dispatched by `HostBridge` | single, explicit command surface; all host logic is in C# |
| Explorer drag & drop is received in JS and forwarded with `postMessageWithAdditionalObjects` (`CoreWebView2File.Path`) | browsers do not expose file paths to JS |
| Native dialogs (OpenFileDialog, OAuth WebView2 dialog) are opened via `BeginInvoke` outside of the WebView2 event handler | avoids nested message loops inside WebView2 event handlers |
| Type inspection via `System.Reflection.MetadataLoadContext` (resolver = target DLL folder + all shared frameworks of the running runtime, core assembly `System.Private.CoreLib`) | DLLs of any target framework (net48, netstandard, net8+) can be inspected without loading/executing them and without locking them permanently |
| UJMW invocation is plain HTTP (`HttpClient.Send`, synchronous on a worker thread) | the test client must not depend on the contract assembly being executable; it reproduces the UJMW wire protocol ("Level C" of the `ujmw` skill) |
| Persistence: one JSON file per user `%APPDATA%\UJMW.TestClient\settings.json` (Newtonsoft.Json); WebView2 user data in `%LOCALAPPDATA%\UJMW.TestClient\WebView2` | persistent per user, roaming capable |
| JWT generation via `jose-jwt` | requirement |
| Infrastructure data (zones as area tree, hosts with web applications, OAuth configurations, wellknown endpoints) is provided by `System.Web.UJMW.IUjmwManagedInfrastructure` (shared in `UJMW.DynamicClient`) | reusable contract; only infrastructure-relevant data, never user-specific presets/templates/JWT inputs |
| Infrastructure source selectable in the settings: **Local** (`LocalUjmwManagedInfrastructure`, `%APPDATA%\UJMW.TestClient\infrastructure.json`), **External** (UJMW `DynamicClientFactory` proxy with URL + token) or **Reflection** (public parameterless implementation in a DLL next to the client; only DLLs referencing `UJMW.DynamicClient` are loaded) | out-of-the-box usable locally, centrally manageable in organizations |
| Editing is only offered if `GetCapabilities().IsReadOnly == false`; Resolve only if `CanResolve == true` | capability-driven UI |
| Target framework `net10.0-windows` | aligned with newest TFM of the solution |

## 3. Functional requirements

### 3.1 Layout

- Left: main tree view (DLLs as root nodes). Right: detail view. Both separated by a draggable **splitter** (width persisted).

### 3.2 DLL list (main tree root level)

- DLLs are added via file-open dialog (multi-select, `*.dll;*.exe`) or via **drag & drop from Windows Explorer**.
- The list is persisted per user.
- Display text: assembly name + version, e.g. `MyContracts (1.2.3.0)`.
- DLLs whose file does not exist anymore are shown **greyed out** (they stay in the list).
- Selected DLL can be removed with the **Del** key.

### 3.3 Type selection (detail view for a DLL)

- Clicking a DLL shows a second tree in the detail view: the namespaces of the DLL (hierarchical by `.`) containing the public types.
- Interfaces: **blue**, classes: **black**, static classes: **grey and not selectable**.
- A single click on a type toggles its icon between unchecked and checked (checkbox-like).
- Checked types appear as child nodes of the DLL in the main tree (persisted with the DLL entry, including the kind so that they can be shown when the DLL is missing).

### 3.4 Type detail view (main tree: type selected)

- **URL field** with a persistent history **per FullQualifiedTypeName** (DLL- and version-independent) as dropdown, ordered by last-used descending. Free text input is possible.
- **Zone** field: dropdown (zones of the managed infrastructure, flattened area tree) **or** free text input; persisted per user (`CurrentZone`). The zone replaces the placeholder `{zone}` inside the SideChannel content before sending (JSON-string-escaped, i.e. use it inside a JSON string: `"zone": "{zone}"`).
- **Resolve** button next to the zone: only enabled if `IUjmwManagedInfrastructure.GetCapabilities().CanResolve`. Calls `Resolve(contractName = FullQualifiedTypeName, zone)`; one URL is applied directly, multiple URLs open a child popup for selection.
- **Wellknown** combobox: wellknown endpoints of the contract from the infrastructure store (mapping contract -> host + web application + endpoint URL, resulting URL `https://<host>/<applicationBaseUrl>/<endpointUrl>`; the endpoint URL is maintained exclusively here). Selecting applies the URL; **New…** / **Delete** only if the store is not read-only.
- **Open browser** button next to the URL.
- The following areas are **always visible but greyed out/disabled** as long as no URL is present:
	- a) **Authorization** header value: manual input or issued via a special dialog containing
		- a **JWT generator** based on `jose-jwt` (algorithm, sign key incl. base64 flag, payload JSON, lifetime, automatic `iat`/`nbf`/`exp`); all inputs including the sign key are persisted,
		- an **OAuth authorization code flow requestor** based on WebView2 (authorize URL, token URL, client id, optional client secret, redirect URI, scope, PKCE); settings persisted.
		The issued token is applied as `Bearer <token>`.
	- b) collapsible area **SideChannel** with a text box holding a JSON object; templates can be saved / loaded / deleted via a combobox.
- **Test client** area:
	- combobox with all methods of the type (interfaces include inherited interface methods),
	- selecting a method prepares a JSON request: all non-`out` parameters (`ref` included), meaningfully filled, optional parameters with their compiled default value, complex types expanded (camelCase property names, depth-limited),
	- the sidechannel property is always pre-filled as `"_": {AUTO}`; the token `{AUTO}` is replaced **immediately before sending** with the content of the SideChannel text box (`{}` if empty),
	- **EXECUTE** button (`POST <url>/<methodName>`, `Content-Type: application/json`, optional `Authorization` header); the request timeout is configurable in the settings (**default 5 seconds**, per-call `CancellationTokenSource`),
	- request and response text boxes use the **full width** of the area (minus the documentation column), have a fixed (doubled) height, are not resizable and show scrollbars,
	- **XML documentation** (`<assembly>.xml` next to the DLL, read by `XmlDocReader`): the type summary is shown below the header, the method summary below the signature, parameter / return value descriptions in a **documentation column at the right edge** of the test client area,
	- read-only result box:
		- `fault` property not null and not `""` -> background **light red**,
		- hard HTTP error codes / transport exceptions / invalid UJMW responses -> shown in the box with **darker red** background.
- **Star** button: saves the current window (URL, authorization header, sidechannel, method, request JSON) as **preset**.
	- Name input pre-filled with `<hostname-from-url>/<MethodName>`; for a loaded preset with its original name.
	- Saving onto an existing name shows an **overwrite warning**.

### 3.5 Presets in the main tree

- Presets are shown below the corresponding type node (keyed by FullQualifiedTypeName, i.e. visible under every DLL containing the checked type).
- Every `/` in a preset name creates a **virtual folder** level.
- Clicking a folder does nothing (only the expander toggles); clicking a preset loads it into the detail view.
- Context menu on folders and presets: **Rename** (folder: applied to all contained presets; preset: rename = move, full path editable) and **Delete**.

## 4. Implementation map

| Artifact | Responsibility |
|---|---|
| `Program.cs` | entry point |
| `MainForm.cs` | WinForms shell, WebView2 initialization, virtual host mapping, navigation guard |
| `Bridge/HostBridge.cs` | command dispatcher for all UI requests, state snapshot (`getState`), persistence mutations |
| `Model/*.cs` | persisted settings model (`AppSettings`, `DllEntry`, `CheckedType`, `UrlHistoryEntry`, `Preset`, `SideChannelTemplate`, `InfrastructureZone`, `JwtGeneratorSettings`, `OAuthSettings`) |
| `Persistence/SettingsStore.cs` | load/save of `settings.json` (corrupt files are backed up and replaced by defaults) |
| `Inspection/AssemblyInspector.cs` | display name, namespace/type listing, method listing via MetadataLoadContext |
| `Inspection/RequestTemplateBuilder.cs` | sample request JSON generation incl. `{AUTO}` token |
| `Invocation/UjmwInvoker.cs` | `{AUTO}` replacement, HTTP call, result classification (`Success` / `Fault` / `Error`) |
| `Auth/JwtTokenGenerator.cs` | JWT creation via jose-jwt (HS*, RS*, PS*, ES*, none) |
| `Auth/OAuthCodeFlowDialog.cs` | WebView2-based authorization code flow incl. PKCE and token exchange |
| `wwwroot/*` | complete UI (vanilla JS, no build step) |

### 4.1 Bridge commands

`getState`, `addDllsViaDialog`, `dropFiles`, `removeDll`, `getDllTypes`, `toggleType`, `getTypeDetail`, `touchUrl`, `openBrowser`, `execute`, `savePreset`, `renamePreset`, `renameFolder`, `deletePreset`, `deleteFolder`, `saveSideChannelTemplate`, `deleteSideChannelTemplate`, `setSideChannelText`, `setLayout`, `saveZones`, `generateJwt`, `startOAuth`, `resolveUrls` (placeholder).

When adding a UI feature: add a command in `HostBridge.ExecuteCommand`, keep the JS side free of business logic beyond rendering and input handling.

## 5. Rules for further development

- Follow the C# coding style of the user instructions (explicit types, no `var`, `_`-prefixed PascalCase fields, fully implemented properties, `this.` for members, K&R braces, no ternaries, no `async`/`await`, XML `<summary>` on methods). The JS code avoids ternaries and `async`/`await` as well (Promises with `.then`).
- Prefer `Newtonsoft.Json`.
- Never execute code of inspected DLLs; keep using MetadataLoadContext.
- Exceptions are only caught at the bridge boundary (reported to the UI) and where they are explicitly expected (missing references during inspection, transport failures, invalid JSON).
- The UJMW wire behavior must follow the `ujmw` skill (POST, operation name in URL, `out` parameters omitted, `fault` evaluation, non-2xx = transport failure).

## 6. Open points

- Specification of the Resolve dialog / ApiCatalog query (zone info transfer, response format).
- Optional: HTTP-header sidechannel transport (currently only the `_` wrapper property).
