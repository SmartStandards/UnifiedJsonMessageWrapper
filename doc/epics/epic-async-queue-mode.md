# Epic: UJMW Async Queue Mode (Pull-Based Processing via `_queue`)

**Status:** Concept validated, all open questions and operational policies decided (not yet an active requirement)  
**Scope:** ASP.NET Core hosting (`UJMW.DynamicController`), the .NET dynamic client (`UJMW.DynamicClient`), a new processor-side runtime (`QueueProcessor<T>`), Swagger. **WCF is explicitly out of scope.**  
**Builds on:** [Epic: Long-Running Operations](epic-long-running-operations.md), [Decision: HTTP verbs for execution control](../decisions/decision-http-verbs-for-execution-control.md)

---

## 1. Abstract and motivation

Today a UJMW host always **executes** a contract itself: the dynamic controller resolves an implementation of the contract from DI and invokes it. The caller pushes work, the host processes it.

There are scenarios where the processing unit **must not (or cannot) be called**:

- **Security zones / network topology:** the processing unit lives in a protected zone (e.g. behind a firewall, inside a customer network, on an isolated machine) which allows outbound connections only. It must *pull* its jobs instead of being *called*.
- **Technical reasons:** the processing unit is a desktop agent, a container without ingress, a short-lived worker or a machine with a dynamic address.
- **Simple worker parallelization:** several identical processors pull jobs from the same queue; scaling happens by starting more processors, without a load balancer in front of them.

The **Async Queue Mode** reverses the call direction for a contract while keeping the caller side **completely unchanged**:

- The **caller** uses the regular UJMW protocol (including the long-running protocol `POST ?wait` / `GET` / `DELETE`) against a regular-looking UJMW endpoint. It does not know that a queue exists.
- The **host** does not execute the contract. It **enqueues** each call into a short-term, in-memory queue.
- A **processor** (agent) pulls queued calls via the well-known sub-route `_queue`, executes them against its real implementation, sends heartbeats (including callback invocations) and finally pushes the outcome back.

### 1.1 Explicit non-goals and delimitation

- **This is not a durable, process-spanning job queue.** Queue entries live in memory only. A restart of the host loses all queued and active entries. Even with a durable queue, a restarted *caller* could not reconnect to its job, because the UJMW long-running protocol (on which the caller side relies) has no reconnect capability (see the non-goals of the long-running epic).
- It is a **short-term queue to reverse the call direction** (push → pull), not a message bus, not a scheduler, not a workflow engine.
- No exactly-once guarantee (see 10).
- No fan-out / publish-subscribe: every queue entry is processed by exactly one processor at a time.

### 1.2 Positive differentiation from competing approaches

| Approach | Drawback compared to the UJMW queue mode |
|---|---|
| Message broker (RabbitMQ, Azure Service Bus, ...) | Additional infrastructure, separate message contracts, the caller must be rewritten (no typed RPC), no synchronous caller experience |
| Reverse tunnels / relays (e.g. Azure Relay, ngrok) | Inbound semantics are only emulated; security teams often reject them; no worker parallelization |
| Hand-written "job table + polling" | Duplicates protocol, DTOs, sidechannel, auth and cancellation for every contract |
| SignalR / WebSockets with server-to-client invocation | Stateful long-lived connections, no simple HTTP pull, poor fit for strict outbound-only proxies |

The queue mode reuses the **typed contract**, the **DTO projection**, the **sidechannel**, the **auth hooks**, **callbacks** and **cancellation** of UJMW. Neither the caller nor the processor implementation needs to know about the queue.

---

## 2. Architectural overview

```text
 Caller (unchanged UJMW client)
	 │  POST <route>/<Method>[?wait=..]  /  GET (poll)  /  DELETE (cancel)
	 ▼
 ┌──────────────────────────────────────────────────────────────────────────────┐
 │ UJMW host (ASP.NET Core)                                                     │
 │                                                                              │
 │  Dynamic controller for TContract  (emitted exactly as for AddControllerFor) │
 │      │  "implementation instance" = dynamic UJMW CLIENT proxy of TContract   │
 │      ▼                                                                       │
 │  UjmwQueueBridgeExecutor : IHttpPostExecutor, IHttpRequestExecutor           │
 │      │  (JSON request / poll / cancel  →  clear method calls)               │
 │      ▼                                                                       │
 │  IUjmwQueuingService  (caller side: Enqueue / Poll / RequestCancel)          │
 │      │  inherits                                                             │
 │      ▼                                                                       │
 │  IUjmwQueue           (processor side: Info / Dequeue / Heartbeat /          │
 │      ▲                 Return / Complete)                                     │
 │      │                                                                       │
 │  Queue controller  <route>/_queue  (GET / PUT / DELETE / POST)               │
 └──────▲───────────────────────────────────────────────────────────────────────┘
				│ HTTP (outbound only, from the processor's point of view)
				│
 ┌──────┴────────────────────────────┐        ┌──────────────────────────────────┐
 │ UjmwQueueClient : IUjmwQueue      │   or   │ in-process: the host's           │
 │ (remote processor)                │        │ IUjmwQueuingService as IUjmwQueue │
 └──────▲────────────────────────────┘        └──────────────▲───────────────────┘
				│                                                    │
				└────────────────  QueueProcessor<T>  ───────────────┘
															│ forward-directed call
															▼
											 real implementation of T
```

### 2.1 Why deserialize and re-serialize the request?

At first sight it looks wasteful: the controller deserializes the incoming JSON into the emitted request DTO, maps it to method arguments, and the client proxy serializes these arguments back to JSON. The reasons:

1. **The request cannot be accepted "raw"** without duplicating essential parts of the emitted controller: routing, request DTO types, model binding, the `?wait` handling, the emitted `GET` / `DELETE` long-running actions, auth filter attributes, `[AllowAnonymous]`, API group names and all Swagger / OpenAPI annotations (including XML documentation). The queue endpoint must look **exactly** like a regular endpoint in Swagger and MCP.
2. **The controller cannot simply re-serialize** the request without duplicating what the dynamic client already does: argument-to-DTO projection, `ref` / `out` handling, exclusion of `CancellationToken` and callback parameters, sidechannel capture, the long-running protocol (`POST ?wait` → `GET` → `DELETE`), callback replay and fault handling. The dynamic client is the existing, tested implementation of exactly this projection – therefore it is used as the adapter.
3. **Symmetry:** the JSON stored in the queue is **exactly the wire format** a regular UJMW host would receive. A processor therefore needs no knowledge beyond the UJMW protocol itself (Level B / Level C interoperability, see the UJMW skill), and processors can be written in any technology stack.

The cost (one additional in-process JSON round-trip per call) is negligible compared to the latency of a queue.

---

## 3. Host side: registration

### 3.1 `AddQueueFor` – a neighbor of `AddControllerFor`

