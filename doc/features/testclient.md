# UJMW Test Client

← [Documentation Guide](../README.md) · Developer skill (architecture rules): [ujmw-testclient-app-development](../../.agents/skills/ujmw-testclient-app-development/SKILL.md)

## 1. Abstract and motivation

Swagger UI shows *endpoints*; developers think in *contracts*. The **UJMW Test Client** is a Windows developer tool that starts from the contract DLL: it lists the contract types of any .NET assembly, generates ready-to-edit UJMW request JSON for every method, and invokes the endpoint over plain HTTP – including authentication, sidechannels and long-running operations.

### 1.1 Positive differentiation

| Alternative | What the Test Client does differently |
|---|---|
| Swagger UI | contract-centric (types, methods, XML docs from the DLL); works also for WCF hosts and hosts without Swagger |
| Postman / curl | requests are generated from the method signatures; sidechannel, JWT/OAuth and long-running polling built in |
| Writing a small console client | no code, no reference to the contract assembly at runtime, no dependency conflicts |

Key property: DLLs are **inspected by metadata only** (`System.Reflection.MetadataLoadContext`). Contract assemblies of any target framework (net48, netstandard, net8+) can be used without being loaded or executed, and without locking them permanently.

---

## 2. Getting started

1. Start `UJMW.TestClient.exe` (project `dotnet/tools/UJMW.TestClient`, `net10.0-windows`, requires the WebView2 runtime).
2. Add a contract DLL via the file dialog or **drag & drop from Explorer** (or pass the path as first command-line argument / "Open with").
3. Click the DLL, check the contract interface in the namespace tree – it appears below the DLL in the main tree.
4. Select the type, enter the **URL** of the endpoint (controller base URL, e.g. `https://host/api/calculator`).
5. Pick a **method** – the request JSON is pre-generated.
6. Press **EXECUTE**. The result box shows the response (light red if `fault` is set, dark red for transport/HTTP errors or invalid UJMW responses).
7. Press the **star** to save everything as a preset.

---

## 3. Feature overview

### 3.1 Contract discovery

| Feature | Behavior |
|---|---|
| DLL list | persisted per user; missing files are shown greyed out; **Del** removes |
| Type tree | namespaces hierarchical; interfaces blue, classes black, static classes grey (not selectable) |
| XML documentation | `<assembly>.xml` next to the DLL is shown for types, methods, parameters and return values |
| Method list | includes inherited interface methods |

### 3.2 Request generation

- All non-`out` parameters (including `ref`) are generated with meaningful sample values; optional parameters use their compiled default; complex types are expanded in camelCase (depth-limited).
- `CancellationToken` and callback (`Action<...>`) parameters are not part of the request (see [Long-running operations](long-running-operations.md)).
- The sidechannel property is always pre-filled as `"_": {AUTO}`.

### 3.3 URL and infrastructure

| Feature | Behavior |
|---|---|
| URL history | per full type name, ordered by last use |
| Zone | free text or selected from the infrastructure zone tree (e.g. `Production/EU`) |
| URL lookup (`…`) | well-known endpoints of the infrastructure store, user pins, optional **Resolve** (if supported) |
| Infrastructure source | **Local** (`%APPDATA%\UJMW.TestClient\infrastructure.json`), **External** (UJMW endpoint implementing `IUjmwManagedInfrastructure`), or a **registered store provider** from a DLL |

### 3.4 Authentication

- `Authorization` header as free text, or issued via a dialog:
	- **JWT generator** (`jose-jwt`; algorithm, sign key, payload, lifetime with automatic `iat`/`nbf`/`exp`),
	- **OAuth authorization code flow** (WebView2; PKCE supported).
- Issued tokens are applied as `Bearer <token>`.
- The placeholder `{shared}` in the header is replaced by a shared, persisted authorization header.

### 3.5 Sidechannel

- Collapsible **SideChannel** box containing a JSON object; templates can be saved / loaded / deleted.
- `{AUTO}` in the request is replaced **immediately before sending** with the box content (`{}` if empty).
- `{zone}` inside the sidechannel is replaced with the current zone.
- See [Sidechannels](sidechannels.md) for the semantics on the host.

### 3.6 Long-running operations

For methods with a `CancellationToken` parameter the client uses the long-running protocol on the wire:

| Aspect | Behavior |
|---|---|
| Initiation | `POST <url>/<method>?wait=2` |
| Polling | `GET` with `asyncId` / `pollingSalt` / `pollingHash`, respecting `pollNbf` |
| Transcript | every round-trip is listed with its `async` state (`execState`, `pollNbf`, `cancellable`) |
| Callbacks | callback invocations are shown in `#` order |
| Cancel | a **Cancel** button sends the `DELETE` request while the operation runs |
| NBF override | `pollNbf` can be ignored (setting `AlwaysOverrideNbfPolling` or per execution) to poll at a fixed fast cadence |
| Compatibility | servers without the feature ignore `wait` and answer synchronously |

Queue-mode endpoints ([Async queue mode](async-queue-mode.md)) look exactly like regular endpoints and are invoked the same way.

### 3.7 Presets

- The star saves URL, authorization, sidechannel, method and request JSON.
- Presets appear below the type node (per full type name, independent of the DLL version).
- `/` in a preset name creates virtual folders; context menu offers **Rename** (move) and **Delete**.

### 3.8 FUSE data mode

If the selected type implements the FUSE-fx contract `System.Data.Fuse.IRepository<TEntity, TKey>` (detected by name, no FUSE reference), a data table mode is offered: column filters, sorting, paging, field selection and CRUD buttons according to the repository capabilities. Data-mode favorites store URL, auth, sidechannel, filter, sorting and page size.

---

## 4. Settings and persistence

| Item | Location / default |
|---|---|
| User settings, DLL list, presets, templates, JWT/OAuth inputs | `%APPDATA%\UJMW.TestClient\settings.json` |
| Local infrastructure store | `%APPDATA%\UJMW.TestClient\infrastructure.json` |
| WebView2 user data | `%LOCALAPPDATA%\UJMW.TestClient\WebView2` |
| Request timeout | **5 seconds** (configurable) |

---

## 5. Architecture (short)

| Part | Responsibility |
|---|---|
| `MainForm` | WinForms shell hosting one WebView2 control |
| `wwwroot/index.html`, `app.js`, `app.css` | complete UI, served via virtual host `https://ujmw-testclient.local/` |
| `Bridge/HostBridge` | single command surface between JS and C# (`postMessage`, `{id, cmd, args}` → `{id, ok, result, error}`) |
| `Inspection/*` | metadata-only DLL inspection, request generation, XML docs, FUSE detection |
| `Invocation/UjmwInvoker*` | plain HTTP UJMW wire calls (regular and long-running) |

Extension rules and the full requirement list are maintained in the [developer skill](../../.agents/skills/ujmw-testclient-app-development/SKILL.md).
