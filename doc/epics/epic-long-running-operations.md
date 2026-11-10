# Epic: Long-Running Operations (Async Execution, Polling, Cancellation and Callbacks)

**Status:** Concept / not yet an active requirement  
**Scope:** ASP.NET Core hosting, the .NET dynamic client, the TypeScript `CreateServiceProxy` (UShell), the UJMW.TestClient, MCP exposure and Swagger. **WCF is explicitly out of scope.**

---

## 1. Motivation

Long-running service operations are dangerous for UJMW hosts today: a request that runs for minutes blocks the web application and cannot be aborted by the caller. Moving such work to a background task without a cancellation mechanism makes the situation even worse.

This epic introduces two closely related features:

1. **Async execution with polling and cancellation** – initiated through a contract convention (`CancellationToken` parameter) and controlled through HTTP verbs.
2. **Callbacks (`Action<...>` parameters)** – a comfortable alternative to observing `out` parameters while a call is still running.

### 1.1 Purpose and explicit non-goal

The purpose of this epic is to **decouple the transport layer from HTTP timeouts** (and from long-blocking HTTP requests). It is **not** about general asynchronous usage:

- From the perspective of the caller, operations remain **synchronous** – the call blocks until the operation has terminated.
- Polling is performed **internally** by the client proxies.
- The `CancellationToken` is the **only abort channel** from the client side.
- Callback invocations are fetched in **bulk** with every internal poll and replayed on the client side.

General asynchronous usage (fire-and-forget, observing operations from elsewhere, `async`/`await` support in contracts) is **out of scope** and will be addressed in a later step together with `UShell.ServerCommands` and possibly `async`/`await` pattern support.

---

## 2. Feature 1: Long-runner detection by convention

### 2.1 The convention

A contract method that declares a `System.Threading.CancellationToken` parameter is treated as a **potentially long-running operation**.

```csharp
int ProcessAllOpenTickets(CancellationToken c, int ticketOwnerId, out int currentTicketId);
```

Rationale:

- Anything that may run for a long time must accept a `CancellationToken` anyway – otherwise it would be unsafe to execute it in a hosted environment.
- `CancellationToken` is a BCL standard type. Requiring it as a convention does not force any additional assembly references onto contract authors.
- The contract is not polluted with knowledge about UJMW, about the hosting technology or about the consumer. The token alone carries the clear semantic: *"this function may run longer"*.
- Existing contracts cannot collide, because `CancellationToken` parameters were not supported by UJMW before.

### 2.2 Transport rules for the token

- The `CancellationToken` parameter may be declared at **any position** within the method signature. At most one `CancellationToken` parameter per method is allowed.
- The `CancellationToken` parameter is **never** part of the request DTO.
- In **synchronous** mode (no `wait` query parameter) the server binds the token to `HttpContext.RequestAborted`, so aborting the HTTP request cancels the operation.
- In **async** mode the server creates its own `CancellationTokenSource` per operation, which is triggered by the `DELETE` request (see below).

---

## 3. Using HTTP verbs for execution control

### 3.1 Why this is not a layer violation

UJMW deliberately forbids mapping HTTP verbs to business semantics (CRUD: read → GET, delete → DELETE, ...). This rule remains **fully in force**.

This feature uses HTTP verbs for a different kind of semantic: not *what the method does*, but *how the transport layer initiates and controls the execution* of the method. As a middleware that fully owns its HTTP usage, UJMW may define such transport-level semantics. This does not leak transport concerns into the business layer and is therefore not a layer violation.

See also: [Decision: HTTP verbs for execution control](../decisions/decision-http-verbs-for-execution-control.md).

### 3.2 Endpoint mapping

All three verbs use the **identical URL** of the operation, which makes the relationship maximally transparent.

| Verb | URL | Request DTO | Meaning |
|---|---|---|---|
| `POST` | `.../ProcessAllOpenTickets?wait=2` | regular request DTO (`{ "ticketOwnerId": 123 }`) | **Initiates** a new operation |
| `GET` | `.../ProcessAllOpenTickets?asyncId=2019847563219457&pollingSalt=a8Kd3x&pollingHash=...` | none | **Polls** an existing operation |
| `DELETE` | `.../ProcessAllOpenTickets?asyncId=2019847563219457&pollingSalt=Qm71zP&pollingHash=...&wait=2` | none | **Requests cancellation** of an existing operation |

