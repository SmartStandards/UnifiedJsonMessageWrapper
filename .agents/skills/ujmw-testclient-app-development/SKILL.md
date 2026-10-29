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
| Infrastructure source selectable in the settings: **Local** (`LocalUjmwManagedInfrastructure`, `%APPDATA%\UJMW.TestClient\infrastructure.json`), **External** (UJMW `DynamicClientFactory` proxy with URL + token) or a **registered store provider** (public, non-abstract class with public parameterless constructor implementing the interface, found in a regularly added DLL, registered by the user; the registration (`RegisteredProvider`: DLL path + type name) survives the removal of the DLL from the list; loaded into an own `AssemblyLoadContext`, `UJMW.DynamicClient` and all client assemblies are shared) | out-of-the-box usable locally, centrally manageable in organizations, custom stores without neighbour-DLL scanning |
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
- **Open with**: a DLL path passed as first command line argument is added (if not yet present) and selected directly after startup.
- Types implementing `IUjmwManagedInfrastructure` (usable as store provider) are shown **bold and green** in the type selection; clicking them opens a dialog to register them as store provider (instead of checking them).

### 3.3 Type selection (detail view for a DLL)

- Clicking a DLL shows a second tree in the detail view: the namespaces of the DLL (hierarchical by `.`) containing the public types.
- Interfaces: **blue**, classes: **black**, static classes: **grey and not selectable**.
- A single click on a type toggles its icon between unchecked and checked (checkbox-like).
- Checked types appear as child nodes of the DLL in the main tree (persisted with the DLL entry, including the kind so that they can be shown when the DLL is missing).

### 3.4 Type detail view (main tree: type selected)

- **URL field** with a persistent history **per FullQualifiedTypeName** (DLL- and version-independent) as dropdown, ordered by last-used descending. Free text input is possible.
- **Zone** field: editable combobox (same style as the URL combobox, compact indented tree of the zones; selecting inserts the **full area path** joined by `/`, e.g. `Production/EU`) **or** free text input;
- **URL lookup** (`…` button right of the URL field, left of the browser button) opens a dialog containing:
  - **Resolve** button (only if `CanResolve`), using the zone of the main window; resolved URLs are added to the list.
  - The list always contains the **wellknown endpoints** of the store (`https://<host>/<applicationBaseUrl>/<endpointUrl>`) and the **user-specific pins** (`PinnedEndpoint`, stored in `settings.json`, NOT in the store).
  - A server/web application combobox: selecting an entry directly adds a pin `https://<host>/<applicationBaseUrl>/?????/<UJMW contract name>` (`UjmwContractNaming`), where `?????` intentionally marks a possible sub route to be edited.
  - Removing the pin symbol deletes a pin; if the store is not read-only, wellknown entries can be deleted and pins can be **promoted** into the store (only after the `?????` placeholder was replaced).
  - Clicking a URL applies it to the URL field.
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
- Presets have a `Kind`: `Method` (star icon, single call) or `FuseData` (table icon `▦`, FUSE data mode).

### 3.6 FUSE data mode

