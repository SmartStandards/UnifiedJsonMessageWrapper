# UJMW Contextualization

← [Documentation Guide](../README.md)

## 1. Abstract and motivation

Sidechannels transport context that the **caller** owns. Some context, however, is defined by the **endpoint** itself:

- the tenant is part of the URL (`/{tenant}/api/orders`),
- a gateway puts the user id into a dedicated header,
- a request DTO carries a property that must become ambient (e.g. `mandantId`),
- every call to a specific endpoint must run inside a scope (tenancy scope, transaction scope, logical call context).

**Contextualization** (ASP.NET Core dynamic controller only) lets you declare such *contextual arguments* per endpoint and wrap every call in a *contextualization hook* – without touching the contract.

### 1.1 Positive differentiation

| Alternative | Drawback |
|---|---|
| Add the tenant as a parameter to every contract method | pollutes contracts, duplicates routing knowledge |
| ASP.NET middleware reading route values | not aware of the UJMW call, runs outside the service invocation, no access to request DTO values |
| Hand-written controllers | loses the generated routing / Swagger / DTOs |

---

## 2. Getting started

```csharp
services.AddDynamicUjmwControllers((DynamicUjmwControllerRegistrar r) => {

	r.AddControllerFor<IOrderService>((DynamicUjmwControllerOptions o) => {

		o.ControllerRoute = "{tenant}/api/orders";
		o.BindContextualArgumentToRouteSegment("tenant");

		o.ContextualizationHook = (IDictionary<string, string> args, Action inner) => {
			using (TenancyScope.Enter(args["tenant"])) {
				inner.Invoke();
			}
		};

	});

});
```

Call: `POST https://host/acme/api/orders/GetOpenOrders` → the service method runs inside `TenancyScope("acme")`.

---

## 3. Contextual argument sources

| Method | Value source | Notes |
|---|---|---|
| `BindContextualArgument(name, Func<string> valueGetter)` | any getter, evaluated per call | e.g. machine name, configuration value |
| `BindContextualArgumentToRouteSegment(name, routeSegmentAlias = null)` | placeholder in `ControllerRoute` | alias is used for documentation (Swagger) |
| `BindContextualArgumentToHeaderValue(name, headerName)` | HTTP request header | |
| `BindContextualArgumentToRequestDto(name, dtoPropertyName = null, propTypeIfGenerating = null)` | property of the request DTO | if the contract method does not declare the property, it is **generated** into the DTO (default type `string`) |

Each name may be registered only once per endpoint (collision guard).

---

## 4. Consuming contextual arguments

There are two consumers – choose the one that fits:

### 4.1 Endpoint-individual: `ContextualizationHook`

```csharp
public delegate void ContextualizationHookDelegate(
	IDictionary<string, string> endpointContextualArguments,
	Action innerInvokeContextual
);
```

- Invoked **around** each call of this endpoint.
- Receives all contextual arguments of the endpoint.
- **Must** invoke `innerInvokeContextual` exactly once; skipping it raises an exception ("The UJMW ContextualizationHook MUST NOT skip invoking the given inner action!").

### 4.2 Global: overlay into the sidechannel processing

```csharp
UjmwHostConfiguration.ConfigureRequestSidechannel((Type contractType, IncommingRequestSideChannelConfiguration cfg) => {
	cfg.AcceptUjmwUnderlineProperty();
	cfg.AcceptContextualArguments();            // all contextual arguments
	// cfg.AcceptContextualArguments("tenant"); // or an explicit selection
	cfg.ProcessDataVia((data) => MyAmbience.Restore(data));
});
```

- The contextual arguments of the called endpoint are **overlaid** onto the sidechannel dictionary (endpoint values override caller values with the same key).
- This allows one global ambient-restore routine for both caller-provided and endpoint-derived context.
- Prefer this global approach where possible; use the hook for real scope handling.

---

## 5. Interaction with other features

| Feature | Behavior |
|---|---|
| [Sidechannels](sidechannels.md) | Overlay via `AcceptContextualArguments`; endpoint values win. |
| [Long-running operations](long-running-operations.md) | Contextual arguments and the restored sidechannel are captured with the initiating `POST`; the hook runs around the background execution. |
| [Async queue mode](async-queue-mode.md) | The host-side context is applied on the host; the processor only receives what is in the request JSON (`_` and DTO properties). Use DTO-bound arguments or the sidechannel to make context visible to processors. |
| Swagger | Route segment arguments appear as path parameters; generated DTO properties appear in the request schema. |

---

## 6. Important artifacts

| Artifact | Purpose |
|---|---|
| `DynamicUjmwControllerOptions.BindContextualArgument*` | declaration of contextual arguments |
| `DynamicUjmwControllerOptions.ContextualizationHook` | per-endpoint scope hook |
| `IncommingRequestSideChannelConfiguration.AcceptContextualArguments` | overlay into the global sidechannel processing |
| `DynamicUjmwControllerFactory.MethodRedirector` | collects arguments, invokes the hook around the service call |
