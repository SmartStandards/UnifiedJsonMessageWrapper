# UJMW Documentation Guide

This is the entry point into the UJMW documentation. It follows one red thread: **what UJMW is**, **how a normal call works**, and **which special features can be switched on when a scenario needs more**. Every special feature has its own detail document.

| If you want to ... | Read |
|---|---|
| understand the wire format (wrapper, `return`, `fault`, `_`) | [Protocol specification (root readme)](../readme.md) |
| get a mental model and choose the right feature | this document |
| transport ambient context (tenant, correlation, culture, ...) | [Sidechannels](features/sidechannels.md) |
| make an endpoint aware of its context (route segments, headers, scopes) | [Contextualization](features/contextualization.md) |
| run operations longer than an HTTP timeout, with cancellation and progress callbacks | [Long-running operations ("Async")](features/long-running-operations.md) |
| let a protected worker *pull* jobs instead of being called | [Async queue mode](features/async-queue-mode.md) |
| invoke and debug endpoints manually | [UJMW Test Client](features/testclient.md) |
| plug in authentication | [Authentication hook requirements](1-requirements.md) |
| understand why a design decision was made | [Decisions](decisions/) and [Epics](epics/) |

---

## 1. Abstract and motivation

UJMW (*Unified Json Message Wrapper*) is a deliberately small **contract-first RPC convention over HTTP + JSON**. A service is described by a plain .NET interface (the *contract*). Every method becomes one URL, every call is a `POST` with a JSON wrapper object containing the named arguments, and every response is a JSON wrapper containing `return`, `out`/`ref` values or a `fault`.

The design goals are:

- **The business layer never sees the transport.** No HTTP verbs as CRUD semantics, no status codes as business results, no URL patterns with record ids.
- **Contracts stay dependency-free.** Contract assemblies only use BCL types (`CancellationToken`, `Action<...>`, `DisplayNameAttribute`, ...). Everything UJMW-specific is derived by *convention*.
- **Conventions enrich, they never break.** A client or server that does not know a convention (sidechannel, `async`, callbacks, ...) still works; unknown values are ignored instead of treated as errors.
- **Out-of-the-box usability.** All advanced features have sensible defaults; nothing must be configured to get started.

### 1.1 Positive differentiation

| Alternative | What UJMW does differently |
|---|---|
| REST | No mapping of business operations to HTTP verbs or URL resource patterns; composite keys, paging and filtering are ordinary arguments |
| SOAP / WCF | Plain JSON, no envelope schema, no WSDL tooling, works with any HTTP stack |
| gRPC | No code generation and no HTTP/2 requirement; human-readable payloads; browser and proxy friendly |
| JSON-RPC | Named arguments in a flat wrapper per method URL; strongly typed .NET proxies and controllers without a generator; standardized sidechannel |
| Hand-written controllers | The dynamic controller emits routing, DTOs, Swagger metadata and auth hooks from the contract interface |

---

## 2. Getting started (the normal call)

### 2.1 Contract

```csharp
public interface ICalculator {

	int Add(int a, int b);

	bool TryDivide(int a, int b, out int result);

}
```

### 2.2 Host (ASP.NET Core)

```csharp
services.AddSingleton<ICalculator, Calculator>();

services.AddDynamicUjmwControllers((DynamicUjmwControllerRegistrar r) => {
	r.AddControllerFor<ICalculator>("api/calculator");
});
```

### 2.3 Client (.NET)

```csharp
ICalculator calculator = DynamicClientFactory.CreateInstance<ICalculator>("https://host/api/calculator");
int sum = calculator.Add(2, 3);
```

### 2.4 On the wire

```http
POST https://host/api/calculator/Add
Content-Type: application/json

{ "a": 2, "b": 3 }
```

```json
{ "return": 5 }
```

That is the whole core. Everything below is **optional** and activated either by configuration or by a contract convention.

---

## 3. The special features at a glance

| Feature | Activated by | Problem it solves | Platforms | Detail doc |
|---|---|---|---|---|
| **Sidechannels** | host/client configuration | Transport ambient data (tenant, correlation id, culture, ...) without polluting method signatures | ASP.NET Core, WCF, dynamic client, CommandLineFacade | [sidechannels.md](features/sidechannels.md) |
| **Contextualization** | `DynamicUjmwControllerOptions` per endpoint | Derive context from route segments, headers, DTO properties or getters, and wrap each call in a scope | ASP.NET Core | [contextualization.md](features/contextualization.md) |
| **Long-running operations ("Async")** | contract convention: a `CancellationToken` parameter | HTTP timeouts, cancellation, progress via `Action<...>` callbacks | ASP.NET Core host, dynamic client, Test Client | [long-running-operations.md](features/long-running-operations.md) |
| **Async queue mode** | registration via `AddQueueFor<T>` instead of `AddControllerFor<T>` | Reverse the call direction: processors behind firewalls *pull* jobs; simple worker scaling | ASP.NET Core host, dynamic client (processor) | [async-queue-mode.md](features/async-queue-mode.md) |
| **Authentication hook** | `UjmwHostConfiguration.SetAuthHeaderEvaluator(...)` | Central, contract-aware validation of the `Authorization` header | ASP.NET Core, WCF | [1-requirements.md](1-requirements.md) |
| **Test Client** | separate Windows tool | Manually invoke any endpoint based on a contract DLL | Windows | [testclient.md](features/testclient.md) |