- Activated when the selected type closes the FUSE-fx contract `System.Data.Fuse.IRepository<TEntity, TKey>` (directly or via base types/interfaces) with concrete, resolvable generic arguments. Detection is done **by name** in `Inspection/FuseRepositoryDetector.cs` - the TestClient must **never** reference FUSE packages or DLLs (snapshot knowledge only).
- A toggle **directly below the SideChannel** switches between data mode (default ON) and the raw JSON mode (opt-out).
- Data table: column filters (FUSE `FieldOperators`, translated into an `ExpressionTree`), optional expert `ExpressionTree` JSON (combined as `subTree`), or a string search expression (`*BySearchExpression`, only if `SupportsStringBasedSearchExpressions`); multi-column sorting (click / Shift+click, `^` prefix = descending); pagination via `limit`/`skip` with `CountAll`/`Count`/`CountBySearchExpression`.
- Hidden columns switch reads to `GetEntityFields*` (`includedFieldNames`, key fields are always loaded).
- Projected functions: `GetCapabilities`, `GetOriginIdentity`, `GetEntities*`, `GetEntityFields*`, `Count*`, `ContainsKey`, `TryAddEntity`, `TryUpdateEntity`, `TryUpdateEntityFields`, `AddOrUpdateEntity`, `AddOrUpdateEntityFields`, `TryDeleteEntities`, `MassupdateByKeys`, `Massupdate`, `MassupdateBySearchExpression`, `TryUpdateKey`. `EntityRef` methods are intentionally **not** projected. Buttons are disabled according to the repository capabilities.
- Key fields are resolved by `[PrimaryIdentity]` + `[(Unique)PropertyGroup]`, then `[Key]`, then `Id`/`<Entity>Id`, then the first property of the key type. Composite keys are sent as `{ "field1": ..., "field2": ... }` (FUSE `CompositeKeyN`).
- All calls go through the regular `execute` bridge command (generated request wrappers incl. `{AUTO}` sidechannel).
- The star in the data mode saves a `FuseData` favorite: URL, authorization, sidechannel, filter, sorting and page size (**not** the current page). Default name: full URL path translated into folders + `/(ENTITY-TABLE)` (no method name).

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
| `Inspection/FuseRepositoryDetector.cs`, `Inspection/FuseEntityDescriptor.cs` | name-based detection of closed FUSE `IRepository<TEntity, TKey>` contracts and entity/field/key metadata for the data mode |
| `Invocation/UjmwInvoker.cs` | `{AUTO}` replacement, HTTP call, result classification (`Success` / `Fault` / `Error`) |
| `Auth/JwtTokenGenerator.cs` | JWT creation via jose-jwt (HS*, RS*, PS*, ES*, none) |
| `Auth/OAuthCodeFlowDialog.cs` | WebView2-based authorization code flow incl. PKCE and token exchange |
| `wwwroot/*` | complete UI (vanilla JS, no build step) |

### 4.1 Bridge commands

`setLayout`, `setCurrentZone`, `setClientSettings`, `saveInfrastructureSettings`, `takeStartupDll`, `registerProvider`, `unregisterProvider`, `getPinnedEndpoints`, `savePinnedEndpoint`, `deletePinnedEndpoint`, `generateJwt`, `startOAuth`; asynchronous infrastructure commands: `getInfrastructure`, `saveInfrastructureZones`, `saveInfrastructureHosts`, `saveOAuthConfigurations`, `getWellknownEndpoints`, `saveWellknownEndpoint`, `deleteWellknownEndpoint`, `promotePinnedEndpoint`, `resolveUrls`.

For manual tests of the store provider feature, `dotnet/test/UJMW.Tests/MockReadOnlyUjmwManagedInfrastructure.cs` (read-only, `CanResolve = true`) can be registered from the test assembly.

When adding a UI feature: add a command in `HostBridge.ExecuteCommand`, keep the JS side free of business logic beyond rendering and input handling.

## 5. Rules for further development

- Follow the C# coding style of the user instructions (explicit types, no `var`, `_`-prefixed PascalCase fields, fully implemented properties, `this.` for members, K&R braces, no ternaries, no `async`/`await`, XML `<summary>` on methods). The JS code avoids ternaries and `async`/`await` as well (Promises with `.then`).
- Prefer `Newtonsoft.Json`.
- Never execute code of inspected DLLs; keep using MetadataLoadContext.
- Exceptions are only caught at the bridge boundary (reported to the UI) and where they are explicitly expected (missing references during inspection, transport failures, invalid JSON).
- The UJMW wire behavior must follow the `ujmw` skill (POST, operation name in URL, `out` parameters omitted, `fault` evaluation, non-2xx = transport failure).

## 6. Open points

- Optional: HTTP-header sidechannel transport (currently only the `_` wrapper property).