The existing registration method is `DynamicUjmwControllerRegistrar.AddControllerFor<TService>(...)`. The queue mode adds a neighbor with **identical overloads and identical options** (`DynamicUjmwControllerOptions`):

```csharp
services.AddDynamicUjmwControllers((DynamicUjmwControllerRegistrar r) => {

	r.AddControllerFor<IRegularService>("api/regular");

	r.AddQueueFor<IReportRenderer>((DynamicUjmwControllerOptions o) => {
		o.ControllerRoute = "api/reports";
	});

});
```

Behavior:

- The **same factory** (`DynamicUjmwControllerFactory.BuildDynamicControllerType`) emits the controller – including all actions, long-running `GET` / `DELETE` actions, auth filters and Swagger metadata.
- The emitted controller does **not** require an implementation of `TService` from DI. Instead it requires the (non-generic) `IUjmwQueuingService` (indirectly, see 3.2).
- Additionally, the sub-route **`<route>/_queue`** is registered (see 5).
- Sub-services (properties of the contract) are **not supported in phase 1**: `AddQueueFor` fails fast at registration if the contract exposes sub-service properties (decided Q-SUB, support in a later iteration).

### 3.2 Injection of the client proxy (bridge)

The emitted controller constructor currently receives the root service instance of type `TService` from DI. For queue mode the constructor parameter type is replaced by an internal, generic bridge type:

```csharp
internal sealed class UjmwQueueBridge<TService> : IUjmwQueueBridge {

	// created once per contract type (singleton): a dynamic UJMW client proxy of TService
	// whose transport is a UjmwQueueBridgeExecutor bound to the IUjmwQueuingService from DI

}
```

- `UjmwQueueBridge<>` is registered once as **open generic singleton** by `AddQueueFor` (if not already registered).
- `DynamicControllerBase<TService>` unwraps the bridge: if the injected root instance is an `IUjmwQueueBridge`, its `ContractProxy` is used as root instance. Everything else (`SubServiceNavPath`, `MethodRedirector`, long-running dispatch) stays untouched.
- **Why not register `TService` itself in DI?** The same process may also host or consume a real implementation of `TService`; registering a queue proxy as `TService` would hijack those resolutions.

### 3.3 The `IUjmwQueuingService` in DI

- If no `IUjmwQueuingService` is registered, `AddQueueFor` registers the default **`UjmwInMemoryQueuingService`** as singleton (out-of-the-box usability, no mandatory configuration).
- Custom implementations (e.g. with different priorities, metrics, quotas) can be registered before.

### 3.4 Minimal-invasive integration into the existing code

All queue-specific wire-ups live in **separate partial-class files** (e.g. `DynamicUjmwControllerFactory.QueueMode.cs`, `DynamicUjmwControllerRegistrar.QueueMode.cs`) and in a new folder `QueueMode/`. The original files receive only these minimal hooks:

| Original file | Minimal change |
|---|---|
| `DynamicUjmwControllerRegistrar.cs` | add the `partial` modifier; the entry list must carry a "queue mode" flag (or a second list is held in the partial file) |
| `DynamicUjmwControllerFactory.cs` | one branch point: the type of the emitted constructor parameter (`TService` vs. `UjmwQueueBridge<TService>`) |
| `DynamicUjmwControllerFactory.DynamicControllerBase.cs` | one branch in the constructor: unwrap `IUjmwQueueBridge`; one selection point for the operation store (the bridge provides a dedicated, unlimited `IUjmwOperationStore` instance, see C-1) |
| `SetupExtensions.cs` | none (the hosted service registration stays as is) |

The `_queue` controller is **not** mixed into the emitted contract controller: a small handwritten base class `UjmwQueueControllerBase` contains the logic, and the partial factory file emits only a tiny subclass per contract carrying the `[Route("<route>/_queue")]`, `[ApiExplorerSettings(GroupName = ...)]` and auth filter attributes.

---

## 4. Interfaces

All methods carry the **contract type** as first parameter, so that every implementation (and the remote `UjmwQueueClient`) can map an entry to its concrete endpoint.

**Queue key (decided, Q-KEY):** a queue is identified by **contract type + resolved controller route** (`queueName` = resolved controller route). Therefore a contract registered twice (different routes, generic contracts, sub-services) gets separate queues.

- `queueName == null` addresses "the only queue of this contract type"; if the contract type is registered more than once, `null` is rejected with a `fault` (ambiguous).
- The remote `UjmwQueueClient` still resolves its URL by contract type via the `urlGetter` (9.3); on the wire the queue is identified by its URL, so `queueName` is not transported. It is only relevant for in-process processors.

**Auth hook mapping (decided, Q-AUTH):** for `_queue` actions the regular auth hooks receive the queued **contract type** as `contractType` and the corresponding **reference method of `IUjmwQueue`** (`GetInfo`, `Dequeue`, `Heartbeat`, `Return`, `Complete`) as `targetContractMethod`. Hooks therefore never receive `null` and can distinguish queue access by `targetContractMethod.DeclaringType == typeof(IUjmwQueue)` (e.g. to authorize processors differently from callers).

### 4.1 `IUjmwQueue` – processor side (basis of everything under `_queue`)

```csharp
public interface IUjmwQueue {

	/// <summary>
	/// Returns statistics about the queue of the given contract (no side effects).
	/// </summary>
	UjmwQueueInfo GetInfo(Type contractType, string queueName);

	/// <summary>
	/// Allocates the next matching entry for the given processor. The allocation must be confirmed
	/// by a first heartbeat within the dequeue confirmation timeout, otherwise it is withdrawn.
	/// Returns an empty lease (RequestId == null) if no matching entry is available.
	/// </summary>
	UjmwQueueLease Dequeue(Type contractType, string queueName, string processorId, UjmwQueueMethodFilter methodFilter);

	/// <summary>
	/// Heartbeat of a leased entry. Delivers buffered callback invocations (JSON, UJMW callback wire format)
	/// and returns whether a cancellation was requested by the caller.
	/// </summary>
	UjmwQueueHeartbeatResult Heartbeat(
		Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash,
		string callbackInvocationsJson
	);

	/// <summary>
	/// Returns a leased entry to the queue (or confirms the cancellation if it was requested).
	/// </summary>
	UjmwQueueResult Return(Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash);

	/// <summary>
	/// Delivers the final UJMW response (return / out / ref / fault / remaining callback invocations / sidechannel).
	/// If 'dequeueNext' is not null, the next matching entry is allocated in the same round-trip
	/// and returned in the shape of a dequeue result (otherwise the returned lease is null).
	/// </summary>
	UjmwQueueLease Complete(
		Type contractType, string queueName, string processorId, string requestId, string pollingSalt, string pollingHash,
		string responseJson, UjmwQueueMethodFilter dequeueNext
	);

}
```

### 4.2 `IUjmwQueuingService` – caller side (plus processor side by inheritance)

