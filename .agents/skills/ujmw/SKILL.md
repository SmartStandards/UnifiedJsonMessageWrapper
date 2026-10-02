---
name: ujmw
description: Agent skill and interoperability guide for the UJMW protocol and its .NET framework. Use when generating, reviewing or integrating UJMW services and clients, or when directly invoking UJMW endpoints over HTTP.
---

# UJMW – Agent Skill & Interoperability Guide

**Version:** 1.0  
**Status:** Desired State / Normative Skill Reference  
**Primary audience:** AI agents and developers generating, reviewing, integrating, or directly invoking UJMW services  
**Maintenance model:** Incremental, Git-managed; keep structure stable after this initial V1

---

## 1. Purpose and Required Agent Capabilities

This document defines the desired-state understanding of UJMW required for an AI agent to use the protocol and its .NET framework correctly.

An agent trained from this document MUST be able to operate at three distinct usage levels.

### 1.1 Level A – Native .NET framework usage

The agent MUST be able to generate good UJMW-using .NET code, including:

- defining and consuming typed service contracts,
- registering service implementations in DI,
- exposing contracts through dynamic UJMW controllers,
- creating typed dynamic clients,
- configuring routes and names,
- using generic contracts and hierarchical sub-services,
- configuring authentication hooks,
- configuring sidechannels and SmartAmbience integration,
- configuring contextual arguments and contextualization,
- using transport customization and retry facilities,
- optionally exposing suitable services through MCP.

At this level, the agent SHOULD use the UJMW framework instead of manually reproducing its HTTP protocol.

### 1.2 Level B – UJMW implementation in another technology stack

If no native UJMW library exists for the target language or stack, the agent MUST be able to implement interoperable UJMW communication.

Depending on scope, it MAY:

1. create a reusable UJMW helper/framework for that stack, or
2. explicitly implement individual UJMW HTTP calls.

Such an implementation MUST reproduce the UJMW wire protocol rather than reinterpret it according to conventions of the target stack.

For example, an implementation in Python, Java, TypeScript, Go, Rust, or another language MUST NOT convert UJMW operations into REST CRUD semantics.

### 1.3 Level C – Direct agent invocation

If an agent has:

- a UJMW service base URL,
- the relevant service contract, e.g. a .NET interface,
- the required input values,
- and HTTP access,

the agent SHOULD be able to derive and execute the corresponding UJMW request itself without requiring a generated .NET client.

The agent MUST be able to derive:

- operation name,
- operation URL,
- request fields,
- omitted `out` fields,
- included `ref` fields,
- optional `_` sidechannel,
- authorization/header requirements,
- response `return`,
- response `ref`/`out` values,
- response `fault`.

### 1.4 Scope boundary

This is primarily a **usage and interoperability skill**, not a framework-maintainer manual.

Internal Reflection.Emit, IL, dynamic assembly, and cache implementation details are documented only when they have observable consequences for users or interoperable implementations.

---

## 2. Executive Mental Model

UJMW is a lightweight, typed-contract-oriented RPC protocol with a high-convenience .NET implementation.

Its architectural position is:

```text
Business / Application Layer
        │
        │ arbitrary typed service contracts
        ▼
┌──────────────────────────────────────┐
│                 UJMW                 │
│                                      │
│ typed, contract-oriented RPC         │
│                                      │
│ • operation / method calls           │
│ • named arguments                    │
│ • return / ref / out / fault         │
│ • optional sidechannels              │
│ • contextualization hooks            │
│ • authentication integration hooks   │
│ • generated HTTP/DTO projection      │
└──────────────────────────────────────┘
        │
        │ JSON + HTTP
        ▼
┌──────────────────────────────────────┐
│                 HTTP                 │
└──────────────────────────────────────┘
```

The defining principle is:

> **The service contract is the primary abstraction. HTTP and JSON are transport projections of that contract, not the business model.**

UJMW aims to combine:

- maximum practical wire interoperability,
- a very small protocol surface,
- strong typing where the host language supports it,
- high .NET convenience,
- business-layer independence from transport semantics.

The .NET developer experience can feel similar to local/remoting-style invocation, while the actual protocol remains explicit, simple HTTP/JSON rather than runtime-coupled object remoting.

---

## 3. Architectural Positioning

### 3.1 UJMW is RPC, not REST resource modeling

UJMW MUST be understood as RPC.

A service exposes named operations. Operations have named arguments, return values, and optionally `ref`/`out` values.

UJMW deliberately does **not** model business semantics by selecting HTTP verbs according to CRUD meaning.

Agents MUST NOT automatically translate:

- read → `GET`,
- create → `POST`,
- update → `PUT`/`PATCH`,
- delete → `DELETE`.

Doing so would introduce application semantics into the transport layer.

The popularity of REST is not by itself an architectural justification for such a mapping.

### 3.2 Pragmatic use of HTTP

UJMW does use HTTP structure where useful.

A normal UJMW service operation is addressed by URL and invoked through HTTP POST.

Thus UJMW uses HTTP for transport and call addressing while deliberately avoiding CRUD/business interpretation of HTTP verbs.

An informational endpoint may use GET; this is infrastructure behavior and does not alter the RPC model.

### 3.3 Comparison with JSON-RPC

UJMW and JSON-RPC are much closer architectural relatives than UJMW and REST.

Both model remote operation invocation.

A typical JSON-RPC call conceptually looks like:

```http
POST /rpc
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "method": "ChangeCustomer",
  "params": {
    "customerId": 123,
    "newName": "Miller"
  },
  "id": 42
}
```

The equivalent UJMW style is intentionally flatter:

```http
POST /CustomerService/ChangeCustomer
Content-Type: application/json

{
  "customerId": 123,
  "newName": "Miller"
}
```

Important differences:

