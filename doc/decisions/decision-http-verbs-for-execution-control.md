# Decision: HTTP Verbs for Execution Control

**Status:** Accepted (concept level)  
**Context:** [Epic: Long-Running Operations](../epics/epic-long-running-operations.md)

---

## Context

UJMW is a typed, contract-oriented RPC protocol. It deliberately does **not** derive HTTP verbs from business semantics. Mapping CRUD meaning to verbs (read → `GET`, create → `POST`, update → `PUT`/`PATCH`, delete → `DELETE`) would leak application semantics into the transport layer and is therefore forbidden. Every regular UJMW operation is invoked via `POST`.

The long-running operations epic needs a way to initiate an operation, poll its state and request its cancellation – all on the same operation URL.

## Our position: HTTP verbs belong to the transport layer

HTTP verbs must **not be misused to carry business-logic semantics**.

REST maps verbs onto CRUD operations. In our view this is a **design mistake**:

- **CRUD does not scale as an API model.** Practice shows that even repositories built around CRUD always have to offer more than create/read/update/delete. Real business requirements need operations such as "approve", "recalculate", "merge" or "process all open tickets", and these do not fit any CRUD verb.
- **REST added workarounds instead of fixing the root cause.** As REST evolved, it kept the CRUD mapping and added operation-style endpoints anyway (e.g. `POST /orders/{id}/approve`, "actions", "commands"). This mixes two contradicting models in one API. It does not acknowledge the real issue.
- **RPC is the lower layer.** The correct layering is the opposite of what REST implies. A typed RPC operation is the general primitive. Repository CRUD operations are just one specific set of operations defined **on top of** it. CRUD therefore belongs to the contract (method names and signatures), never to the transport.

The long-running operations feature is an almost **ideal example of why HTTP verbs should belong exclusively to the transport layer**. Initiating, polling and cancelling an execution are pure transport concerns. They apply to **any** operation, regardless of its business meaning. If CRUD semantics already occupied the verbs, `GET` and `DELETE` would be ambiguous here: does `DELETE` delete a business entity or cancel the running call? Because UJMW never assigns business meaning to verbs, they stay free for this transport-level purpose. They can be used here unambiguously and in the same way for every operation.

## Decision

UJMW uses HTTP verbs to express **how the transport layer controls the execution** of an operation – never what the operation does:

| Verb | Transport-level meaning |
|---|---|
| `POST` | Initiate a (new) execution of the operation |
| `GET` | Poll the state of an existing execution (no request content) |
| `DELETE` | Request cancellation of an existing execution (no request content) |

All three verbs address the **identical operation URL** and return the **identical response DTO**.

## Rationale

- The ban on CRUD verb mapping remains **fully in force**. Its reason is that verb semantics must not describe the *content* of a business operation.
- Here the verbs describe the *execution control* of an arbitrary operation, which is a genuine transport concern. UJMW, as a middleware that fully owns its HTTP usage, may define such transport-level semantics. This is **not a layer violation**.
- `GET` and `DELETE` fit naturally, because poll and cancel requests carry no request content.
- Using the same URL makes the relationship between initiation, polling and cancellation maximally transparent.

## Consequences

- Dynamic UJMW controllers (ASP.NET Core only, never WCF) emit additional `GET` and `DELETE` actions for long-running operations; these appear officially in Swagger / OpenAPI.
- Agents and implementers must **not** interpret `GET` / `DELETE` on a UJMW operation URL as read/delete business semantics.
- Regular (non long-running) UJMW operations remain `POST`-only.

## Alternatives considered

- **Separate control endpoints** (e.g. `.../ProcessAllOpenTickets/poll`): rejected – adds artificial URL segments that could collide with method names and obscure the relationship.
- **Control flags in a `POST` body**: rejected – poll and cancel requests carry no business arguments; `POST` with only control data would blur the request DTO.
