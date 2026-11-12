# UJMW Long-Running Operations ("Async")

← [Documentation Guide](../README.md) · Design record: [epic-long-running-operations.md](../epics/epic-long-running-operations.md) · Decision: [HTTP verbs for execution control](../decisions/decision-http-verbs-for-execution-control.md)

## 1. Abstract and motivation

A request that runs for minutes is dangerous for a web host: it hits HTTP / proxy / load-balancer timeouts, blocks resources, and cannot be aborted cleanly by the caller.

UJMW **long-running operations** decouple the transport from HTTP timeouts:

- the operation runs in the background on the host,
- the client proxy polls internally,
- the caller still experiences a **plain synchronous call**,
- the `CancellationToken` of the caller is the **only** abort channel,
- progress can be reported through `Action<...>` **callback parameters**.

> **Scope:** "Async" here means *asynchronous transport*, **not** fire-and-forget and **not** `async`/`await` contracts. Those are possible later extensions and explicitly out of scope. The feature is implemented for **ASP.NET Core hosts only** (not WCF).

### 1.1 Positive differentiation

| Alternative | Drawback compared to UJMW long-running operations |
|---|---|
| Raise HTTP timeouts | Does not help with proxies/gateways, no cancellation, blocks threads |
| Separate "start job" + "get status" contract methods | Pollutes the contract with transport concerns; every team re-invents ids, states, security |
| SignalR / WebSockets | Stateful connections, separate programming model, poor proxy compatibility |
| Callback URLs / webhooks | Caller must be reachable inbound |

The UJMW variant needs **no contract change beyond a `CancellationToken`**, is 100 % backward compatible and remains callable by any plain HTTP client.

---

## 2. Getting started

### 2.1 Contract (the convention)

```csharp
public interface ITicketService {

	int ProcessAllOpenTickets(CancellationToken cancellationToken, int ticketOwnerId, Action<int> onTicketProcessed);

}
```

- One `CancellationToken` parameter (any position) marks the method as **potentially long-running**. More than one token is not supported.
- `Action` / `Action<T1..Tn>` parameters are **callbacks**. `Func<...>` is not supported.
- Neither token nor callbacks are part of the request DTO.

### 2.2 Host

Nothing to do – `AddControllerFor<ITicketService>(...)` detects the convention and emits the additional `GET` / `DELETE` actions. Optional tuning:

```csharp
UjmwHostConfiguration.LongRunningOptions.MaxConcurrentLongRunningAsyncCalls = 50;
```

### 2.3 Client

```csharp
ITicketService svc = DynamicClientFactory.CreateInstance<ITicketService>(url);

using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMinutes(10))) {
	int count = svc.ProcessAllOpenTickets(cts.Token, 42, (int ticketId) => Console.WriteLine(ticketId));
}
```

The call blocks until the operation terminated. Callbacks are replayed on the client side in the original order. Cancelling the token sends a `DELETE` to the host.

Client tuning:

| Setting | Default | Meaning |
|---|---|---|
| `UjmwClientConfiguration.LongRunningWaitSeconds` | `10` | `wait` value of the initiating `POST` (and `DELETE`). Recommendation for UI layers: about `2`. |
| `UjmwClientConfiguration.PollSleeper` | built-in | Hook to customize waiting between polls (e.g. tests) |

---

## 3. Wire protocol

All three verbs use the **same URL** as the regular method call. This is a transport-level semantic (how a call is executed), not a business semantic – see the [decision record](../decisions/decision-http-verbs-for-execution-control.md).

| Verb | Query | Body | Meaning |
|---|---|---|---|
| `POST` | `?wait=<seconds>` (optional) | regular request DTO | initiate |
| `GET` | `?asyncId=..&pollingSalt=..&pollingHash=..` | none | poll |
| `DELETE` | `?asyncId=..&pollingSalt=..&pollingHash=..&wait=<seconds>` | none | request cancellation |

Without `wait`, the method executes synchronously as before (the token is bound to `HttpContext.RequestAborted`). Servers that do not know the protocol ignore `wait` and answer synchronously – a safe fallback.

### 3.1 Initiate

```http
POST .../ProcessAllOpenTickets?wait=2
{ "ticketOwnerId": 42 }
```

```json
{
	"onTicketProcessedInvokes": [ { "#": 1, "arg1": 1001 }, { "#": 2, "arg1": 1002 } ],
	"async": {
		"asyncId": "2019847563219457",
		"execState": 1,
		"pollNbf": "2025-01-01T12:00:05Z",
		"cancellable": true,
		"pollingSecret": "Zt8kQ2xW"
	}
}
```

If the operation completes within `wait`, the final response is returned directly; `async` then carries only the final `execState` (no `asyncId`, nothing to poll).

### 3.2 Poll / cancel

```http
GET .../ProcessAllOpenTickets?asyncId=2019847563219457&pollingSalt=a8Kd3x&pollingHash=...
```

The response shape is **identical for all three verbs**: `return` (once completed), `out`/`ref` values, `fault`, callback invocations and `async`.

### 3.3 The `async` object