- UJMW normally puts the operation name into the URL rather than a generic `method` field.
- UJMW does not require a `jsonrpc` protocol marker.
- UJMW does not require a request `id` envelope field.
- UJMW request parameters are top-level JSON properties.
- UJMW response metadata such as `return`, `fault`, and `_` is also represented directly in the response wrapper.
- The .NET implementation derives concrete request/response DTO types directly from the service contract.
- Swagger/OpenAPI can therefore expose concrete schemas for individual operations.
- .NET `ref` and `out` semantics have an explicit UJMW representation.
- sidechannel data is a first-class protocol capability.

JSON-RPC can be combined with separate schema systems, typed generators, or documentation conventions. It is therefore incorrect to claim that JSON-RPC is inherently incapable of typing.

The UJMW advantage is more specific:

> **Strong contract typing, concrete operation schemas, and useful API documentation arise naturally from the same .NET service contract while the wire protocol itself remains lightweight and easy to reproduce in non-.NET stacks.**

### 3.4 Layer boundary above UJMW

UJMW MUST remain agnostic to concrete business or repository semantics.

Higher-level frameworks may standardize particular kinds of contracts.

For example, SmartStandards FUSE-fx may define standardized repository-access contracts above UJMW. This is mentioned only to clarify the boundary: repository semantics belong above UJMW, not inside its transport/RPC core.

---

## 4. Core Contract Model

### 4.1 Contract-first

The service contract is the source of truth.

Example:

```csharp
public interface ICustomerService {

  Customer GetCustomer(long customerId);

  Customer ChangeCustomer(
    long customerId,
    string newName,
    ref int revision,
    out string changeToken
  );

}
```

An agent SHOULD begin with this contract rather than first inventing:

- REST resources,
- hand-written ASP.NET controllers,
- duplicate transport interfaces,
- manually synchronized request/response DTO families.

### 4.2 Operation name

A public contract method represents an operation.

The effective transport operation name normally corresponds to the method name, subject to UJMW's supported naming override conventions.

Agents MUST preserve explicit existing UJMW naming overrides.

### 4.3 Named arguments

Input parameters become named request-wrapper properties.

Parameter names are therefore observable protocol information.

Agents SHOULD NOT casually rename public contract parameters when wire compatibility matters.

### 4.4 Return value

A non-void return value is transported in the response property:

```text
return
```

### 4.5 `ref`

A `ref` parameter:

- appears in the request,
- appears in the response,
- is mapped back to the caller by the .NET dynamic client.

### 4.6 `out`

An `out` parameter:

- does not need an input value,
- is omitted from the request,
- appears in the response.

### 4.7 Fault

The response may contain:

```text
fault
```

A non-empty UJMW `fault` indicates an invocation failure even when HTTP transport itself succeeded.

Agents MUST distinguish transport success from invocation success.

---

## 5. Normative Wire Protocol

This section is critical for non-.NET implementations and direct agent invocation.

### 5.1 Normal operation URL

Given:

```text
base URL = https://server.example/CustomerService
operation = ChangeCustomer
```

the normal call URL is:

```text
https://server.example/CustomerService/ChangeCustomer
```

Conceptually:

```text
<base-url>/<operation-name>
```

Agents MUST avoid accidentally producing a double slash when composing URLs.

### 5.2 HTTP method

A normal operation call uses:

```http
POST
```

The HTTP verb does not encode business semantics.

### 5.3 Content type

The normal request body is JSON:

```http
Content-Type: application/json; charset=utf-8
```

### 5.4 Request object construction

For a method:

```csharp
R Foo(
  A a,
  B b,
  ref C c,
  out D d
);
```

the request object contains:

```text
a
b
c
```

and does **not** contain:

```text
d
```

If request sidechannel transport through the UJMW wrapper is enabled, the request may additionally contain:

```text
_
```

### 5.5 Response object construction

The response may contain:

```text
return   // if R is not void
c        // updated ref value
d        // out value
fault
_        // if response sidechannel is enabled
```

### 5.6 Complete `ref`/`out` example

Contract:

```csharp
public interface ICustomerService {

  Customer ChangeCustomer(
    long customerId,
    string newName,
    ref int revision,
    out string changeToken
  );

}
```

Request:

```http
POST /CustomerService/ChangeCustomer
Content-Type: application/json
Authorization: Bearer ey...

{
  "customerId": 123,
  "newName": "Miller",
  "revision": 7
}
```

Possible successful response:

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "revision": 8,
  "changeToken": "chg-9f4a",
  "return": {
    "id": 123,
    "name": "Miller"
  },
  "fault": null
}
```

A raw client MUST:

1. read `fault`,
2. treat a non-empty `fault` as invocation failure,
3. read `return` as the method result,
4. read `revision` as the new `ref` value,
5. read `changeToken` as the `out` value.

### 5.7 `void` example

Contract:

```csharp
void Ping(string message);
```

Request:

```http
POST /DemoService/Ping
Content-Type: application/json

{
  "message": "hello"
}
```

Possible response:

```json
{
  "fault": null
}
```

There is no `return` property required for a void method.

### 5.8 Complex argument example

Contract:

```csharp
SearchResult Search(SearchRequest request, int maxResults);
```

Request:

```json
{
  "request": {
    "query": "asthma",
    "includeArchived": false
  },
  "maxResults": 20
}
```

Response:

```json
{
  "return": {
    "items": [
      {
        "id": 4711,
        "title": "Example"
      }
    ]
  },
  "fault": null
}
```

Normal JSON object serialization rules apply recursively to complex DTOs.

### 5.9 Request sidechannel through `_`

If request sidechannel data is transported through the UJMW wrapper:

```http
POST /CustomerService/GetCustomer
Content-Type: application/json

