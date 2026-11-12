# UJMW Sidechannels

← [Documentation Guide](../README.md)

## 1. Abstract and motivation

Many values are relevant for *every* call but belong to no method signature: tenant, user, correlation id, culture, feature flags, tracing data. Passing them as regular arguments pollutes every contract; putting them into custom HTTP headers ties the business layer to HTTP.

The **sidechannel** is UJMW's standardized transport for such *ambient* data. It is a flat string dictionary that travels next to the regular arguments – either as the reserved wrapper property `_` or as an HTTP header – and is captured/restored by hooks on both sides. The business code never touches it directly; it typically lives in an ambient context (e.g. [SmartAmbience](https://github.com/SmartStandards/SmartAmbience)).

### 1.1 Positive differentiation

| Alternative | Drawback compared to the UJMW sidechannel |
|---|---|
| Extra method parameters | Pollutes every contract; caller must know and pass the values explicitly |
| Ad-hoc HTTP headers | Transport-specific, not visible in the JSON payload, every team invents its own names |
| Thread-static / global state on the server | Does not cross process boundaries at all |
| W3C trace context only | Covers tracing, not tenancy or business context |

---

## 2. Wire format

### 2.1 As wrapper property `_` (default UJMW channel)

```json
{
	"a": 2,
	"b": 3,
	"_": {
		"tenantId": "4711",
		"correlationId": "c0ffee"
	}
}
```

- `_` is a reserved name; it never collides with arguments.
- Values are strings (a flat dictionary).
- The same structure can be sent back in the **response** (backchannel).

### 2.2 As HTTP header

Alternatively, the dictionary is transported as JSON inside a named HTTP header (e.g. `my-ambience`). This is useful when infrastructure components (gateways, proxies) must read the context without parsing the body. The header name `_` is not allowed.

### 2.3 Directions

| Direction | Client side | Host side |
|---|---|---|
| Request (client → host) | `UjmwClientConfiguration.ConfigureRequestSidechannel` – *provide* + *capture* | `UjmwHostConfiguration.ConfigureRequestSidechannel` – *accept* + *process* |
| Response (host → client) | `UjmwClientConfiguration.ConfigureResponseBackchannel` – *accept* + *process* | `UjmwHostConfiguration.ConfigureResponseSidechannel` – *provide* + *capture* |

The vocabulary is symmetric: the **sending** side *captures* data from its ambient context and *provides* it on one or more channels; the **receiving** side *accepts* one or more channels and *processes* (restores) the data.

---

## 3. Getting started

### 3.1 Standard setup (one line per side)

Client:

```csharp
UjmwClientConfiguration.ConfigureStandardUjmwRequestSidechannel((IDictionary<string, string> target) => {
	target["tenantId"] = MyAmbience.TenantId;
});
```

Host:

```csharp
UjmwHostConfiguration.ConfigureStandardUjmwRequestSidechannel((MethodInfo calledContractMethod, IEnumerable<KeyValuePair<string, string>> data) => {
  MyAmbience.Restore(data);
});
```

The standard setup uses only the `_` property (client: `ProvideUjmwUnderlineProperty`, host: `AcceptUjmwUnderlineProperty`).

### 3.2 Explicit configuration

Client (outgoing request):

```csharp
UjmwClientConfiguration.ConfigureRequestSidechannel((Type contractType, OutgoingRequestSideChannelConfiguration cfg) => {
	cfg.ProvideUjmwUnderlineProperty();
	cfg.ProvideHttpHeader("my-ambience");
	cfg.CaptureDataVia((IDictionary<string, string> target) => {
		target["tenantId"] = MyAmbience.TenantId;
	});
});
```

Host (incoming request):

```csharp
UjmwHostConfiguration.ConfigureRequestSidechannel((Type contractType, IncommingRequestSideChannelConfiguration cfg) => {
	cfg.AcceptUjmwUnderlineProperty();
	cfg.AcceptHttpHeader("my-ambience");
	cfg.ProcessDataVia((IEnumerable<KeyValuePair<string, string>> data) => {
		MyAmbience.Restore(data);
	});
	cfg.AcceptNoChannelProvided(); // optional: tolerate callers without any sidechannel
});
```

---

## 4. Rules and semantics

| Rule | Description |
|---|---|
| Channel order | On the receiving side, the **first** accepted channel that is present in the request wins. |
| Configuration per contract | The configurator delegates receive the contract type, so different contracts can use different channels. |
| Missing vs. empty | Once channels are accepted, a request without any of them is rejected, unless `AcceptNoChannelProvided(...)` was configured. An *empty* `_: {}` is a present (but empty) channel and triggers the processing method with no entries. |
| Defaults | `AcceptNoChannelProvided(defaultsGetter)` can provide default values when no channel was sent; without a getter, the processing method is **not** triggered at all in that case. |
| No channel at all | `ProvideNoChannel()` disables sending on the client side. |
| Per endpoint | `DynamicUjmwControllerOptions.EnableRequestSidechannel` / `EnableResponseSidechannel` (both default `true`) switch the feature per controller. |
| Contextual overlay | On ASP.NET Core, `AcceptContextualArguments(...)` overlays endpoint-specific contextual arguments onto the sidechannel data (see [Contextualization](contextualization.md)). |
| Compatibility | Conventions only enrich: a receiver that does not know a key simply ignores it. |

---

## 5. Interaction with other features

| Feature | Behavior |
|---|---|
| [Long-running operations](long-running-operations.md) | The sidechannel of the initiating `POST` is snapshotted and restored before the service method runs on the background thread. `GET` / `DELETE` requests carry no sidechannel. |
| [Async queue mode](async-queue-mode.md) | The queued request JSON contains the `_` property; the processor restores it before executing the job (configurable via `QueueProcessor<T>.Dispatcher`). The processor's response sidechannel is delivered back to the caller. |
| [Test Client](testclient.md) | The request is pre-filled with `"_": {AUTO}`, replaced by the content of the SideChannel box right before sending (templates supported, `{zone}` placeholder). |
| WCF host | Supported via `UjmwServiceBehaviour` / `UjmwHostConfiguration` of `UJMW.DynamicWcfHost` with the same vocabulary. |

---

## 6. Important artifacts

| Artifact | Package | Purpose |
|---|---|---|
| `UjmwClientConfiguration.ConfigureRequestSidechannel` | DynamicClient | outgoing request setup |
| `UjmwClientConfiguration.ConfigureResponseBackchannel` | DynamicClient | incoming response setup |
| `UjmwClientConfiguration.ConfigureStandardUjmwRequestSidechannel` | DynamicClient | one-line standard setup |
| `OutgoingRequestSideChannelConfiguration` | DynamicClient | `ProvideUjmwUnderlineProperty`, `ProvideHttpHeader`, `ProvideNoChannel`, `CaptureDataVia` |
| `IncommingResponseSideChannelConfiguration` | DynamicClient | `AcceptUjmwUnderlineProperty`, `AcceptHttpHeader`, `ProcessDataVia`, `AcceptNoChannelProvided` |
| `UjmwHostConfiguration.ConfigureRequestSidechannel` | DynamicController / WcfHost / CommandLineFacade | incoming request setup |
| `UjmwHostConfiguration.ConfigureResponseSidechannel` | DynamicController / WcfHost / CommandLineFacade | outgoing response setup |
| `IncommingRequestSideChannelConfiguration` | DynamicController | `Accept*`, `AcceptContextualArguments`, `ProcessDataVia`, `AcceptNoChannelProvided` |
| `OutgoingResponseSideChannelConfiguration` | DynamicController | `Provide*`, `CaptureDataVia` |