The **response DTO is identical for all three verbs**, so a client never needs an extra poll directly after a cancellation request.

### 3.3 POST – initiate

```http
POST .../ProcessAllOpenTickets?wait=2
Content-Type: application/json

{
	"ticketOwnerId": 123
}
```

```json
{
	"currentTicketId": 123,
	"fault": null,
	"async": {
		"asyncId": "2019847563219457",
		"execState": 1,
		"pollNbf": "2025-01-01T12:00:05Z",
		"cancellable": true,
		"pollingSecret": "Zt8kQ2xW"
	}
}
```

- **100% backward compatible:** `wait` is optional. Omitting it means synchronous execution exactly as before.
- If `wait` is set:
	- after the given number of seconds the request ends and the operation continues in the background (async case),
	- the `async` property is **always** returned, even if the operation completed within the wait time.
- If the operation **completed within the wait time**, the result is delivered directly in this response. In this case **no `asyncId`** (and therefore no `pollingSecret` and no `pollNbf`) is propagated to the outside – there is nothing left to poll. The `async` object then only carries the final `execState` (e.g. `2` or `3`).
- The `async` property itself is optional in the protocol: its absence indicates that the server does not support this feature (older UJMW). An older server will ignore `wait` and simply execute synchronously, which is a safe fallback.
- `return` is only present once the operation has completed (and only for non-void methods, as before).

### 3.4 GET – poll

```http
GET .../ProcessAllOpenTickets?asyncId=2019847563219457&pollingSalt=a8Kd3x&pollingHash=...
```

```json
{
	"currentTicketId": 123,
	"return": 999,
	"fault": null,
	"async": {
		"asyncId": "2019847563219457",
		"execState": 2,
		"pollNbf": "2025-01-01T12:00:10Z",
		"cancellable": false
	}
}
```

- No request content, therefore `GET` fits naturally.
- The response DTO is identical to the `POST` variant.
- The request must be authenticated with `pollingSalt` and `pollingHash` (see 6.2).

### 3.5 DELETE – request cancellation

```http
DELETE .../ProcessAllOpenTickets?asyncId=2019847563219457&pollingSalt=Qm71zP&pollingHash=...&wait=2
```

```json
{
	"currentTicketId": 123,
	"fault": null,
	"async": {
		"asyncId": "2019847563219457",
		"execState": -2,
		"pollNbf": "2025-01-01T12:00:12Z",
		"cancellable": false
	}
}
```

- No request content.
- The request must be authenticated with `pollingSalt` and `pollingHash` (see 6.2).
- `wait` gives the operation the chance to react to the cancellation before the response is produced.
- If the operation has not yet terminated when the response is produced, `execState` is `-1` (running, cancellation requested); once it has terminated due to the cancellation, `execState` is `-2`.

### 3.6 The `async` property

| Field | Meaning |
|---|---|
| `asyncId` | Server-generated identifier of the operation, transported as **JSON string** (see 6.1) |
| `execState` | Execution state of the operation (see table below) |
| `pollNbf` | ISO UTC date-time; the client should not poll before this point in time ("not before"), calculated as described in 5.5 |
| `cancellable` | Whether a `DELETE` request is currently meaningful |
| `pollingSecret` | Only in the response to the initiating `POST`: secret used by the client to authenticate subsequent `GET` / `DELETE` requests (see 6.2) |
| `pollingSalt` | Only in `GET` / `DELETE` requests, as **query string parameter**: fresh random value generated by the client (see 6.2) |
| `pollingHash` | Only in `GET` / `DELETE` requests, as **query string parameter**: hash of `pollingSecret + pollingSalt` (see 6.2) |

#### `execState` values

| Value | Meaning |
|---|---|
| `-2` | Cancelled (terminated after an explicit cancellation request) |
| `-1` | Running, cancellation request received |
| `0` | Not yet started (queued, if a server-side queue exists) |
| `1` | Running |
| `2` | Completed successfully |
| `3` | Failed (`fault` is set) |

Negative values always indicate that a cancellation was requested.

### 3.7 Changes to existing protocol rules