{
  "_": {
    "currentTenant": "4711",
    "correlationId": "a13f"
  },
  "customerId": 123
}
```

The `_` object is infrastructure data, not a business argument.

Its conceptual type is:

```csharp
Dictionary<string, string>
```

### 5.10 Response sidechannel through `_`

Example:

```json
{
  "_": {
    "serverNode": "NODE-3",
    "correlationId": "a13f"
  },
  "return": {
    "id": 123,
    "name": "Miller"
  },
  "fault": null
}
```

### 5.11 Sidechannel through HTTP headers

A configured sidechannel may instead be transported in an HTTP header.

The header value is a JSON-serialized string dictionary.

Conceptually:

```http
my-ambient-data: {"currentTenant":"4711","correlationId":"a13f"}
```

The exact header name is configuration-defined and is therefore not part of the universal UJMW wire format.

### 5.12 Multiple sidechannel transports

A sender may be configured to provide the same captured snapshot through more than one channel, e.g.:

- `_`,
- one or more HTTP headers.

The data is captured once and can then be projected to multiple configured channels.

On receive, configured channel order can be semantically significant.

### 5.13 Authorization header

Authorization is transported through the normal HTTP header:

```http
Authorization: <configured value>
```

The exact token scheme is not dictated by UJMW itself.

### 5.14 JSON serialization behavior

The current .NET UJMW web implementation uses Newtonsoft.Json.

Relevant interoperability expectations include:

- JSON object serialization,
- ISO-style date formatting,
- camel-case property-name resolution for serialized CLR properties.

A foreign-stack implementation SHOULD match the actual JSON names exposed by the endpoint/Swagger rather than blindly assuming CLR source casing.

### 5.15 HTTP success and UJMW success

A raw client MUST evaluate two levels:

```text
HTTP transport result
        ↓
UJMW response / fault result
```

Normal 2xx HTTP status indicates successful transport handling.

It does not guarantee that the invoked operation succeeded.

After a successful HTTP response, the client MUST inspect `fault`.

### 5.16 Non-2xx behavior

Non-success HTTP responses are transport/protocol failures and MUST NOT be interpreted as normal UJMW return wrappers.

Authentication-related statuses such as 401 and 403 may be produced by the host authentication layer.

### 5.17 Legacy XML-encapsulated reply compatibility

The current .NET client contains compatibility handling for responses shaped like:

```xml
<UJMW> ...json... </UJMW>
```

This is legacy compatibility behavior.

It is **not** the desired modern UJMW wire format and SHOULD NOT be generated by new implementations.

The desired wire format is direct JSON.

---

## 6. Deterministic Contract-to-Wire Algorithm

An agent implementing or directly executing a UJMW call MUST be able to follow this algorithm.

### 6.1 Input

Required information:

- service base URL,
- target contract,
- target method,
- method argument values,
- optional authorization value,
- optional sidechannel configuration/data.

### 6.2 Select operation

Find the target method on the contract, including inherited interfaces where applicable.

Determine its effective UJMW operation name.

### 6.3 Build URL

Construct:

```text
<base-url>/<operation-name>
```

Normalize the separator so exactly one `/` joins both components.

### 6.4 Build request fields

For every method parameter:

- ordinary input parameter → include,
- `ref` parameter → include current value,
- `out` parameter → omit.

Use the parameter's transport name as the JSON property name.

### 6.5 Add wrapper sidechannel

If `_` request-sidechannel transport is required:

```json
"_": {
  "key": "value"
}
```

Add it at the same JSON object level as method arguments.

### 6.6 Add HTTP sidechannel headers

For every configured HTTP-header sidechannel:

1. serialize the string dictionary as JSON,
2. place that JSON string into the configured header.

### 6.7 Add authorization

If required, add:

```http
Authorization: ...
```

### 6.8 Send

Send:

```http
POST
Content-Type: application/json
```

with the constructed request object.

### 6.9 Evaluate HTTP response

If the HTTP status is not successful, handle it as transport/auth failure.

Do not parse it as a successful UJMW operation result merely because the body happens to contain JSON.

### 6.10 Parse UJMW response

For successful transport:

1. parse the JSON object,
2. inspect `fault`,
3. if `fault` is non-empty, surface an invocation fault,
4. if method return type is non-void, read `return`,
5. read each `ref` field,
6. read each `out` field,
7. process `_` or configured response headers if required.

### 6.11 Pseudocode

```text
method = resolve(contract, requestedOperation)

url = join(baseUrl, effectiveOperationName(method))

request = {}

for parameter in method.parameters:
    if parameter is out-only:
        continue

    request[transportName(parameter)] = suppliedValue(parameter)

if underlineSidechannelEnabled:
    request["_"] = capturedSidechannel

headers["Content-Type"] = "application/json"

if authorization exists:
    headers["Authorization"] = authorization

for configuredHeaderSidechannel:
    headers[name] = jsonSerialize(capturedSidechannel)

httpResponse = POST(url, headers, jsonSerialize(request))

if httpResponse.status is not 2xx:
    fail as transport/auth error

response = jsonDeserialize(httpResponse.body)

if response["fault"] is non-empty:
    fail as UJMW invocation fault

result = response["return"] if method is non-void

for each ref parameter:
    update caller value from response[propertyName]

for each out parameter:
    set caller value from response[propertyName]

process response sidechannel if configured

return result
```

---

## 7. Implementing UJMW in a Non-.NET Stack

### 7.1 Two valid strategies

An agent may implement:

**A. a reusable UJMW helper**

Useful when several services/operations will be consumed.

or:

**B. explicit per-call HTTP**

Useful for a small integration or direct agent call.

Both MUST obey Section 5 and Section 6.

### 7.2 Minimal helper responsibilities

A reusable foreign-stack helper SHOULD provide:

- base URL handling,
- operation URL composition,
- JSON request wrapper construction,
- authorization header support,
- `_` sidechannel support,
- configured header-sidechannel support,
- HTTP POST,
- HTTP status evaluation,
- `fault` evaluation,
- `return` extraction,
- `ref`/`out` extraction.

### 7.3 What a foreign helper MUST NOT invent

It MUST NOT introduce incompatible mandatory fields such as:

```json
{
  "jsonrpc": "2.0",
  "method": "...",
  "id": "..."
}
```

It MUST NOT move method arguments under a `params` object unless a specific compatibility adapter explicitly requires that different protocol.

It MUST NOT infer HTTP verbs from operation meaning.

### 7.4 Use Swagger/OpenAPI when available

For a non-.NET client, Swagger/OpenAPI generated by the UJMW endpoint is a valuable source of:

- operation URLs,
- concrete request schemas,
- concrete response schemas,
- JSON property names,
- nested DTO structures,
- documentation.

Agents SHOULD prefer actual endpoint schema information over guessing serialization details.

### 7.5 Type mapping

Foreign stacks SHOULD map JSON types naturally while respecting the endpoint schema.

Special care is required for:

- 64-bit integers,
- decimal values,
- date/time representations,
- nullable values,
- enums,
- nested objects,
- arrays/collections.

The service's Swagger/OpenAPI schema or known DTO definitions SHOULD be treated as authoritative when available.

---

## 8. Native .NET Client Usage

### 8.1 Dynamic client factory

The preferred .NET experience is:

```csharp
IMyService client =
  DynamicClientFactory.CreateInstance<IMyService>(...);