```csharp
public interface IUjmwQueuingService : IUjmwQueue {

	/// <summary>
	/// Enqueues a UJMW request (wire format JSON incl. sidechannel) for the given transport-level method name.
	/// </summary>
	UjmwQueueTicket Enqueue(Type contractType, string queueName, string methodName, string requestJson, bool isLongRunning);

	/// <summary>
	/// Returns the current state of an entry: execution state, drained callback invocations
	/// and - once terminated - the final response JSON. Returns false for unknown entries / wrong hash.
	/// </summary>
	bool TryGetState(
		Type contractType, string queueName, string entryId, string pollingSalt, string pollingHash, TimeSpan maxWait,
		out UjmwQueueEntryState state
	);

	/// <summary>
	/// Requests the cancellation of an entry. A queued entry is removed immediately (execState -2),
	/// a leased entry is flagged ('cancellationRequested' on the next heartbeat).
	/// </summary>
	bool TryRequestCancel(
		Type contractType, string queueName, string entryId, string pollingSalt, string pollingHash, TimeSpan maxWait,
		out UjmwQueueEntryState state
	);

}
```

The separation is important: a `QueueProcessor<T>` only ever needs `IUjmwQueue`. Therefore the remote `UjmwQueueClient` only has to implement `IUjmwQueue`, and an in-process processor can directly use the host's `IUjmwQueuingService`.

### 4.3 `UjmwQueueBridgeExecutor` – the adapter

Implements `IHttpPostExecutor` **and** `IHttpRequestExecutor` (the latter is mandatory, otherwise the dynamic client falls back to a synchronous `POST` for long-running methods). The client proxy is created with a synthetic URL (e.g. `ujmw-queue://<contract-key>/<subservice-path>`), so the executor can parse contract and transport-level method name from the URL.

| Client action (dynamic client) | Mapped to |
|---|---|
| `POST <m>` (no `wait`, regular method) | `Enqueue(..., isLongRunning: false)` and **blocking** wait until the entry has terminated (see 8) |
| `POST <m>?wait=..` (long-running method) | `Enqueue(..., isLongRunning: true)`; returns immediately with `async.execState = 0` (queued), internal `asyncId` and `pollingSecret` |
| `GET <m>?asyncId&pollingSalt&pollingHash` | `TryGetState` |
| `DELETE <m>?asyncId&pollingSalt&pollingHash&wait` | `TryRequestCancel` |
| `GET <root>` (info endpoint) | not supported (the regular controller answers info requests itself) |

---

## 5. Wire protocol of `<route>/_queue`

General rules:

- All responses are JSON with HTTP `200`; queue framework errors are reported via `fault` (UJMW style). `fault` only carries errors of the **queue framework itself** (never business faults – those travel inside the embedded UJMW response).
- `401` / `403` as produced by the regular auth hooks.
- `pollingSalt` / `pollingHash` (uniform names with the long-running protocol, decided Q-NAMES): identical algorithm as the long-running polling authentication: `pollingHash = Base64Url(SHA-256(UTF-8(pollingSecret + pollingSalt)))` without padding, a fresh 6-character `pollingSalt` per request (reuse of `UjmwAsyncProtocol.ComputePollingHash`).
- `requestId` is a 53-bit Snowflake-style id transported as JSON string (same generator as `asyncId`).
- `processor` is a purely informational name of the processor instance (statistics, diagnostics). It is **not** a security identity and may change during a lease (e.g. in a cluster where processors hand jobs over to each other); the queuing service simply updates the name stored for the entry. Only `requestId`, `pollingSalt` and `pollingHash` authorize `PUT` / `DELETE` / `POST` (decided Q-PROC).

### 5.1 `GET <route>/_queue` – info

Without `dequeue` the endpoint only returns statistics; `processor` is ignored. Whether the auth hooks run or anonymous access is allowed is configurable (`UjmwQueueOptions.AllowAnonymousInfo`, default: auth hooks run).

```json
{
	"statsTotal": {
		"queued": 12, "active": 3,
		"past1m": 0, "past10m": 0, "past1h": 0, "past10h": 0
	},
	"statsPerMethod": {
		"RenderReport": { "queued": 10, "active": 2, "past1m": 0, "past10m": 0, "past1h": 0, "past10h": 0 },
		"RenderPreview": { "queued": 2, "active": 1, "past1m": 0, "past10m": 0, "past1h": 0, "past10h": 0 }
	},
	"processors": {
		"worker-01@host-a": { "active": 1, "lastActivity": "2025-01-01T12:00:00Z" },
		"worker-02@host-b": { "active": 2, "lastActivity": "2025-01-01T12:00:03Z" }
	},
	"fault": null
}
```

- `lastActivity` is the latest of dequeue poll, heartbeat or completion.
- `pastXX` are sliding-window counters of entries which **terminated** within the window (in any final state: completed, failed, cancelled, withdrawn by policy; decided Q-STATS).
- There is **no** `raw` parameter (a human-friendly HTML representation was considered and discarded).

### 5.2 `GET <route>/_queue?processor=<id>&dequeue=<filter>` – dequeue (lease)

`dequeue` filter syntax:

| Value | Meaning |
|---|---|
| `*` | next entry of any method |
| `m1,m4,m5` | whitelist |
| `!m1,!m4,!m5` | blacklist |
| mixed positive and negated items | rejected with `fault` |

Method names are the **transport-level** method names (as used in the URL, i.e. including the disambiguation of overloads).

```json
{
	"totalInQueue": 234,
	"method": "RenderReport",
	"requestId": "2019847563219457",
	"request": { "reportId": 42, "format": "pdf", "_": { "tenantId": "..." } },
	"pollingSecret": "Zt8kQ2xW",
	"heartbeatSec": 3,
	"fault": null
}
```

Empty queue:

```json
{ "totalInQueue": 0, "method": null, "requestId": null, "request": null, "pollingSecret": null, "heartbeatSec": 0, "fault": null }
```

- The auth hooks run.
- The entry is **immediately allocated** (tentative lease) for this processor.
- **Dequeue confirmation (two-phase handshake):** the processor must confirm the allocation with a first heartbeat (`PUT`, see 5.3) within `dequeueConfirmationTimeoutSec` (default `2` seconds).
	- Only **after** a successful confirming `PUT` the processor may start the actual execution of the job.
	- Without a timely confirmation the allocation is **withdrawn immediately** and the entry becomes available again **with priority** (it was guaranteed never executed, so redelivery is always safe, independent of any redelivery policy).
	- A late confirming `PUT` is answered with `"cancellationRequested": true` and `"fault": "Withdrawn - dequeue confirmation missed"`; the processor must discard the job.
	- Rationale: `GET` stays the verb for dequeueing (transport control, see the decision record). Unintended `GET` requests (automatic retries of HTTP stacks, prefetching, tools, lost responses) can never swallow an entry, because such an allocation is never confirmed and is released after 2 seconds.