- **Response property name:** the return value keeps the existing name `return` (never `result`).
- **`fault` exclusivity exception:** so far, a response containing `fault` must not contain any other property. This rule gets an exception: **`async` may exist next to `fault`**, so the client still receives the operation state of a failed operation.
- **New reserved name `async`:** `async` becomes a reserved wrapper property name, like `return`, `fault` and `_`. A collision is accepted, because `async` is a C# keyword and should not be used as a parameter name anyway.

---

## 4. Feature 2: Callbacks via `Action<...>` parameters

### 4.1 Motivation

Processing `out` parameters that are updated while the call is still running is very unpleasant on the caller side. UJMW therefore supports callback parameters as an alternative:

```csharp
int ProcessAllOpenTickets(CancellationToken c, int ticketOwnerId, Action<int> currentTicketIdCallback);
```

### 4.2 Wire format

The message wrapper stays the same as for transporting `out` parameters – but the value becomes an **array of invocations**, one property per callback parameter:

```json
{
	"currentTicketIdCallbackInvokes": [
		{ "#": 1, "arg1": 2453 },
		{ "#": 2, "arg1": 2453 },
		{ "#": 4, "arg1": 2453 },
		{ "#": 7, "arg1": 2453 }
	],
	"onSomethingOtherHappenedInvokes": [
		{ "#": 3 },
		{ "#": 5 }
	],
	"onSuccessInvokes": [
		{ "#": 6, "arg1": "hey-ho", "arg2": true }
	]
}
```

- `#` is a sequence number that is **unique across all callbacks** of one operation.
- `arg1` ... `argN` carry the arguments of the `Action<T1, ..., TN>` in positional order (generic delegates have no meaningful parameter names).
- `#` was chosen instead of `_` to avoid confusion with the sidechannel property `_`.

### 4.3 Proxy behavior

- **Server side:** for every callback parameter, a delegate is injected that caches each invocation (with its sequence number) in an array until the next poll **or** the end of the operation.
- **Client side:** with every poll response, the client fetches the invocation arrays and invokes the original delegates.

### 4.4 Officially documented rules and limitations

1. **Only `Action<...>` is supported – no `Func<...>`.** Callbacks cannot return values to the server.
2. **Global ordering is guaranteed:** the client merges all invocation arrays and replays them strictly in ascending `#` order, across all callback parameters.
3. **Callback buffer overflow never silently loses invocations** (see 4.5).

### 4.5 Callback buffer overflow

The number of buffered (not yet delivered) callback invocations per operation is limited by `callbackBufferLimit`.

When the service implementation invokes a callback while the buffer is full:

1. **Freeze phase:** the callback invocation **blocks synchronously** (inside the callback call of the service implementation) for up to `freezeTimeBeforeCallbackBufferOverflow` seconds (default `30`), waiting for a poll to drain the buffer. This prevents a short burst of e.g. 1000 rapid status messages from aborting everything.
2. **Overflow:** if the buffer is still full after this grace period, a **`UjmwCallbackBufferOverflowException`** is thrown **synchronously back to the caller of the callback** (the service implementation).

Rationale: invocations must **never be silently dropped**, but the operation must also **never hang forever**. Because the exception arrives synchronously in the service implementation, it can be handled there – e.g. by stopping to deliver further status messages from that point on, or by waiting on its own and retrying.

---

## 5. Server-side execution infrastructure

### 5.1 Pluggable operation store / executor

The management of async operations (queueing, execution, state, results and callback buffers) is abstracted behind an **interface**, so that alternative implementations (e.g. distributed stores) can be plugged in later.

The **default implementation** is an **in-memory queue**:

- it is based on Microsoft's *Hosted Service* concept (`IHostedService` / `BackgroundService`) for long-running background operations within an ASP.NET Core application – the same approach as used in the CyclicTriggering framework,
- it shuts itself down when the application terminates, honoring the global application cancellation token (`IHostApplicationLifetime.ApplicationStopping` / the `stoppingToken` of the hosted service); this token is linked into the per-operation `CancellationTokenSource`,
- operations waiting in the queue report `execState` `0`.

### 5.2 Configuration at framework initialization