```

The resulting object implements the actual service contract.

Agents SHOULD prefer this over manually writing HTTP/JSON code in .NET unless raw protocol access is explicitly required.

### 8.2 Explicit URL

```csharp
IMyService client =
  DynamicClientFactory.CreateInstance<IMyService>(
    "https://server.example/MyService"
  );
```

### 8.3 Explicit authorization header

```csharp
IMyService client =
  DynamicClientFactory.CreateInstance<IMyService>(
    "https://server.example/MyService",
    "Bearer ..."
  );
```

### 8.4 Default URL getter

```csharp
UjmwClientConfiguration.DefaultUrlGetter =
  contractType => ResolveUrl(contractType);
```

Then:

```csharp
IMyService client =
  DynamicClientFactory.CreateInstance<IMyService>();
```

### 8.5 Default auth-header getter

```csharp
UjmwClientConfiguration.DefaultAuthHeaderGetter =
  contractType => ResolveAuthorizationHeader(contractType);
```

The getter result may be cached according to UJMW client configuration.

### 8.6 HttpClient factory

UJMW allows centralized `HttpClient` creation.

This can be used for:

- timeout configuration,
- handler configuration,
- proxy behavior,
- environment-specific transport settings.

### 8.7 Customizing flags

Client creation may pass transport-customization flags.

These flags are intentionally opaque to UJMW itself and can be interpreted by the configured HTTP client factory.

Agents SHOULD use this extension point rather than adding application-specific transport branches to UJMW core behavior.

---

## 9. Transport Abstraction and Alternative Invokers

### 9.1 `IAbstractCallInvoker`

The dynamic client is fundamentally backed by an invocation abstraction:

```csharp
public interface IAbstractCallInvoker {

  object InvokeCall(
    string uniqueMethodNameOnTransportLayer,
    MethodInfo method,
    object[] arguments,
    string[] argumentNames,
    string methodSignatureString
  );

}
```

The generated proxy maps the typed contract call into this abstraction.

### 9.2 Standard HTTP layering

Conceptually:

```text
typed contract proxy
  → IAbstractCallInvoker
    → UJMW web call invoker
      → IHttpPostExecutor
        → HttpClient
```

### 9.3 Alternative invocation mechanisms

HTTP is not a fundamental requirement of `DynamicClientFactory`.

A different `IAbstractCallInvoker` can back the same typed contract.

A demonstrated use case is command-line invocation.

Therefore:

> **Dynamic UJMW client does not mean HTTP client; it means typed contract proxy backed by an invocation strategy.**

### 9.4 Bridging

A service contract backed by a non-HTTP invoker can be registered in DI and then exposed again through a dynamic UJMW HTTP controller.

This permits clean invocation bridging without changing the business-facing contract.

---

## 10. Native ASP.NET Core Server Usage

### 10.1 Desired baseline

The normal server-side pattern is:

1. register implementation in DI,
2. expose the contract through UJMW,
3. add optional capabilities only when needed.

Example:

```csharp
services.AddSingleton<ICustomerService, CustomerService>();

services.AddDynamicUjmwControllers(
  registrar => {
    registrar.AddControllerFor<ICustomerService>();
  }
);
```

### 10.2 DI and exposure are separate

This:

```csharp
services.AddSingleton<IMyService, MyService>();
```

does not by itself expose an endpoint.

This:

```csharp
registrar.AddControllerFor<IMyService>();
```

does.

Agents MUST preserve this separation.

### 10.3 Configuration styles

Simple:

```csharp
registrar.AddControllerFor<IMyService>();
```

Configurator:

```csharp
registrar.AddControllerFor<IMyService>(
  options => {
    options.ApiGroupName = "My API";
    options.ControllerRoute = "services/my-service";
  }
);
```

Explicit options object:

```csharp
registrar.AddControllerFor<IMyService>(
  new DynamicUjmwControllerOptions {
    ApiGroupName = "My API",
    ControllerRoute = "services/my-service"
  }
);
```

Prefer the simplest form that expresses the requirement.

---

## 11. Routing, Naming, Aggregation, and Generic Contracts

### 11.1 Route templates

Routes may combine:

- controller placeholders,
- generic type placeholders,
- normal ASP.NET Core route parameters.

Example:

```csharp
options.ControllerRoute = "{tenant}/v1/[Controller].svc";
```

### 11.2 Multiple contracts on one route

UJMW supports endpoint aggregation:

```csharp
registrar.AddControllerFor<IPrimaryService>(
  options => options.ControllerRoute = "aggregated.svc"
);

registrar.AddControllerFor<IFileService>(
  options => options.ControllerRoute = "aggregated.svc"
);
```

Agents MUST NOT assume one route equals one contract.

### 11.3 One contract on multiple routes

The same contract may be registered multiple times with different options.

Agents MUST NOT assume one contract equals one endpoint.

### 11.4 Generic contracts

Closed generic contracts can be exposed independently.

Example:

```csharp
DynamicUjmwControllerOptions options =
  new DynamicUjmwControllerOptions {
    ControllerRoute = "Repo/{0}",
    ControllerTitle = "Gen ({0})",
    ControllerNamePattern = "{0}Repository",
    ApiGroupName = "GenericRepo"
  };