| Field | Meaning |
|---|---|
| `asyncId` | Server-generated id (53-bit, Snowflake-like, transported as JSON string); not a secret |
| `execState` | see below |
| `pollNbf` | UTC "not before" time for the next poll |
| `cancellable` | whether a `DELETE` is currently meaningful |
| `pollingSecret` | only in the initiating response; used to authenticate `GET`/`DELETE` |

| `execState` | Meaning |
|---|---|
| `-2` | cancelled |
| `-1` | running, cancellation requested |
| `0` | queued (not yet started) |
| `1` | running |
| `2` | completed |
| `3` | failed (`fault` is set) |

### 3.4 Callbacks

```json
"onProgressInvokes": [ { "#": 1, "arg1": "Rendering", "arg2": 3 } ]
```

- One property `<callbackParamName>Invokes` per callback parameter.
- `#` is a sequence number unique across all callbacks of one operation; the client replays all invocations strictly in `#` order.
- `arg1..argN` are the positional arguments of the `Action<...>`.
- Invocations are drained by the poll that delivers them (single consumer).

### 3.5 Polling authentication

```text
pollingHash = Base64Url( SHA-256( UTF-8( pollingSecret + pollingSalt ) ) )   // without padding
```

- `pollingSecret`: 8 alphanumeric characters, only transmitted once (initiating response).
- `pollingSalt`: 6 fresh random characters per request.
- Wrong id or hash → `"fault": "Wrong asyncId or pollingHash"`; nothing is drained, nothing is disclosed.
- The regular UJMW auth hooks always run first (for `POST`, `GET` and `DELETE`, with the original method as target).

### 3.6 Protocol rule changes

- `async` is a reserved wrapper property (like `return`, `fault`, `_`).
- `async` may coexist with `fault`.

---

## 4. Host-side behavior and configuration

The default `IUjmwOperationStore` is an in-memory store (`UjmwInMemoryOperationStore`) driven by a hosted service. It honors application shutdown (the stopping token is linked into every operation token).

| `UjmwLongRunningOptions` | Default | Meaning |
|---|---|---|
| `MaxConcurrentLongRunningAsyncCalls` | `300` | parallel operations; more are queued (`execState 0`) |
| `CallbackBufferLimit` | `1000` | buffered, undelivered callback invocations per operation |
| `FreezeTimeBeforeCallbackBufferOverflow` | `30 s` | a callback on a full buffer blocks this long before throwing `UjmwCallbackBufferOverflowException` |
| `MinOutcomePreservTime` | `10 s` | minimum retention of a terminated outcome |
| `MaxOutcomePreservTime` | `300 s` | maximum retention of a terminated outcome |
| `OutcomeCleanupLimit` | `100` | number of retained outcomes above which cleanup (oldest first) starts |
| `ThrottlingLevel` | `1` | load-dependent poll throttling (1 = interval doubles at 100 active operations) |
| `CalculateNbf` | `null` | custom `pollNbf` calculation (`UjmwPollNbfCalculator`); default is a Fibonacci schedule plus load throttling |

Configure via `UjmwHostConfiguration.LongRunningOptions`; a custom store can be provided via `UjmwHostConfiguration.OperationStore`.

### 4.1 Callback buffer overflow

Invocations are **never silently dropped** and the operation **never hangs forever**: a callback invocation on a full buffer first blocks (freeze phase), then throws `UjmwCallbackBufferOverflowException` synchronously into the service implementation, which can decide how to react (e.g. stop reporting progress).

### 4.2 Context preservation

The sidechannel and contextual arguments of the initiating `POST` are snapshotted and restored before the service method runs in the background (see [Sidechannels](sidechannels.md), [Contextualization](contextualization.md)).

---

## 5. Consumers

| Consumer | Behavior |
|---|---|
| .NET dynamic client | `POST ?wait` → `GET` loop respecting `pollNbf` → callback replay → `DELETE` on cancellation |
| TypeScript `CreateServiceProxy` (UShell) | same principles; recommended `wait` ≈ 2 s |
| [Test Client](testclient.md) | shows every round-trip, callback invocations, a **Cancel** button and an "ignore NBF" option |
| Swagger / OpenAPI / MCP | `GET` and `DELETE` appear as separate operations on the same path with documented query parameters, so protocol-unaware consumers can implement polling from the documentation alone |

---

## 6. Limitations

- ASP.NET Core only (no WCF).
- In-memory store: a host restart loses running operations; there is no reconnect.
- Single consumer per operation; not a shared process list.
- No `Func<...>` callbacks; no `async`/`await` contracts.

---

## 7. Important artifacts

| Artifact | Package | Purpose |
|---|---|---|
| `UjmwAsyncProtocol` | shared | wire names, exec states, hash algorithm, parameter classification |
| `UjmwClientConfiguration.LongRunningWaitSeconds` / `PollSleeper` | DynamicClient | client tuning |
| `UjmwLongRunningOptions` | DynamicController | host tuning |
| `IUjmwOperationStore` / `UjmwInMemoryOperationStore` | DynamicController | operation management |
| `UjmwHostConfiguration.LongRunningOptions` / `OperationStore` | DynamicController | host configuration entry points |
| `UjmwCallbackBufferOverflowException` | DynamicController | overflow signal into the service implementation |