All values are passed in during framework initialization in the startup. Every value has a default, so no configuration is mandatory.

| Setting | Default | Meaning |
|---|---|---|
| `maxConcurrentLongRunningAsyncCalls` | `300` | Maximum number of operations executed in parallel; further operations are queued (`execState` `0`) |
| `callbackBufferLimit` | `1000` | Maximum number of buffered, not yet delivered callback invocations per operation (see 4.5) |
| `freezeTimeBeforeCallbackBufferOverflow` | `30` (seconds) | Grace period a callback invocation blocks on a full buffer before `UjmwCallbackBufferOverflowException` is thrown (see 4.5) |
| `minOutcomePreservTime` | `10` (seconds) | Minimum time an outcome is preserved after the operation has terminated (see 5.4) |
| `maxOutcomePreservTime` | `300` (seconds) | Maximum lifetime of an outcome after the operation has terminated, independent of any limit (see 5.4) |
| `outcomeCleanupLimit` | `100` | Number of preserved outcomes above which cleanup starts (see 5.4) |
| `throttlingLevel` | `1` | Load-dependent throttling factor for the poll interval (see 5.5) |
| `CalculateNbf` | default implementation (see 5.5) | Customizing delegate hook for calculating `pollNbf` |

### 5.3 Concurrency

The number of operations executed in parallel is limited by `maxConcurrentLongRunningAsyncCalls`. Operations beyond this limit wait in the queue and report `execState` `0` until a slot becomes free.

### 5.4 Outcome retention

The **outcome** is the final result of an operation (completed, failed or cancelled – including `return`, `out` values, `fault` and remaining callback invocations), which still has to be delivered to the client.

Rules:

- An outcome is **always** preserved for at least `minOutcomePreservTime` after the operation has terminated (global minimum, applies in any case).
- If the number of preserved outcomes exceeds `outcomeCleanupLimit`, outcomes are deleted **oldest first** until the limit is met again – but **only** outcomes whose `minOutcomePreservTime` has already expired. Therefore the buffer may temporarily grow beyond `outcomeCleanupLimit`.
- `maxOutcomePreservTime` limits the lifetime of every outcome **independently of the limit**.
- **Per-operation extension:** the maximum lifetime grows per operation – but **only if a poll actually happened and a `pollNbf` was actually sent** to the client. In that case the maximum lifetime of this operation's outcome is raised to **the sent `pollNbf` point in time + the global `maxOutcomePreservTime`**. This guarantees that a client respecting a (possibly large) `pollNbf` never misses its outcome.

### 5.5 Poll interval (`pollNbf`)

`pollNbf` is calculated by a customizing delegate hook that can be replaced at framework initialization:

```text
CalculateNbf(operationStartTime, lastPollTime, overallActiveOperations)
```

The **default implementation** combines two components:

- an **individually increasing component** based on a **predetermined Fibonacci schedule** (see below),
- a **load-dependent throttling**, based on the number of overall active operations and the configurable `throttlingLevel` (default `1`).

#### Fibonacci schedule

The schedule depends **only on the elapsed time since `operationStartTime`** – not on whether or when the client has polled before. Rationale: an operation that has already been running for a long time is likely to keep running for a long time.

- The elapsed time is divided into windows bounded by consecutive Fibonacci numbers (in seconds): `1, 2, 3, 5, 8, 13, 21, 34, ...`
- A poll in the middle of a window receives the **full** Fibonacci value of that window, **not** the remainder up to the next window boundary.
- Example: a poll after 9 seconds falls into the window `8..13` and receives the full value `5` – not `4`, which would merely reach the boundary at 13 seconds.

#### Formula

```text
seconds = ROUND_TO_INTEGER( Fibonacci<window of elapsed time since operationStartTime> * ( (overallActiveOperations * throttlingLevel) \ 100 + 1 ) )
```

Mnemonic:

- `throttlingLevel = 1`: the poll interval **doubles at 100** active operations and **triples at 200**.
- `throttlingLevel = 2`: the poll interval **doubles already at 50** active operations and **triples at 100**.

---

## 6. Security

### 6.1 The `asyncId`

The `asyncId` follows the **Snowflake44** concept (see the Snowflake44 repository of SmartStandards) as closely as possible, but with **fewer random bits**, so that the value fits into 53 bits (the precise integer range of JavaScript / IEEE 754 doubles).