registrar.AddControllerFor<IGenericInterface<Foo, int>>(options);
registrar.AddControllerFor<IGenericInterface<Bar, string>>(options);
```

Naming/route patterns SHOULD be used to avoid generated-name collisions.

---

## 12. Hierarchical Contracts and Sub-Services

### 12.1 Concept

A service contract may expose child services through suitable complex read-only properties.

Example:

```csharp
public interface IRootService {

  IUserService Users { get; }

  IOrderService Orders { get; }

}
```

### 12.2 Client projection

The dynamic client can create child proxies on demand and extend the internal sub-client path.

### 12.3 Server projection

The dynamic controller registrar can recursively create child controllers/routes while resolving the actual child instance by navigating from the injected root service.

### 12.4 Agent rule

Agents SHOULD preserve natural hierarchical service structures.

They SHOULD NOT flatten them merely to imitate conventional REST controller organization.

---

## 13. Sidechannels

### 13.1 Purpose

Sidechannels transport infrastructure or ambient data independently of business parameters.

Examples:

- tenant context,
- correlation context,
- tracing values,
- ambient transaction handles,
- other cross-cutting call context.

### 13.2 Request and response are independent

UJMW separately supports:

- outgoing request sidechannel,
- incoming request sidechannel,
- outgoing response/backchannel,
- incoming response/backchannel.

Agents MUST reason about direction explicitly.

### 13.3 `_` channel

The reserved `_` wrapper property can carry a string dictionary.

### 13.4 HTTP-header channel

A configured HTTP header can carry the same kind of data as JSON-serialized dictionary content.

### 13.5 Multiple channels

Multiple channels may be provided.

On receive, order may matter because the first channel containing usable data can win.

### 13.6 Optional absence

"No channel provided" and "an empty channel was provided" are distinct states.

Configuration can explicitly allow missing sidechannel data and optionally supply defaults.

Agents MUST preserve this semantic distinction.

---

## 14. SmartAmbience Integration

### 14.1 Relationship

SmartAmbience is a compatible SmartStandards framework for ambient dataflow.

UJMW provides transport channels.

SmartAmbience provides the ambient-state contract and capture/restore semantics.

They are separate frameworks designed to compose cleanly.

### 14.2 Typical flow

```text
caller ambient state
  → AmbienceHub.CaptureCurrentValuesTo
    → UJMW sidechannel
      → HTTP/JSON
        → UJMW sidechannel restore
          → AmbienceHub.RestoreValuesFrom
            → business invocation
```

### 14.3 Example configuration

```csharp
AmbienceHub.DefineFlowingContract(
  "tenant-identifiers",
  contract => {
    contract.IncludeExposedAmbientFieldInstances("currentTenant");
    contract.IncludeExposedAmbientFieldInstances("dtHandle");
  }
);

UjmwClientConfiguration.ConfigureRequestSidechannel(
  (serviceType, sideChannel) => {

    if (HasDataFlowSideChannelAttribute.TryReadFrom(
      serviceType,
      out string contractName
    )) {

      sideChannel.ProvideUjmwUnderlineProperty();

      sideChannel.CaptureDataVia(
        snapshot =>
          AmbienceHub.CaptureCurrentValuesTo(
            snapshot,
            contractName
          )
      );
    }
    else {
      sideChannel.ProvideNoChannel();
    }

  }
);
```

Server:

```csharp
UjmwHostConfiguration.ConfigureRequestSidechannel(
  (serviceType, sideChannel) => {

    if (HasDataFlowSideChannelAttribute.TryReadFrom(
      serviceType,
      out string contractName
    )) {

      sideChannel.AcceptHttpHeader("my-ambient-data");
      sideChannel.AcceptUjmwUnderlineProperty();
      sideChannel.AcceptContextualArguments();

      sideChannel.ProcessDataVia(
        incomingData =>
          AmbienceHub.RestoreValuesFrom(
            incomingData,
            contractName
          )
      );
    }
    else {
      sideChannel.AcceptNoChannelProvided();
    }

  }
);
```

### 14.4 Agent rule

When a project already uses SmartAmbience, agents SHOULD prefer its flowing contracts and UJMW sidechannels for ambient cross-boundary state.

They MUST NOT pollute every business method with infrastructure parameters merely to carry that state.

---

## 15. Contextual Arguments and Contextualization

### 15.1 Purpose

Contextual arguments are endpoint-derived values and are distinct from generic sidechannel transport.

They can originate from:

- a getter,
- a route segment,
- a request DTO property,
- an HTTP header.

### 15.2 Getter

```csharp
options.BindContextualArgument(
  "skyfall",
  () => "007"
);
```

### 15.3 Route

```csharp
options.ControllerRoute = "{tnt}/v1/[Controller].svc";

options.BindContextualArgumentToRouteSegment(
  "MandantAusRoute",
  "tnt"
);
```

The route placeholder MUST exist.

### 15.4 Header

```csharp
options.BindContextualArgumentToHeaderValue(
  "huhu",
  "hu-hu"
);
```

### 15.5 Request DTO / shadow property

```csharp
options.BindContextualArgumentToRequestDto(
  "dtHandle",
  propTypeIfGenerating: typeof(int)
);
```

If the business method does not declare that property, UJMW can generate it on the request wrapper.

This intentionally allows endpoint/infrastructure context without changing the business method signature.

### 15.6 Around hook

```csharp
options.ContextualizationHook =
  (endpointContextualArguments, innerInvokeContextual) => {

    // ENTER CONTEXT

    innerInvokeContextual.Invoke();

    // LEAVE CONTEXT
  };
