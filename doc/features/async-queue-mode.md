# UJMW Async Queue Mode

← [Documentation Guide](../README.md) · Design record and decision log: [epic-async-queue-mode.md](../epics/epic-async-queue-mode.md) · Builds on: [Long-running operations](long-running-operations.md)

## 1. Abstract and motivation

Normally a UJMW host **executes** a contract itself. The queue mode **reverses the call direction** for a contract while the caller side stays completely unchanged:

- the **caller** uses an ordinary UJMW endpoint (including the long-running protocol),
- the **host** does not execute the call, it **enqueues** it into a short-term in-memory queue,
- a **processor** **pulls** queued calls via `<route>/_queue`, executes them against its real implementation, reports heartbeats and callback invocations, and pushes back the final response.

Typical scenarios:

| Scenario | Why queue mode |
|---|---|
| Processing unit inside a protected zone / customer network | only outbound connections are allowed |
| Desktop agents, containers without ingress, dynamic addresses | the processor cannot be called |
| Simple worker scaling | start more processors against the same queue, no load balancer needed |

> **Not a message bus.** Queue entries live in memory only; a host restart loses them. There is no exactly-once guarantee and no fan-out. It is a short-term *push → pull* inversion.

### 1.1 Positive differentiation

| Alternative | Drawback compared to the UJMW queue mode |
|---|---|
| Message broker (RabbitMQ, Service Bus, ...) | additional infrastructure, separate message contracts, caller must be rewritten, no synchronous caller experience |
| Reverse tunnels / relays | inbound semantics only emulated, often rejected by security teams |
| Hand-written job table + polling | duplicates DTOs, auth, sidechannel, cancellation per contract |
| SignalR server-to-client invocation | long-lived stateful connections, poor fit for strict outbound proxies |

The queue mode reuses the **typed contract**, DTO projection, sidechannel, auth hooks, callbacks and cancellation. Neither the caller nor the processor implementation knows about the queue.

---

## 2. Getting started

### 2.1 Host

```csharp
services.AddDynamicUjmwControllers((DynamicUjmwControllerRegistrar r) => {

	r.AddControllerFor<IRegularService>("api/regular");

	r.AddQueueFor<IReportRenderer>((DynamicUjmwControllerOptions o) => {
		o.ControllerRoute = "api/reports";
	});

});
```

- `AddQueueFor<T>` has the same overloads and options as `AddControllerFor<T>`; Swagger shows the same endpoint.
- **No implementation of `T` is needed on the host.**
- If no `IUjmwQueuingService` is registered, the default `UjmwInMemoryQueuingService` is used. Register your own instance to pass `UjmwQueueOptions`:

```csharp
services.AddSingleton<IUjmwQueuingService>(new UjmwInMemoryQueuingService(new UjmwQueueOptions { MaxQueueSize = 500 }));
```

### 2.2 Remote processor

```csharp
UjmwQueueClient queue = new UjmwQueueClient((Type contractType) => "https://host/api/reports");

QueueProcessor<IReportRenderer> processor = new QueueProcessor<IReportRenderer>(
	new ReportRenderer(), queue, maxParallelTasks: 2
);

processor.Start();   // polls every 2 seconds while idle
// ...
processor.Dispose();
```

`UjmwQueueClient` appends `/_queue` to the controller URL. Without explicit getters it uses `UjmwClientConfiguration.DefaultUrlGetter` / `DefaultAuthHeaderGetter`.

### 2.3 In-process processor

The host's `IUjmwQueuingService` also implements the processor-side `IUjmwQueue`, so a processor can run inside the host (see the demo route `QueuedLongRunningDemo` in `UJMW.DemoWebService.net10.0`):

```csharp
IUjmwQueuingService queuingService = serviceProvider.GetRequiredService<IUjmwQueuingService>();

QueueProcessor<ILongRunningDemoService> processor = new QueueProcessor<ILongRunningDemoService>(
	new LongRunningDemoService(), queuingService, 2, null, null, default(CancellationToken), "QueuedLongRunningDemo"
);

//poll increasing (see 6.0) - the values shown are the defaults
processor.IncreasePollRateForSecs = 20;
processor.IncreasedPollRateMs = 500;

lifetimeEvents.ApplicationStarted.Register(() => processor.Start(0));
lifetimeEvents.ApplicationStopping.Register(() => processor.Dispose());
```

### 2.4 Caller

Unchanged – any UJMW client:

```csharp
IReportRenderer renderer = DynamicClientFactory.CreateInstance<IReportRenderer>("https://host/api/reports");
byte[] pdf = renderer.RenderReport(cancellationToken, 42, (string step) => Console.WriteLine(step));
```

---

## 3. Architecture

```text
 Caller ── POST/GET/DELETE ──► Dynamic controller (same as AddControllerFor)
																		 │ implementation instance = dynamic UJMW client proxy
																		 ▼
															 UjmwQueueBridgeExecutor
																		 ▼
															 IUjmwQueuingService  (Enqueue / Poll / RequestCancel)
																		 ▲  inherits IUjmwQueue (GetInfo / Dequeue / Heartbeat / Return / Complete)
																		 │
															 <route>/_queue  ◄── HTTP (outbound only) ── UjmwQueueClient ◄── QueueProcessor<T> ──► real implementation
```

The JSON stored in the queue is **exactly the UJMW wire format** a regular host would receive. Processors can therefore be written in any technology stack.

---

## 4. Processor protocol (`<route>/_queue`)

All responses are HTTP `200` with JSON; queue framework errors are reported via `fault`. Business faults travel inside the embedded UJMW response. Regular auth hooks run; they receive the queued contract type and the corresponding `IUjmwQueue` method (`GetInfo`, `Dequeue`, `Heartbeat`, `Return`, `Complete`) as target, so processors can be authorized differently from callers.

| Verb | Query | Body | Purpose |
|---|---|---|---|
| `GET` | – | – | statistics (`statsTotal`, `statsPerMethod`, `processors`) |
| `GET` | `processor=<name>&dequeue=<filter>` | – | allocate the next entry |
| `PUT` | `processor&requestId&pollingSalt&pollingHash` | callback invocations (optional) | heartbeat / first `PUT` = confirmation |
| `DELETE` | `processor&requestId&pollingSalt&pollingHash` | – | return the entry / confirm a requested cancellation |
| `POST` | `processor&requestId&pollingSalt&pollingHash` | `{ "response": {...}, "dequeueNext": "*" }` | deliver the final response (optionally dequeue the next job) |

`dequeue` filter: `*` (any), `m1,m2` (whitelist), `!m1,!m2` (blacklist); mixing is rejected. Method names are transport-level names.

### 4.1 Lifecycle of an entry

```text
 enqueued ──dequeue──► allocated ──first PUT (≤ 2 s)──► leased/running ──POST──► completed
													│                                 │   │
													└─ no confirmation: withdrawn,    │   └─ DELETE: requeued (Fibonacci position, max 10 releases)
														 requeued with priority         └─ no heartbeat (60 s): lease loss → RedeliveryPolicy / "processor lost"
```

- **Two-phase dequeue:** accidental `GET`s (retries, prefetching) can never swallow an entry.
- **Heartbeats** renew the lease, deliver buffered callback invocations, and return `cancellationRequested` when the caller cancelled.
- **Polling authentication** reuses the long-running algorithm: `pollingHash = Base64Url(SHA-256(UTF-8(pollingSecret + pollingSalt)))`; the `pollingSecret` is issued per allocation.
- `processor` is purely informational, never a security identity.

### 4.2 Dequeue response

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

---

## 5. Configuration

### 5.1 Host: `UjmwQueueOptions`

| Option | Default | Rule |
|---|---|---|
| `MaxQueueSize` | `1000` | full queue rejects immediately with `Queue capacity exceeded` |
| `DequeueConfirmationTimeoutSec` | `2` | first `PUT` must arrive within this time |
| `HeartbeatSec` | `3` | expected heartbeat interval (sent as `heartbeatSec`) |
| `LeaseLossTimeoutSec` | `60` | max gap between heartbeats before lease-loss handling |
| `SynchronousCallTimeoutSec` | `100` | max wait for methods **without** `CancellationToken` |
| `AllowAnonymousInfo` | `false` | anonymous access to the statistics endpoint |
| `RedeliveryPolicy` | `null` (no redelivery) | decides about redelivery after a lost lease |
| `LongRunningOptions` | `UjmwHostConfiguration.LongRunningOptions` | callback buffer and outcome retention |

### 5.2 Processor: `QueueProcessor<TService>`