| Bits | Count | Semantic |
|---|---|---|
| 63..53 | 11 | unused (always `0`) |
| 52..9 | 44 | UTC time stamp in milliseconds – **identical to Snowflake44** (epoch 1900..2456, 1 ms precision) |
| 8..0 | 9 | random |

Preserved Snowflake44 properties:

- 64-bit integer (`long`), **always positive** (topmost bit unused),
- **44-bit UTC time stamp** with 1 ms precision and the same epoch,
- the creation time can be **decoded** from the ID,
- IDs are **roughly sortable by creation time**.

Deviations from Snowflake44 (where we have leeway):

- only **9 instead of 19 random bits**, therefore the time stamp sits at bit 9 instead of bit 19; `Snowflake44.DecodeDateTime` can therefore not be applied directly (decode: `id >> 9`),
- the higher collision risk (1:512 within the same millisecond) is irrelevant, because the server is the only generator for its operations and applies **collision protection** (regenerate on collision with a currently known operation).

Additional rules:

- The `asyncId` is **served as JSON string** anyway, so that no client ever needs to rely on precise integer handling.
- The `asyncId` is **not a secret** and is not meant to be unguessable – protection against foreign access is provided exclusively by the `pollingSecret` (6.2).

### 6.2 Polling authentication (`pollingSecret`)

- The response to the initiating `POST` additionally contains a **`pollingSecret`** inside the `async` object. The client stores this secret.
- Every `GET` and `DELETE` request additionally authenticates itself with two **query string parameters**:
	- `pollingSalt` – **6 new random characters**, freshly generated by the client for **every** request,
	- `pollingHash` – `Base64Url(SHA-256(UTF-8(pollingSecret + pollingSalt)))` without padding.
- The secret itself is never transmitted again after the initiating `POST`.
- **Design goal: maximum performance.** The secret is kept **short**, and the hash algorithm and encoding are chosen for minimal CPU cost on both sides (polls can be frequent and numerous):
	- `pollingSecret`: **8 alphanumeric characters** (`A-Z`, `a-z`, `0-9`), cryptographically random,
	- hash algorithm: **SHA-256** (hardware-accelerated in .NET and available in every relevant stack, e.g. Web Crypto API),
	- `pollingHash` encoding: **Base64Url** without padding (URL-safe, compact, no escaping in the query string).
- This mechanism protects operations against each other even for **anonymous services** (without any regular authentication): nobody can poll, drain or cancel a foreign operation without knowing its secret.

### 6.3 Behavior on failed polling authentication

A `GET` or `DELETE` with an unknown `asyncId` or a wrong `pollingHash`:

- does **not** drain any buffer (no callback invocations, no outcome are consumed),
- does **not** expose any information about whether the operation exists at all,
- is answered with `"fault": "Wrong asyncId or pollingHash"`.

### 6.4 Order of authentication steps

1. The **regular UJMW auth hooks always run first** – for `POST`, `GET` and `DELETE`.
	- The method name passed to the hooks is the **original main call** (e.g. `ProcessAllOpenTickets`).
	- The hooks do not notice any difference – they are not aware of the async mechanism.
2. **Afterwards**, for `GET` and `DELETE` only, the **`pollingHash` verification** (6.2) is performed.

A separate identity binding of operations is **not** provided – it is subsumed by the `pollingSecret` mechanism.

---

## 7. Contextualization, sidechannel and consumers

### 7.1 Sidechannel / ambient context

- The sidechannel (`_`) and the resulting ambient context (e.g. SmartAmbience) of the initiating `POST` are **snapshotted** and stored together with the **execution context** of the operation.
- The snapshot is **restored from the execution context before the service method is invoked** in the background execution.
- `GET` and `DELETE` requests do **not** deliver a sidechannel; the operation always runs with the context of its initiating `POST`.

### 7.2 Single consumer

- Each operation has exactly **one consumer** (the initiating client). Callback invocations and the outcome are drained by the poll that fetches them.
- Using this layer as a **shared process list** that multiple parties observe concurrently is an explicit **non-requirement**.

### 7.3 .NET dynamic client