```

The inner invocation MUST be executed.

### 15.7 Overlay into sidechannel

Configured contextual arguments can be overlaid into incoming sidechannel data before ambient restoration.

This allows values derived from HTTP route/header/DTO context to participate in the ambient context seen by the business invocation.

---

## 16. Authentication and AuthTokenHandling

### 16.1 UJMW auth evaluator

UJMW can centrally evaluate the HTTP Authorization header with knowledge of:

- contract type,
- target contract method,
- remote caller information.

Example shape:

```csharp
UjmwHostConfiguration.AuthHeaderEvaluator =
  (
    string rawAuthHeader,
    Type contractType,
    MethodInfo targetContractMethod,
    string callingMachine,
    ref int httpReturnCode,
    ref string failedReason
  ) => {

    // validate

    return true;
  };
```

### 16.2 Endpoint opt-out

```csharp
options.EnableAuthHeaderEvaluatorHook = false;
```

This is a deliberate exception and SHOULD NOT be applied casually.

### 16.3 AuthTokenHandling relationship

AuthTokenHandling is a compatible SmartStandards framework.

Desired separation:

```text
UJMW
  → obtains/routes HTTP authorization information

AuthTokenHandling
  → validates/interprets token semantics
```

When the project already uses AuthTokenHandling, agents SHOULD integrate with it rather than implementing ad-hoc token parsing in individual service methods.

### 16.4 Business-layer separation

Authorization tokens SHOULD NOT become ordinary business method parameters.

---

## 17. Argument Pre-Evaluation

UJMW provides a global pre-invocation hook:

```csharp
UjmwHostConfiguration.ArgumentPreEvaluator =
  (
    Type contractType,
    MethodInfo calledContractMethod,
    object[] arguments
  ) => {

    // cross-cutting inspection / validation

  };
```

This is suitable for cross-cutting concerns.

Business-specific validation remains business-layer responsibility.

---

## 18. Documentation, Swagger, and OpenAPI

### 18.1 Concrete generated DTOs

The ASP.NET Core projection generates concrete request and response DTO types for operations.

This gives Swagger/OpenAPI concrete schemas instead of merely documenting an opaque generic RPC payload.

### 18.2 Contract information available to documentation

Generated API metadata can include:

- operation name,
- request properties,
- parameter types,
- response properties,
- return type,
- `ref`/`out` fields,
- optional `_`,
- method documentation,
- parameter documentation,
- return documentation.

### 18.3 XML documentation

UJMW can propagate XML documentation from contract methods and parameters into Swagger metadata.

Agents SHOULD preserve useful XML documentation on public contracts.

### 18.4 Interoperability value

For foreign-stack implementations and direct agent calls, Swagger/OpenAPI SHOULD be used when available to confirm:

- exact route,
- exact JSON property names,
- DTO structure,
- nullable/required shape,
- primitive and nested types.

---

## 19. API Grouping, Endpoint Information, and Version Awareness

### 19.1 API grouping

```csharp
options.ApiGroupName = "My API";
```

can group generated endpoints.

An assembly-based fallback can be enabled.

### 19.2 Endpoint information

UJMW endpoint information can include:

- endpoint qualifying name,
- contract identity,
- assembly version,
- known methods,
- API group metadata.

### 19.3 Version checking

Dynamic clients can use endpoint information to evaluate version compatibility.

Agents SHOULD preserve contract assembly version semantics where the solution uses this feature.

---

## 20. Retry and Failure Handling

### 20.1 Retry decision

UJMW client retry configuration can consider:

- contract type,
- exception,
- attempt number,
- HTTP code,
- URL by reference.

The URL may be changed for a later attempt, enabling fallback endpoints.

### 20.2 Safety

Retry MUST be applied carefully because RPC operations may have side effects.

Agents MUST NOT assume every operation is idempotent.

### 20.3 Processing boundary

Transport failures may be retryable.

Errors that occur after a successful response has already been obtained and while processing that response SHOULD NOT automatically cause the operation to be transmitted again.

### 20.4 Retry limit

UJMW has an internal hard upper limit on retries.

Agents SHOULD NOT wrap UJMW in uncontrolled additional retry loops.

---

## 21. MCP Exposure

### 21.1 Optional capability

A dynamic UJMW controller may opt into MCP exposure:

```csharp
registrar.AddControllerFor<IWikiAccess>(
  options => {
    options.EnableMcp = true;
  }
);
```

### 21.2 Projection model

Conceptually:

```text
typed service contract
  ├─ UJMW HTTP endpoint
  ├─ Swagger/OpenAPI
  └─ optional MCP tools
```

MCP does not replace the UJMW contract.

Agents SHOULD enable it only where tool exposure is actually desired.

---

## 22. Self-Announcement and Optional Infrastructure

UJMW can optionally expose an announcement trigger endpoint:

```csharp
registrar.AddAnnouncementTriggerEndpoint();
```

This belongs to optional discovery/infrastructure behavior and SHOULD NOT be copied into minimal configurations without a corresponding system requirement.

---

## 23. Desired Agent Coding Rules

### 23.1 MUST

An agent MUST:

- treat UJMW as typed RPC,
- treat the service contract as the primary abstraction,
- understand the raw UJMW wire format,
- be able to derive a call from a contract,
- keep UJMW free of concrete business semantics,
- preserve named parameter semantics,
- distinguish ordinary input, `ref`, and `out`,
- distinguish `return`, `fault`, and `_`,
- distinguish HTTP failure from UJMW invocation fault,
- keep DI registration separate from endpoint exposure,
- preserve configured sidechannel ordering,
- preserve deliberate contextualization behavior,
- use SmartAmbience/AuthTokenHandling integration when the project architecture calls for it,
- reproduce UJMW exactly when implementing it in another stack.

### 23.2 SHOULD

An agent SHOULD:

- use the native UJMW .NET framework in .NET code,
- use `AddControllerFor<T>()` as the server baseline,
- use `DynamicClientFactory.CreateInstance<T>()` as the .NET client baseline,
- use Swagger/OpenAPI to drive foreign-stack interoperability when available,
- keep ambient infrastructure values out of business signatures,
- use sidechannels for ambient cross-boundary state,
- use contextual arguments for endpoint-derived context,
- preserve XML documentation,
- preserve natural hierarchical service contracts,
- add optional features only when actually required.

### 23.3 MUST NOT

An agent MUST NOT:

- redesign UJMW as REST because REST is conventional,
- select HTTP verbs from CRUD semantics,
- add JSON-RPC envelope fields to UJMW,
- put UJMW arguments under a mandatory `params` object,
- use `_` as a normal business property,
- treat `fault` as an HTTP-status replacement or vice versa,
- assume one contract maps to one route,
- assume one route maps to one contract,
- assume dynamic UJMW clients necessarily use HTTP,
- add ambient/auth parameters to business methods when infrastructure mechanisms are appropriate,
- manually duplicate generated controllers/DTOs without a concrete requirement.

---

## 24. Canonical Examples

### 24.1 Minimal .NET server

```csharp
public interface ICustomerService {