### 3.1 How the features build on each other

```text
										┌───────────────────────────────┐
										│        Test Client (tool)      │  uses all of the below on the wire
										└───────────────────────────────┘
 ┌───────────────────────────────────────────────────────────────┐
 │ Async queue mode       (reuses long-running + sidechannel)     │
 ├───────────────────────────────────────────────────────────────┤
 │ Long-running operations (snapshots the sidechannel/context)    │
 ├───────────────────────────────────────────────────────────────┤
 │ Contextualization       (overlays/extends the sidechannel)     │
 ├───────────────────────────────────────────────────────────────┤
 │ Sidechannels            (the generic transport for context)    │
 ├───────────────────────────────────────────────────────────────┤
 │ Core protocol: wrapper, return, out/ref, fault, auth hook      │
 └───────────────────────────────────────────────────────────────┘
```

- **Sidechannels** are the foundation for *ambient* data.
- **Contextualization** is the endpoint-specific layer on top: it can feed additional values into the same sidechannel processing and wrap each call into a scope.
- **Long-running operations** preserve exactly this context: the sidechannel/context of the initiating `POST` is snapshotted and restored on the background thread.
- **Queue mode** reuses the long-running protocol on the caller side and forwards the sidechannel inside the queued request to the processor.

### 3.2 Decision guide

| Question | Recommendation |
|---|---|
| Must every call carry the tenant / user / correlation id? | Sidechannel (global), possibly SmartAmbience |
| Is the tenant part of the URL (`/{tenant}/api/...`)? | Contextualization via `BindContextualArgumentToRouteSegment` |
| Can a call take longer than ~30 seconds, or must it be cancellable? | Add a `CancellationToken` parameter → long-running |
| Should the caller see progress? | Additionally add `Action<...>` callback parameters |
| Can the processing machine not be reached by inbound HTTP? | Async queue mode |
| Do you need a durable message bus / exactly-once? | Not UJMW – use a broker; queue mode is short-term and in-memory |

---

## 4. Reserved wire names (summary)

| Name | Where | Meaning | Detail |
|---|---|---|---|
| `return` | response | return value (absent for `void`) | [readme](../readme.md) |
| `fault` | response | technical error; only `async` may coexist | [readme](../readme.md) |
| `_` | request / response | sidechannel object | [sidechannels.md](features/sidechannels.md) |
| `async` | response | state of a long-running operation | [long-running-operations.md](features/long-running-operations.md) |
| `<callback>Invokes` | response | buffered callback invocations | [long-running-operations.md](features/long-running-operations.md) |
| `wait`, `asyncId`, `pollingSalt`, `pollingHash` | query string | long-running control | [long-running-operations.md](features/long-running-operations.md) |
| `_queue` | route segment | queue infrastructure endpoint | [async-queue-mode.md](features/async-queue-mode.md) |

---

## 5. Packages

| Package | Purpose |
|---|---|
| `UJMW.DynamicClient` | Dynamic typed client proxies (`DynamicClientFactory`), long-running polling, queue processor (`QueueProcessor<T>`, `UjmwQueueClient`) |
| `UJMW.DynamicController` | ASP.NET Core dynamic controllers, long-running host infrastructure, queue host (`AddQueueFor<T>`) |
| `UJMW.DynamicWcfHost` | WCF service host factory (.NET Framework); core protocol, sidechannels and auth hook only |
| `UJMW.CommandLineFacade` | Expose a contract as command line interface |

---

## 6. Further reading

- [Protocol specification](../readme.md)
- [Changelog](changelog.md)
- [Backlog](backlog.md)
- [Decision: HTTP verbs for execution control](decisions/decision-http-verbs-for-execution-control.md)
- [Epic: Long-running operations](epics/epic-long-running-operations.md) (design rationale)
- [Epic: Async queue mode](epics/epic-async-queue-mode.md) (design rationale and decision log)

The epics are the **design records** (rationale, alternatives, decision ids). The documents under `features/` are the **user guides** for the implemented state.