- The .NET dynamic client is contract-agnostic (it implements the contract interface), so the `wait` value cannot be specified per call. It is configured via a **setting on `UjmwClientConfiguration`**.
- **Default: `10` seconds** – suitable for backend-to-backend operations.
- **Recommendation for UI layers** (e.g. React / UShell via `CreateServiceProxy`): about **2 seconds**, so that the UI does not freeze.
- Behavior: synchronous for the caller; internally `POST` (with `wait`) → `GET` loop respecting `pollNbf` → bulk replay of callback invocations in `#` order after every poll → `DELETE` when the caller's `CancellationToken` is cancelled.

### 7.4 TypeScript `CreateServiceProxy` (UShell)

- Follows the same principles as the .NET dynamic client (1.1): the call appears as one single operation to the caller; polling, callback replay and cancellation are handled internally.
- Recommended `wait` value for UI usage: about **2 seconds** (see 7.3).

### 7.5 UJMW.TestClient

- The output displays the **polling** (each internal `POST` / `GET` / `DELETE` round-trip with its `async` state) and the **callback invocations** in a clean, readable way.
- A **Cancel** button is offered while an operation is running; it triggers the `DELETE` request.
- A checkbox **"Ignore NBF"** is offered. When checked, `pollNbf` is ignored and polling happens in a fixed **1.5 second** cycle.

### 7.6 MCP exposure

- In the medium term, MCP shall also be able to use long-running operations via the **enrollment** of the (Swagger / OpenAPI-described) endpoints.
- Consequently, an MCP client (or agent) must be able to perform the polling protocol on its own, based **only on the generated documentation** – see 7.7.

### 7.7 Swagger / OpenAPI

- The `GET` and `DELETE` variants of long-running operations appear **officially in Swagger / OpenAPI** as separate operations on the same path as the `POST`.
- They are generated by the **dynamic controller via Reflection.Emit** as regular controller actions and passed through cleanly, including the query parameters (`wait`, `asyncId`, `pollingSalt`, `pollingHash`) and the `async` object in the response schema.
- The **generated documentation comments** on the controller actions must be detailed enough to allow a consumer without any UJMW library (e.g. an MCP client or AI agent) to implement the protocol, in particular:
	- the meaning of `wait`, `pollNbf`, `execState` and `cancellable`,
	- the **complete algorithm for generating `pollingHash`**: composition of `pollingSecret + pollingSalt`, **UTF-8** string encoding, SHA-256, Base64Url without padding, and the requirement of a fresh 6-character `pollingSalt` per request,
	- the single-consumer semantics of callback invocations and their replay order by `#`.

---

## 8. Implementation scope

- **ASP.NET Core:** dynamic UJMW controllers (server side).
- **Clients and tooling:** .NET dynamic client, TypeScript `CreateServiceProxy` (UShell), UJMW.TestClient.
- **Integrations:** MCP exposure, Swagger / OpenAPI representation.
- **WCF:** explicitly **not** implemented.

### 8.1 Implementation progress

| Area | State |
|---|---|
| Shared protocol primitives (`UjmwAsyncProtocol`: parameter classification, polling hash, secrets/salts, Fibonacci poll delay) | done |
| .NET dynamic client (`POST ?wait` → `GET` polling respecting `pollNbf` → single `DELETE` on cancellation, callback replay in `#` order, `UjmwClientConfiguration.LongRunningWaitSeconds` / `PollSleeper`, new optional transport interface `IHttpRequestExecutor`; transports without it fall back to a synchronous `POST`) | done |
| ASP.NET Core dynamic controller (pluggable `IUjmwOperationStore` with in-memory default bound to an `IHostedService`, emitted `GET` / `DELETE` actions, `POST ?wait` dispatch, outcome retention, callback buffering incl. freeze/overflow, sidechannel snapshot/restore; validated end-to-end over Kestrel with the .NET dynamic client) | done |
| Swagger / OpenAPI representation (emitted `GET` / `DELETE` actions incl. `asyncId` / `pollingSalt` / `pollingHash` / `wait` query parameters and protocol documentation comments) | done |
| TypeScript `CreateServiceProxy`, UJMW.TestClient, MCP | open |

---

## 9. Open questions (to be clarified)

Currently none.