| Member | Default | Meaning |
|---|---|---|
| constructor `maxParallelTasks` | `1` | concurrent jobs (each job runs in its own task = isolated `AsyncLocal` sandbox) |
| constructor `methodWhitelist` / `methodBlacklist` | `null` | C# method names (all overloads) |
| constructor `hardShutdown` | none | triggers `Kill(false)` when cancelled |
| constructor `queueName` | `null` | resolved controller route; only needed in-process when a contract is registered more than once |
| `Start(pollingIntervalSec)` | `2` | regular idle polling interval |
| `IncreasePollRateForSecs` | `20` | duration of the increased poll rate after the queue ran empty |
| `IncreasedPollRateMs` | `500` | poll interval while the increased poll rate is active |
| `Next()` | – | manual single dequeue |
| `Stop(wait)` / `Kill(wait)` / `Dispose()` | – | stop polling / abort running jobs |
| `CallbackBufferLimit` | `1000` | processor-side callback buffer per job |
| `LeaseLossTimeout` | `60 s` | job is cancelled when no heartbeat succeeded within this time |
| `Dispatcher` | – | `UjmwQueueJsonDispatcher`; configures sidechannel restore / backchannel capture |

---

## 6. Behavior details

### 6.0 Polling behavior of the processor (poll increasing)

| Situation | Behavior |
|---|---|
| entry found, slots still free | dequeue again **immediately** (burst) |
| all slots busy | **no** dequeue `GET`s at all – the heartbeat `PUT`s keep the leases alive; the poller wakes up when a slot becomes free |
| queue ran empty after activity | poll every `IncreasedPollRateMs` for `IncreasePollRateForSecs`, then back to `pollingIntervalSec` |
| job completed | the completion `POST` requests the next entry via `dequeueNext` (replaces the `GET`); the slot is handed over directly; an empty result starts the increased poll rate |

This keeps the latency low under load and the request count low while idle.

| Topic | Behavior |
|---|---|
| Synchronous methods | Calls without `CancellationToken` also work; the controller waits up to `SynchronousCallTimeoutSec` (`Queue processing timeout`). |
| Long-running methods | The caller uses `POST ?wait` / `GET` / `DELETE` as usual. While queued, the caller currently sees `execState 1` (accepted limitation). |
| Cancellation | Caller `DELETE` → `cancellationRequested` on the next heartbeat → processor cancels the token and confirms via `DELETE` or delivers the outcome. |
| Callbacks | Processor buffer → heartbeat → entry buffer → caller poll. Back pressure through the whole chain; nothing is silently lost (`Callback buffer full`). |
| Sidechannel | `_` travels inside the queued request; the processor restores it and may return a response sidechannel. |
| Host shutdown | queued and waiting callers receive `host is shutting down`; processors are not cancelled by the host. |
| Selection order | one FIFO per queue across all methods; filters skip non-matching entries. |
| Queue key | contract type + resolved controller route. |
| Sub-services | not supported (fails fast at registration and in `QueueProcessor`). |
| Logging | event-kind range `72400`–`72499` (see [backlog](../backlog.md) `TODO-LOG`). |

### 6.1 Well-known faults (`UjmwQueueProtocol`)

| Fault | When |
|---|---|
| `Queue capacity exceeded` | enqueue on a full queue |
| `Wrong requestId or pollingHash` | failed processor authentication |
| `Withdrawn - dequeue confirmation missed` | late first `PUT` |
| `Dequeued and Released 10 Times (Limit)` | 11th release |
| `processor lost` | lease lost, no redelivery |
| `Queue processing timeout` | synchronous call exceeded `SynchronousCallTimeoutSec` |
| `host is shutting down` | host stopping |
| `Callback buffer full` | heartbeat rejected due to back pressure |
| `Ambiguous queue (...)` / `Unknown queue` | in-process `queueName` resolution |

---

## 7. Important artifacts

| Artifact | Package | Purpose |
|---|---|---|
| `DynamicUjmwControllerRegistrar.AddQueueFor<T>` | DynamicController | registration |
| `IUjmwQueuingService` / `UjmwInMemoryQueuingService` | DynamicController | host-side queue |
| `UjmwQueueOptions` | DynamicController | host configuration |
| `UjmwQueueControllerBase` | DynamicController | `_queue` endpoint logic |
| `IUjmwQueue` | DynamicClient | processor-side interface |
| `UjmwQueueClient` | DynamicClient | remote `IUjmwQueue` over HTTP |
| `QueueProcessor<TService>` | DynamicClient | processor runtime |
| `UjmwQueueJsonDispatcher` | DynamicClient | JSON → method invocation on the processor |
| `UjmwQueueProtocol` | DynamicClient | wire names, faults, release positions |
| `QueueModeEndToEndTests` | Tests | HTTP end-to-end example (host + remote processor + client) |