- After the confirmation the entry is **leased**. `heartbeatSec` tells the processor the expected interval of the subsequent `PUT` requests (default `3` seconds). If no heartbeat arrives within `leaseLossTimeoutSec` (default `60` seconds), the server-side lease-loss handling is invoked (see 10).
- **Selection order (decided Q-FIFO):** there is exactly **one FIFO queue** per queue key across all methods. A dequeue takes the **front-most entry that matches** the method filter (cherry-picking by an explicit filter simply skips non-matching entries; their position is not changed).
- `pollingSecret` is generated **per allocation**
- `request` is exactly the UJMW request JSON (incl. the sidechannel `_`) that a regular host would receive.

### 5.3 `PUT <route>/_queue?processor&requestId&pollingSalt&pollingHash` – heartbeat

Body (optional): buffered callback invocations in the UJMW callback wire format:

```json
{
	"reportProgressInvokes": [
		{ "#": 1, "arg1": "Rendering", "arg2": 3, "arg3": 10 }
	]
}
```

Response:

```json
{ "cancellationRequested": false, "fault": null }
```

Late confirmation (allocation already withdrawn, see 5.2):

```json
{ "cancellationRequested": true, "fault": "Withdrawn - dequeue confirmation missed" }
```

- The **first** `PUT` after a dequeue is the **dequeue confirmation** (see 5.2); the entry switches from "allocated" to "leased" / running (`execState 1` for the caller).
- Renews the lease.
- Callback invocations are appended to the **entry-scoped callback buffer** which is drained by the caller-side polls (logical counterpart of a poll: the heartbeat delivers the "food" for the caller's poll).
- If the entry-scoped buffer is full, the heartbeat answers with a dedicated `fault` and **does not** accept the invocations; the processor retries them with the next heartbeat (back pressure, see 7.2).

### 5.4 `DELETE <route>/_queue?processor&requestId&pollingSalt&pollingHash` – return / confirm cancellation

```json
{ "fault": null }
```

- If `cancellationRequested` was **false**: the lease is released and the entry is **requeued with priority** according to the release rule (decided Q-REQUEUE):
	- Such a release is never a regular business outcome (a business error is a `POST` with a `fault` response). Releases are special cases such as a processor shutdown (`Kill`); nevertheless a small protection against "poison" entries is built in.
	- Every release increments the entry's `releaseCount`. The entry is reinserted at a position following the Fibonacci sequence without the duplicate `1`: `0, 1, 2, 3, 5, 8, 13, 21, 34, 55` for the releases 1 to 10. If the queue currently holds fewer entries, the entry is appended at the end (as far to the front as possible).
	- The 11th release is not requeued anymore; the entry terminates and the caller receives the fault `Dequeued and Released 10 Times (Limit)`.
- If `cancellationRequested` was **true**: confirms that the work was really aborted; the entry terminates with `execState -2` for the caller.

### 5.5 `POST <route>/_queue?processor&requestId&pollingSalt&pollingHash` – completion

```json
{
	"response": { "return": "...", "fault": null, "_": { } },
	"dequeueNext": null
}
```

```json
{ "fault": null }
```

**`dequeueNext` – round-trip reduction for sequential processing:** instead of a separate dequeue `GET` after each completion, the processor can request the next job in the same request in which it delivers the result of the previous one.

- `dequeueNext` carries the **method filter** with exactly the syntax of the `dequeue` query parameter (`"*"`, `"m1,m4"`, `"!m1,!m4"`); `null` (or missing) means: no next job.
- If a filter is given, the response has exactly the shape of the dequeue response (5.2: `totalInQueue`, `method`, `requestId`, `request`, `pollingSecret`, `heartbeatSec`, `fault`); otherwise only `fault`.
- The next job is subject to the same **dequeue confirmation** rule (first `PUT` within 2 seconds).
- If the completion itself is rejected (e.g. lease lost), no next job is allocated.

```json
{
	"response": { "return": "...", "fault": null },
	"dequeueNext": "*"
}
```

```json
{
	"totalInQueue": 233,
	"method": "RenderReport",
	"requestId": "2019847563219512",
	"request": { "reportId": 43, "format": "pdf" },
	"pollingSecret": "Pq3mN8vY",
	"heartbeatSec": 3,
	"fault": null
}
```

- `response` is exactly the UJMW response JSON (incl. `return`, `out` / `ref` values, `fault`, sidechannel and remaining callback invocations) that a regular host would produce.
- With `dequeueNext` (round-trip reduction) the response carries the next allocation in the same shape as 5.2.
- A completion for an expired lease is rejected with `fault` (see 10).

### 5.6 Queue policies and timing defaults (decided)

| Policy | Option (`UjmwQueueOptions` / `QueueProcessor<T>`) | Default | Rule |
|---|---|---|---|
| Queue capacity (Q-CAP) | `UjmwQueueOptions.MaxQueueSize` | `1000` | When the queue is full, the enqueue is rejected **immediately**; the caller receives the fault `Queue capacity exceeded` (no waiting, no blocking) |
| Selection order (Q-FIFO) | – | – | one FIFO queue across all methods; the dequeue takes the front-most entry matching the filter |
| Idle polling (Q-TIMING) | `QueueProcessor<T>.Start(pollingIntervalSec)` | `2` s | interval of dequeue polls while the processor is idle / the queue is empty |
| Dequeue confirmation (Q-TIMING) | `UjmwQueueOptions.DequeueConfirmationTimeoutSec` | `2` s | the first `PUT` must arrive within this time, otherwise the allocation is withdrawn |
| Heartbeat interval (Q-TIMING) | `UjmwQueueOptions.HeartbeatSec` (transported as `heartbeatSec`) | `3` s | interval of the subsequent `PUT` requests (from the second `PUT` on) |
| Lease loss (Q-TIMING) | `UjmwQueueOptions.LeaseLossTimeoutSec` | `60` s | maximum gap between two `PUT` requests before the server-side lease-loss handling (`RedeliveryPolicy`, see 10) is invoked |
| Release / requeue (Q-REQUEUE) | – | max `10` | reinsertion at index 0, 1, 2, 3, 5, 8, 13, 21, 34, 55 (Fibonacci without duplicate 1); the 11th release terminates the entry with the fault `Dequeued and Released 10 Times (Limit)` |
| Callback buffer (Q-BUF) | same options as the long-running feature | same as long-running | the entry-scoped callback buffer towards the caller behaves exactly like the long-running callback buffer (limit, freeze, overflow) |
| Outcome retention (Q-RET) | same options as the long-running feature | same as long-running | terminated entries are retained / evicted with the retention definition of the long-running feature |
| Host shutdown (Q-SHUT) | – | – | see C-7: running processors / jobs are **not** cancelled by the queue host |

---

## 6. Interaction with the long-running feature

This section validates the queue mode against the existing artifacts of the long-running feature (`IUjmwOperationStore`, `UjmwInMemoryOperationStore`, `UjmwLongRunningOptions`, `UjmwOperationStoreHostedService`, `UjmwWebCallInvoker`, `UjmwClientConfiguration`).

### 6.1 Resulting call chain for a long-running method

```text
Caller POST ?wait
 → emitted controller: InvokeLongRunningMethod
 → IUjmwOperationStore.Initiate(scope, body, wait)            [outer operation, own thread]
		 body: contract proxy (dynamic client) is invoked on a background thread
			 → UjmwWebCallInvoker: POST ?wait via UjmwQueueBridgeExecutor
				 → IUjmwQueuingService.Enqueue                         [inner "operation" = queue entry]
			 → UjmwWebCallInvoker: GET polling loop (PollSleeper)    [blocks the outer thread]
				 → IUjmwQueuingService.TryGetState
					 (callback invocations → replayed into the outer IUjmwCallbackRecorder)
Caller GET / DELETE → outer operation store (unchanged)
```

### 6.2 Conflicts and mitigations

| # | Conflict | Impact | Proposed mitigation |
|---|---|---|---|
| C-1 | **Operation store slots / threads:** every queued call occupies an outer long-running operation (and a thread) in the process-wide `IUjmwOperationStore` while it only waits for a processor. | Queued calls could starve the regular long-running endpoints of the same host (concurrency limit). | **Decided (Q-1):** queue-mode controllers use a **dedicated operation store instance** without concurrency limit (the bodies only wait), selected per controller. Requires one small selection hook in `DynamicControllerBase` (listed in 3.4). |
| C-2 | **Double async state:** outer `asyncId` / `pollingSecret` (caller ↔ host) and inner entry id / secret (bridge ↔ queuing service). | Overhead only; the inner values never leave the process. | Accept. The inner secret check may be skipped for in-process bridge calls. |
| C-3 | **`execState` 0 vs. 1:** the outer operation reports "running" (`1`) as soon as its body runs, although the entry is still queued. | The caller cannot distinguish "queued" from "running". | **Decided (Q-1):** accepted in phase 1; an optional "still queued" hook may follow later. |
| C-4 | **Triple callback buffering:** processor buffer → entry-scoped buffer → outer `IUjmwCallbackRecorder` buffer. | Latency; overflow semantics must chain correctly. | Back pressure chain (7.2). The bridge replays synchronously into the recorder, so a freeze of the outer buffer blocks the inner polling – which in turn fills the entry buffer – which rejects heartbeats – which freezes the processor. No invocation is silently lost. |
| C-5 | **Global static client settings** (`UjmwClientConfiguration.LongRunningWaitSeconds`, `PollSleeper`, `FaultRepsonseHandler`, `RequestErrorDiagnosticHook`, sidechannel configuration) also apply to the bridge's client proxy. | The poll cadence of the bridge follows settings meant for real outbound calls; fault messages may be wrapped twice; diagnostics hooks see synthetic `ujmw-queue://` URLs. | The bridge executor answers `pollNbf` itself (short, in-process) and may implement an in-process long-poll (`TryGetState(maxWait)`); document that hooks may see `ujmw-queue://` URLs; the bridge passes business faults through unchanged. |
| C-6 | **Sidechannel round-trip:** the outer operation restores the inbound sidechannel into the ambient context; the bridge's client proxy would re-capture it via the client sidechannel configuration. | Values not covered by the client configuration would be lost or altered. | **Decided (Q-SIDE):** the bridge forwards the **original inbound sidechannel snapshot** unchanged into the queue entry (no re-capturing); the processor restores it inside an isolated job task (9.1). |
| C-7 | **Shutdown order:** `UjmwOperationStoreHostedService` cancels all outer operations on shutdown; the client proxies then send `DELETE` through the bridge. | If the queuing service is disposed first, cancellations fail noisily; a cancellation forwarded to the processors would abort work that could still be completed elsewhere. | **Decided (Q-SHUT):** the queuing service outlives the operation store shutdown. On shutdown of the queue host the **running jobs on the processors are not cancelled** (no `cancellationRequested` is raised because of the shutdown); bridge-originated cancellations caused by the host shutdown are not forwarded to leased entries. Rationale: a later persistent, shared queue store with a partner host (reachable via fallback URLs) could take over the entries – not part of phase 1, but the behavior is already the cleaner one. Pending caller requests of the shutting-down host receive the fault `host is shutting down`. |
| C-8 | **Synchronous methods** (without `CancellationToken`) are sent as a single blocking `POST`. | HTTP timeouts apply, no cancellation, a request thread is blocked. | **Decided (Q-2):** allowed with a configurable timeout (see 8). |
| C-9 | **Single consumer rule** (long-running epic 7.2). | The `_queue` info endpoint is a shared observation. | No conflict: info is statistics only and never drains anything. |
| C-10 | **Route `_queue`** vs. the decision record which rejected separate control endpoints because of possible collisions with method names. | A contract method named `_queue` is a valid C# identifier. | `_queue` becomes a **reserved transport-level name**; `AddQueueFor` fails fast at registration if a contract method maps to it. The decision record's argument targets *per-method* control endpoints; `_queue` is a per-*controller* infrastructure endpoint (like the info endpoint). |
| C-11 | **Auth hooks** receive the invoked `MethodInfo` of the contract; for the `_queue` actions there is no contract method. | Existing hooks may not expect `null`. | **Decided (Q-AUTH):** the reference methods of `IUjmwQueue` are passed (see 4). |
| C-12 | **Queue identity:** the same contract can be registered under several routes (e.g. with different options). | With the contract type as queue key, both controllers would share one queue. | **Decided (Q-KEY):** key = contract type + resolved controller route (see 4). |

---

## 7. Callbacks and cancellation end-to-end

### 7.1 Callbacks

1. The processor runs the real implementation with recording delegates for every `Action<...>` parameter (same sequence semantics `#` as in the long-running epic).
2. Buffered invocations are sent with the next heartbeat (`PUT`). A heartbeat is sent **early** when invocations are pending (not only on the `heartbeatSec` cadence), bounded by a minimal interval.
3. The queuing service appends them to the entry-scoped buffer **preserving `#`**.
4. The bridge drains them with every `TryGetState` and returns them as `<name>Invokes` arrays; the client proxy replays them in `#` order into the delegates it received – which are the recorder delegates of the outer operation.
5. The caller drains them with its regular polls.

The **convention-based progress display** (callback name contains `progress`, see `ILongRunningDemoService`) works transparently, including the `cancellable` restriction.

### 7.2 Back pressure

- Processor-side buffer limit with freeze / overflow semantics identical to the long-running epic (4.5): the callback invocation inside the implementation blocks while the buffer is full and finally throws `UjmwCallbackBufferOverflowException`.
- The entry-scoped buffer towards the caller has **exactly the semantics and options of the long-running callback buffer** (decided Q-BUF).
- A heartbeat whose invocations are rejected (entry buffer full) keeps them in the processor buffer.

### 7.3 Cancellation

```text
Caller DELETE → outer operation token cancelled
 → client proxy sends DELETE via bridge → TryRequestCancel
		 queued entry  → removed, terminates with execState -2 immediately
		 leased entry  → flagged; next PUT answers "cancellationRequested": true
 → processor cancels the CancellationToken of the running implementation
 → processor: DELETE (confirm, execState -2)   or   POST (if the work completed anyway / ignored the token)
 → bridge poll sees the terminal state → client proxy completes → outer operation terminates
```

---

## 8. Regular (synchronous) methods in queue mode

For methods without `CancellationToken` the dynamic client sends a single synchronous `POST`. The bridge must block until the entry terminates. Consequences: HTTP timeouts apply, no cancellation by the caller, a request thread is blocked.

**Decided (Q-2):** such methods are **allowed**, bounded by `UjmwQueueOptions.SynchronousCallTimeoutSec` (default: `100` seconds, the default `HttpClient` timeout of many stacks):

- after the timeout the caller receives a `fault` (`Queue processing timeout`),
- the entry is cancelled: a queued entry is removed, a leased entry gets `cancellationRequested: true` with its next heartbeat,
- documentation recommends declaring a `CancellationToken` for every method that is intended for queue mode.

---

## 9. Processor side

### 9.1 `QueueProcessor<T>`

```csharp
QueueProcessor<IReportRenderer> processor = new QueueProcessor<IReportRenderer>(
	innerService, queue, maxParallelTasks: 4,
	methodWhitelist: new string[] { nameof(IReportRenderer.RenderReport), nameof(IReportRenderer.RenderPreview) },
	methodBlacklist: null,
	hardShutdown: applicationStoppingToken
);

processor.Start(pollingIntervalSec: 2);
```

| Member | Meaning |
|---|---|
| `InnerService` | the real implementation of `T` |
| `CurrentTasks` | number of currently running jobs |
| `Start(int pollingIntervalSec = 2)` | starts an internal poller (dequeue whenever a slot is free) |
(the job sends its heartbeats autonomously) |
| `Stop(bool wait = false)` | stops the internal poller; running jobs continue (optionally wait for them) |
| `Kill(bool wait = true)` | stops the poller, **cancels** the running implementations and returns their entries via `DELETE` in parallel as fast as possible; optionally waits synchronously until all returns are done. Also triggered by `hardShutdown` |

Notes:

- Sub-services of `T` are **not supported in phase 1** (later iteration, decided Q-SUB).
- Polling uses **one** request per `_queue` (filter via `dequeue`), not one per method.
- Every allocation is confirmed with an immediate first `PUT` **before** the implementation is invoked; a rejected confirmation (`Withdrawn - dequeue confirmation missed`) discards the job silently (trace log only).
- With `maxParallelTasks == 1` (sequential processing) the processor uses `dequeueNext` on completion to save the separate dequeue round-trip.
- `Kill` must cancel the running implementations; otherwise a returned entry could be executed twice.

#### Method filters (decided, Q-FILTER)

Whitelist and blacklist are simple **`string[]`** parameters, typically filled with `nameof(...)`:

- `methodWhitelist`: `null` = all methods allowed; otherwise only the listed methods.
- `methodBlacklist`: `null` = nothing excluded; otherwise the listed methods are excluded.
- A name selects the method **including all its overloads** (all transport-level variants of that name).
- The wire protocol accepts either a whitelist or a blacklist; if both are given, the processor sends the **effective whitelist** (whitelist minus blacklist) computed on the processor side.
- Unknown method names are a configuration error and fail fast in the constructor.

#### Isolation of the ambient context (decided, Q-SIDE)

Every job is executed in its **own sub-thread (`Task`)** – also for `maxParallelTasks == 1` and also for in-process processors:

- The `ExecutionContext` flows into the task, so all `AsyncLocal` values (ambient context, e.g. SmartAmbience) are **cloned copy-on-write** for the job.
- Within this sandbox the processor restores the sidechannel of the queue entry and can **overwrite** ambient values freely; nothing leaks back into the poller or into other jobs.
- Ambient keys covered by the processor-side sidechannel configuration but **missing** in the entry's sidechannel are explicitly reset inside the sandbox, so that values of the processor's own context never leak into a job.

### 9.2 JSON dispatcher (new component in `UJMW.DynamicClient`)

The processor receives UJMW **request JSON** and must invoke `T` – which is host-side logic (`MethodRedirector`). Since a processor must not depend on ASP.NET Core, a new lightweight, reflection-based dispatcher in `UJMW.DynamicClient` is required:

- resolves the transport-level method name (incl. overload disambiguation identical to the client),
- deserializes named arguments with Newtonsoft.Json, binds `CancellationToken` and recording callback delegates,
- restores the inbound sidechannel into the ambient context **inside the job sandbox task** (9.1), based on a new, configurable processor-side "incoming request sidechannel" configuration (today this concept only exists host-side),
- builds the UJMW response JSON (`return`, `out` / `ref`, `fault`, outgoing sidechannel).

### 9.3 `UjmwQueueClient : IUjmwQueue`

```csharp
IUjmwQueue queue = new UjmwQueueClient(urlGetter, authHeaderGetter);
```

- `urlGetter` has the signature `Func<Type, string>` (contract type → controller base URL) with the same fallbacks as the dynamic client (`UjmwClientConfiguration.DefaultUrlGetter`, `DefaultAuthHeaderGetter`, `HttpClientFactory`).
- The contract type passed to every `IUjmwQueue` method is forwarded to the `urlGetter`; `"/_queue"` is appended to the resolved URL.
- Uses `IHttpRequestExecutor` (explicit verbs).

---

## 10. Failure, redelivery and retry behavior

Only the **business logic** can decide whether repeating a call is harmful (idempotency). The framework must therefore never silently retry non-idempotent work.

| Situation | Facts | Options |
|---|---|---|
| Caller cannot reach the host | regular UJMW client behavior | unchanged |
| Processor cannot reach the host on dequeue | nothing leased (or an allocation whose response got lost) | retry with back-off (harmless); a lost allocation is never confirmed and withdrawn after 2 seconds |
| Dequeue confirmation missed | the job was guaranteed never executed | always redelivered immediately with priority (not subject to the redelivery policy, does not count as attempt) |
| Processor cannot reach the host on heartbeat | lease may expire while the work continues | processor retries until `leaseLossTimeoutSec` (60 s) elapsed, then cancels the work (it lost the lease) |
| Processor cannot reach the host on completion | work is done, result undeliverable | retry until the lease expires; afterwards the result is discarded |
| Lease expired (processor crash / network) | the work may or may not have been (partially) executed | **(a)** redeliver with priority (at-least-once) **(b)** terminate with fault "processor lost" (at-most-once) **(c)** policy delegate per contract / method deciding (attempt counter supplied) |
| Host restart | all entries lost | outer polls answer "Wrong asyncId or pollingHash"; the caller gets a fault |

**Decided (Q-RETRY):** a policy delegate in `UjmwQueueOptions` (`RedeliveryPolicy(Type contractType, string queueName, string methodName, int attempt, UjmwQueueLeaseLossReason reason) → bool`) with the **safe default (b)**: no redelivery after the loss of a **confirmed** lease; the caller receives the fault `processor lost`. The contract stays free of UJMW attributes.

Not subject to the policy: missed dequeue confirmations (5.2; always redelivered at the head of the queue, never counted) and explicit returns via `DELETE` without cancellation request (requeued by the release rule of 5.4, limited to 10 releases).

---

## 11. Security

- The regular auth hooks run for the contract actions (unchanged) and for dequeue / heartbeat / return / completion (with the reference methods of `IUjmwQueue`, see 4). Info: configurable.
- Lease protection solely by a per-allocation `pollingSecret` (`pollingSalt` / `pollingHash` as in the long-running polling authentication). The `processor` name is informational only and no security identity.
- Wrong `requestId` or `pollingHash`: answered with the fault `Wrong requestId or pollingHash` (no distinction, no information disclosure).

---

## 12. Swagger / OpenAPI and MCP

- The contract actions appear exactly as with `AddControllerFor`.
- The `_queue` actions appear in the same API group with documentation comments detailed enough to implement a processor without a UJMW library (filter syntax, lease / heartbeat rules, hash algorithm).
- MCP: the contract actions can be enrolled as usual. The `_queue` actions are **not** enrolled as tools (infrastructure).

---

## 13. Requirements

| Id | Requirement |
|---|---|
| R-01 | `AddQueueFor` with the same overloads and options as `AddControllerFor` |
| R-02 | The emitted contract controller is identical to `AddControllerFor` (routes, DTOs, Swagger, auth, long-running actions) |
| R-03 | No implementation of the contract is required in DI; `IUjmwQueuingService` is used (in-memory default auto-registered) |
| R-04 | The dynamic client is used as the JSON adapter via a custom `IHttpRequestExecutor` (`UjmwQueueBridgeExecutor`) |
| R-05 | Queue-specific code lives in separate partial files / folder; original files receive only minimal hooks (3.4) |
| R-06 | Well-known sub-route `_queue` with info, dequeue, heartbeat, return and completion |
| R-07 | Per-allocation `pollingSecret`, `pollingSalt` / `pollingHash` authentication; the `processor` name is informational and may change |
| R-07a | Auth hooks for `_queue` actions receive the queued contract type and the reference method of `IUjmwQueue` |
| R-07b | Queue key = contract type + resolved controller route (`queueName`) |
| R-08 | Heartbeat interval `3` s; lease-loss handling after `60` s without `PUT` (subject to R-14) |
| R-08a | Two-phase dequeue: an allocation must be confirmed by a first `PUT` within `dequeueConfirmationTimeoutSec` (default 2 s), otherwise it is withdrawn immediately and redelivered with priority; a late `PUT` gets `cancellationRequested: true` and the fault `Withdrawn - dequeue confirmation missed`; processors start execution only after the confirmation |
| R-08b | `dequeueNext` (method filter) on completion returns the next allocation in the dequeue response shape |
| R-09 | Callbacks flow processor → caller preserving the global `#` order, with back pressure and without silent loss |
| R-10 | Cancellation flows caller → processor; queued entries are cancelled immediately |
| R-11 | `IUjmwQueue` / `IUjmwQueuingService` separation; all methods carry the contract type |
| R-12 | `QueueProcessor<T>` with `Start`, `Next`, `Stop`, `Kill`, `CurrentTasks`, `InnerService`, method filters, `hardShutdown` |
| R-13 | `UjmwQueueClient : IUjmwQueue` with the URL / auth getter fallbacks of the dynamic client |
| R-14 | Redelivery after loss of a confirmed lease is decided by `RedeliveryPolicy`; default: no redelivery, fault `processor lost` |
| R-15 | Queue mode does not degrade regular long-running endpoints of the same host (C-1) |
| R-16 | In-memory only, short-term; explicitly no durability across restarts |
| R-17 | ASP.NET Core only, never WCF |
| R-18 | Strictly minimal-invasive implementation: changes to existing files are limited to the hooks listed in 3.4 |
| R-19 | MSTest unit tests (in `UJMW.Tests`) for the in-memory queuing service (allocation, confirmation timeout, lease expiry, priority redelivery, filters, `dequeueNext`, statistics), the bridge executor, the JSON dispatcher, `QueueProcessor<T>` and an end-to-end test over Kestrel incl. callbacks and cancellation |
| R-20 | The original inbound sidechannel is forwarded unchanged; the processor executes every job in its own task (cloned `AsyncLocal` sandbox) and restores the sidechannel there |
| R-21 | Processor method filters as `string[]` whitelist / blacklist (`nameof`), `null` = all methods |
| R-22 | Sub-services are rejected at registration in phase 1 |
| R-23 | Configurable `MaxQueueSize` (default `1000`); a full queue rejects the enqueue immediately with the fault `Queue capacity exceeded` |
| R-24 | One FIFO queue across all methods; a dequeue takes the front-most entry matching the filter |
| R-25 | Released entries are reinserted at index 0, 1, 2, 3, 5, 8, 13, 21, 34, 55 (Fibonacci without duplicate 1); the 11th release terminates the entry with the fault `Dequeued and Released 10 Times (Limit)` |
| R-26 | Processor idle polling `2` s, dequeue confirmation `2` s (both configurable) |
| R-27 | Callback buffer and outcome retention reuse the definitions / options of the long-running feature |
| R-28 | A shutdown of the queue host does not cancel running processor jobs |
| R-29 | Logging via `DevLogger`; the queue-mode implementation uses only the sub-range `72400` – `72499` of the UJMW-wide reserved range `72xxx` (the enum comment may document `72xxx` as UJMW range and `724xx` as queue-mode range); see TODO-LOG in 15 |
| R-30 | UJMW TestClient support for queue-mode controllers (`_queue` info / inspection) and a DemoService queue host plus in-process processor based on `ILongRunningDemoService` |

---

## 14. Validation findings on the original draft

| # | Finding | Recommendation |
|---|---|---|
| V-1 | Dequeue uses `GET` although it has side effects (allocates an entry). HTTP stacks, proxies and tools may cache, prefetch or automatically retry `GET` requests. | **Resolved:** `GET` is kept (plus `Cache-Control: no-store`); the two-phase dequeue confirmation (5.2) neutralizes unintended `GET` requests. |
| V-2 | The draft uses both `/queue` and `/_queue`. | Use `_queue` (reserved, see C-10). |
| V-3 | The draft names the embedded request `UJMW` in one place and `request` in another; `fault` is written as string `"null"`. | `request` (dequeue) / `response` (completion), `fault` as JSON `null`. |
| V-4 | The draft uses `salt` / `hash` / `secret` while the long-running protocol uses `pollingSalt` / `pollingHash` / `pollingSecret`. | **Resolved:** uniform names `pollingSalt` / `pollingHash` / `pollingSecret`. |
| V-5 | "Fetches `<url>/<all methods of T>/queue`" – the queue is per controller, not per method. | One `_queue` per controller; methods are selected by the `dequeue` filter. |
| V-6 | Method whitelist / blacklist were drafted without a concrete type. | **Resolved:** plain `string[]` whitelist / blacklist (`nameof`), a name covers all overloads (9.1). |
| V-7 | `Kill` returns entries via `DELETE` but the running implementations were not mentioned. | `Kill` must cancel them (9.1). |
| V-8 | Lease loss allows a former processor to still deliver results. | Per-lease secret (5.2). |

---

## 15. Questions and decisions

| Id | Question | Decision | State |
|---|---|---|---|
| Q-1 | How to resolve C-1 (outer operation store slots / threads) and C-3 (`execState` 0 vs 1)? | dedicated operation store instance per queue controller without concurrency limit; `execState` mismatch accepted in phase 1 | **decided** |
| Q-2 | Regular (non-`CancellationToken`) methods in queue mode: reject, allow with timeout or allow unrestricted? | allow with a configurable timeout (fault on expiry, entry gets cancelled) | **decided** |
missed dequeue confirmations are always redelivered; explicit `DELETE` returns follow the release rule (Q-REQUEUE, max 10) | **decided** |
| Q-VERB | Dequeue via `GET` or `POST` (V-1) | `GET` with immediate allocation + mandatory confirmation `PUT` within 2 s (5.2) | **decided** |
| Q-AUTH | Which `MethodInfo` do the auth hooks receive for `_queue` actions (C-11)? | the reference methods of `IUjmwQueue` (`GetInfo`, `Dequeue`, `Heartbeat`, `Return`, `Complete`) plus the queued contract type | **decided** |
| Q-NAMES | Naming of API and wire fields | uniform names: `AddQueueFor`, `_queue`, `pollingSalt` / `pollingHash` / `pollingSecret`, `request` / `response` | **decided** |
| Q-KEY | Queue key (C-12) | contract type + resolved controller route (`queueName`) | **decided** |
| Q-SIDE | Sidechannel forwarding (C-6) and processor-side sidechannel restore (9.2) | forward the original inbound snapshot unchanged; processor restores it via its own configuration inside an isolated job task (`AsyncLocal` sandbox) | **decided** |
| Q-SUB | Sub-services in phase 1? | no (fail fast at registration), later iteration | **decided** |
| Q-FILTER | Type of the processor method filters (V-6) | plain `string[]` whitelist / blacklist filled via `nameof`, `null` = all methods | **decided** |
| Q-RAW | Semantics of `?raw=1` on the info endpoint | discarded (was an idea for a human-friendly HTML representation) | **decided** |
| Q-GETNEXT | Round-trip reduction on completion | renamed to `dequeueNext`, carries the method filter, response in dequeue shape (5.5) | **decided** |
| Q-TESTS | Unit tests | MSTest, see R-19 | **decided** |
| Q-INVASIVE | Integration style | strictly minimal-invasive, see R-18 | **decided** |
| Q-STATS | Counters for terminated entries | `pastXX` (`past1m`, `past10m`, `past1h`, `past10h`) counting all terminated entries, no separate failed / cancelled counters | **decided** |
| Q-CAP | Behavior of a full queue | configurable `MaxQueueSize` (default `1000`), immediate fault `Queue capacity exceeded` on enqueue | **decided** |
| Q-REQUEUE | Position of released entries / poison protection | index 0, 1, 2, 3, 5, 8, 13, 21, 34, 55 (Fibonacci without duplicate 1); the 11th release terminates the entry with the fault `Dequeued and Released 10 Times (Limit)` | **decided** |
| Q-FIFO | Ordering across methods | one FIFO queue across all methods; filters take the front-most matching entry | **decided** |
| Q-TIMING | Timing defaults | idle poll 2 s, dequeue confirmation 2 s, heartbeat interval 3 s, lease loss after 60 s | **decided** |
| Q-BUF | Callback buffer towards the caller | identical to the long-running feature | **decided** |
| Q-RET | Outcome retention | identical to the long-running feature | **decided** |
| Q-PROC | Processor identity | one informational, updatable name; only `requestId` / `pollingSalt` / `pollingHash` authorize | **decided** |
| Q-ASM | Assembly placement | as proposed: host parts (`AddQueueFor`, `_queue` controller, `IUjmwQueuingService`, in-memory queue, bridge executor) in `UJMW.DynamicController`; `IUjmwQueue`, `UjmwQueueClient`, `QueueProcessor<T>`, JSON dispatcher in `UJMW.DynamicClient` | **decided** |
| Q-LOG | Logging event-kind range | UJMW as a whole reserves `72xxx`; the queue mode uses the safe sub-range `72400` – `72499` | **decided** |
| **open (later, out of scope, tracked in [doc/backlog.md](../backlog.md))** |
| Q-SHUT | Behavior on queue host shutdown | running processor jobs are **not** cancelled (C-7) | **decided** |
| Q-SCOPE | Deliverables of this Epic | see 16 | **decided** |

---

## 16. Scope of deliverables

Everything below is part of **this one Epic**. After the implementation the relevant contents are additionally transferred to the official documentation locations (UJMW skill, README / docs, TestClient skill); the Epic remains the design record.

| Deliverable | In scope | Notes |
|---|---|---|
| Host side (`AddQueueFor`, `_queue`, in-memory queuing service, bridge) | yes | phase 1 |
| Processor side (`QueueProcessor<T>`, JSON dispatcher, `UjmwQueueClient`) | yes | .NET only |
| MSTest unit and Kestrel end-to-end tests | yes | R-19 |
| UJMW TestClient support | yes | inspection of `_queue` info; contract actions work unchanged |
| DemoService (`UJMW.DemoWebService.net10.0`) | yes | queue host for `ILongRunningDemoService` plus an in-process processor demonstrating progress callbacks, cancellation, failure and release limits |
| TypeScript processor | no | possible later |
| WordPress plugin acting as queue host | no (later) | idea: a PHP implementation of the `_queue` wire protocol as a publicly reachable relay host |
| Persistent / shared queue store with partner hosts (fallback URLs) | no (later) | the shutdown behavior (Q-SHUT) is already designed for it |