  Customer GetCustomer(long customerId);

}

public sealed class CustomerService : ICustomerService {

  public Customer GetCustomer(long customerId) {
    return LoadCustomer(customerId);
  }

}
```

Registration:

```csharp
services.AddSingleton<ICustomerService, CustomerService>();

services.AddDynamicUjmwControllers(
  registrar => {
    registrar.AddControllerFor<ICustomerService>();
  }
);
```

### 24.2 Minimal .NET client

```csharp
ICustomerService client =
  DynamicClientFactory.CreateInstance<ICustomerService>(
    "https://server.example/CustomerService"
  );

Customer customer =
  client.GetCustomer(4711);
```

### 24.3 Same call without the .NET library

Contract:

```csharp
Customer GetCustomer(long customerId);
```

HTTP:

```http
POST https://server.example/CustomerService/GetCustomer
Content-Type: application/json

{
  "customerId": 4711
}
```

Response:

```json
{
  "return": {
    "id": 4711,
    "name": "Miller"
  },
  "fault": null
}
```

### 24.4 Direct-agent reasoning example

Given:

```csharp
Invoice CreateInvoice(
  long customerId,
  decimal amount,
  ref int revision,
  out string invoiceNumber
);
```

and:

```text
Base URL:
https://api.example/BillingService

customerId = 42
amount = 199.95
revision = 3
```

the agent derives:

```http
POST https://api.example/BillingService/CreateInvoice
Content-Type: application/json

{
  "customerId": 42,
  "amount": 199.95,
  "revision": 3
}
```

It MUST NOT send `invoiceNumber`, because it is `out`.

Possible response:

```json
{
  "revision": 4,
  "invoiceNumber": "INV-2026-00123",
  "return": {
    "id": 8123
  },
  "fault": null
}
```

The resulting logical call result is:

```text
return          = Invoice { id = 8123 }
revision        = 4
invoiceNumber   = "INV-2026-00123"
```

### 24.5 Direct-agent sidechannel example

If the endpoint requires ambient data through `_`:

```http
POST https://api.example/BillingService/CreateInvoice
Content-Type: application/json
Authorization: Bearer ey...

{
  "_": {
    "currentTenant": "tenant-17",
    "dtHandle": "4815162342"
  },
  "customerId": 42,
  "amount": 199.95,
  "revision": 3
}
```

The agent MUST understand that `currentTenant` and `dtHandle` are not method arguments.

---

## 25. Advanced ASP.NET Registration Example

The following is intentionally a capability showcase, **not** a minimal template:

```csharp
services.AddSingleton<IDemoService>(demoService);
services.AddSingleton<IDemoFileService>(demoService);

UjmwHostConfiguration.EnableApiGroupNameFallback = true;

services.AddDynamicUjmwControllers(
  registrar => {

    registrar.AddControllerFor<IContextualizationDemo>(
      options => {
        options.ApiGroupName = "Contextualization-Demo";

        options.BindContextualArgumentToRequestDto(
          "dtHandle",
          propTypeIfGenerating: typeof(int)
        );

        options.ContextualizationHook =
          (endpointContextualArguments, innerInvokeContextual) => {

            // ENTER THE CONTEXT

            innerInvokeContextual.Invoke();

            // LEAVE THE CONTEXT
          };
      }
    );

    registrar.AddControllerFor<IDemoFileService>(
      options => {
        options.ApiGroupName = "Aggregation-Demo";
        options.ControllerRoute = "aggregated.svc";
      }
    );

    registrar.AddControllerFor<IDemoService>(
      options => {
        options.ApiGroupName = "Aggregation-Demo";
        options.ControllerRoute = "aggregated.svc";
      }
    );

    registrar.AddControllerFor<IDemoService>(
      options => {
        options.ApiGroupName = "Contextualization-Demo";
        options.ControllerRoute = "{tnt}/v1/[Controller].svc";

        options.BindContextualArgumentToRouteSegment(
          "MandantAusRoute",
          "tnt"
        );

        options.BindContextualArgument(
          "skyfall",
          () => "007"
        );

        options.BindContextualArgumentToHeaderValue(
          "huhu",
          "hu-hu"
        );

        options.ContextualizationHook =
          (endpointContextualArguments, innerInvokeContextual) => {

            // ENTER THE CONTEXT

            innerInvokeContextual.Invoke();

            // LEAVE THE CONTEXT
          };
      }
    );

    registrar.AddControllerFor<IWikiAccess>(
      options => {
        options.ApiGroupName = nameof(WikiAccessService);
        options.EnableMcp = true;
      }
    );

    registrar.AddAnnouncementTriggerEndpoint();
  }
);
```

An agent MUST decompose this example into independent optional capabilities rather than copying the entire setup by default.

---

## 26. Common Failure Modes

### 26.1 REST reflex

Wrong:

> "The method reads data, therefore expose it as GET."

Correct:

> "The UJMW contract defines an operation. Invoke it as a UJMW call."

### 26.2 JSON-RPC reflex

Wrong:

```json
{
  "jsonrpc": "2.0",
  "method": "GetCustomer",
  "params": {
    "customerId": 42
  }
}
```

Correct UJMW:

```json
{
  "customerId": 42
}
```

sent to:

```text
<CustomerServiceBaseUrl>/GetCustomer
```

### 26.3 Sending `out` values

Wrong:

```json
{
  "customerId": 42,
  "generatedToken": null
}
```

when `generatedToken` is an `out` parameter.

Correct: omit it from the request.

### 26.4 Forgetting `ref` response values

A raw client that reads only `return` is incomplete when the method contains `ref` or `out`.

### 26.5 Ignoring `fault`

A 200 response with non-empty `fault` is not a successful invocation.

### 26.6 Duplicating transport DTOs in .NET

Do not create parallel request/response classes when UJMW already generates the required wrapper and no external compatibility requirement exists.

### 26.7 Polluting business signatures

Do not add tenant, tracing, auth-token, or ambient transaction parameters to every business operation when sidechannel/context/auth infrastructure is the correct layer.

### 26.8 Treating advanced demo setup as baseline

Sidechannels, contextualization, MCP, announcement endpoints, custom routing, and aggregation are capabilities, not mandatory boilerplate.

---

## 27. Migration and Adoption Guidance

This section is intentionally secondary to the desired state.

### 27.1 Hand-written .NET HTTP client → UJMW client

1. identify the actual service contract,
2. use or define the typed interface,
3. create a dynamic UJMW client,
4. move URL/auth/transport concerns into UJMW configuration,
5. remove duplicated wrapper mapping where UJMW already provides it.

### 27.2 Hand-written forwarding controller → dynamic UJMW controller

1. register the service implementation in DI,
2. expose its contract using `AddDynamicUjmwControllers`,
3. transfer meaningful route/auth/context/documentation behavior into UJMW options/hooks,
4. remove forwarding boilerplate.

### 27.3 Existing external REST API

Do not mechanically break an established public REST contract.

A compatibility REST surface and an internal typed UJMW contract may coexist.

### 27.4 Foreign integration

When another stack needs to consume UJMW:

1. obtain contract and/or Swagger,
2. implement the normative wire format,
3. start with explicit HTTP for small integrations,
4. create a reusable helper when repeated usage justifies it,
5. do not redesign the protocol to fit the target stack's preferred API style.

---

## 28. Scope Boundaries

This document does not attempt to fully teach:

- Reflection.Emit implementation,
- emitted IL opcodes,
- dynamic assembly internals,
- internal type-cache mechanics,
- complete SmartAmbience implementation,
- complete AuthTokenHandling implementation,
- complete MCP implementation,
- complete Self-Announcement implementation,
- FUSE-fx beyond architectural boundary clarification.

It **does** document every such feature to the degree necessary to generate correct UJMW-using or UJMW-interoperable code.

---

## 29. Source-Derived Invariants

The reviewed implementation establishes the following consumer-relevant invariants:

- dynamic .NET clients implement the actual contract,
- generated clients also implement `IUjmwClient`,
- typed calls delegate through `IAbstractCallInvoker`,
- HTTP execution is separately abstracted through `IHttpPostExecutor`,
- a null request content is used by the HTTP executor to select GET for infrastructure/info retrieval,
- normal operation calls serialize a JSON request and address the operation by URL,
- request wrappers contain normal input and `ref` values,
- request wrappers omit `out`-only values,
- response wrappers can contain `return`,
- response wrappers can contain `ref` and `out` values,
- response wrappers contain `fault`,
- `_` is the reserved wrapper-side sidechannel property,
- sidechannel values are string dictionaries,
- sidechannel dictionaries can also be JSON-serialized into configured HTTP headers,
- generated ASP.NET request/response DTOs expose concrete types,
- read-only complex properties can represent recursively exposed sub-services,
- multiple contracts may share a route,
- one contract may be registered multiple times,
- contextual arguments can come from getter, route, header, or request DTO,
- contextual request DTO properties can be generated without becoming business method parameters,
- contextual arguments can be overlaid into incoming sidechannel state,
- authentication evaluation can be globally configured and locally disabled,
- argument pre-evaluation occurs before business invocation,
- XML documentation can feed Swagger descriptions,
- MCP exposure is optional per controller configuration,
- endpoint information can expose contract/version/method metadata,
- the client contains legacy support for XML-encapsulated UJMW replies, but direct JSON is the desired format.

---

## 30. Maintenance Rules

After this initial V1, this document MUST be maintained incrementally.

1. Keep top-level section numbering and ordering stable whenever possible.
2. Modify existing sections instead of regenerating the document.
3. Add top-level sections only for genuinely new architectural dimensions.
4. Avoid cosmetic rewrites of unchanged text.
5. Preserve established terminology.
6. Distinguish:
   - desired-state rules,
   - optional capabilities,
   - source-derived current behavior,
   - legacy compatibility.
7. Keep minimal examples minimal.
8. Mark capability-showcase examples explicitly.
9. Add protocol examples whenever a newly discovered feature affects interoperability.
10. Any future change to the wire format MUST be documented with concrete request/response examples.

---

## 31. Reference Basis

This V1 is based on the reviewed UJMW implementation and usage examples covering, among others:

- `IAbstractCallInvoker`,
- `DynamicClientFactory`,
- `UjmwWebCallInvoker`,
- `IHttpPostExecutor`,
- `UjmwClientConfiguration`,
- outgoing request sidechannel configuration,
- incoming response/backchannel configuration,
- `DynamicUjmwControllerOptions`,
- `DynamicUjmwControllerRegistrar`,
- `DynamicUjmwControllerFactory`,
- generated request/response DTOs,
- DTO/parameter mapping,
- dynamic controller invocation,
- auth-header interception,
- ASP.NET Core setup extensions,
- SmartAmbience-compatible request/response dataflow,
- contextual arguments and contextualization,
- AuthTokenHandling-compatible authentication integration,
- generic and aggregated endpoint registration,
- alternative `IAbstractCallInvoker` usage,
- optional MCP exposure,
- optional self-announcement infrastructure.

The implementation is evidence for the protocol and capabilities described here. Future versions SHOULD continue to distinguish deliberate architecture from incidental implementation detail.
