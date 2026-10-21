# UJMW Requirements – Authentication Hook (AccessTokenManager Refactoring, Phase 1)

## Abstract / Motivation

The AuthTokenHandling framework (AccessTokenManager refactoring) needs UJMW hosts to provide more context to the central authentication hook and to allow the hook to influence the HTTP response (for example `WWW-Authenticate` headers pointing to OAuth protected-resource metadata). This document distills only the requirements that are relevant for UJMW. UJMW itself MUST NOT reference AuthTokenHandling; the integration happens exclusively via BCL types.

## Requirements

| # | Requirement | Scope | Status |
|---|-------------|-------|--------|
| R1 | New delegate `AuthHeaderValidationMethod(rawAuthHeader, contractType, targetContractMethod, apiGroupName, callingMachine, ref httpReturnCode, ref failedReason, ref KeyValuePair<string,string>[] responseHeaders)` | all hosts | implemented |
| R2 | Registration via `UjmwHostConfiguration.SetAuthHeaderEvaluator(...)` (overloads for new and legacy form) | all hosts | implemented |
| R3 | Legacy property `AuthHeaderEvaluator` remains (source/binary compatible), marked `[Obsolete]` | all hosts | implemented |
| R4 | Exactly one internal evaluator storage (new form); legacy delegates are wrapped; the obsolete getter returns the original legacy delegate or a reverse wrapper | all hosts | implemented |
| R5 | `responseHeaders` is initialized with an empty array; returned headers are written for accepted AND rejected requests | ASP.NET Core, WCF | implemented |
| R6 | Header values may contain the literal placeholder `{RequestBaseUrl}` (case-sensitive, every occurrence), replaced with `scheme://host[:port]` + path base / application virtual path, without trailing slash | ASP.NET Core, WCF | implemented |
| R7 | ASP.NET Core: `apiGroupName` = `ApiExplorerSettings(GroupName)` of the dynamic controller (`options.ApiGroupName`), `""` if none | ASP.NET Core | implemented |
| R8 | WCF: `apiGroupName` derived from the relative endpoint path: trim `/`, remove trailing `.svc` (case-insensitive), `/` → `-`, casing preserved (`/api/v1/Calendar.svc` → `api-v1-Calendar`), `""` if unknown | WCF | implemented |
| R9 | CommandLineFacade exposes the API surface but never invokes the evaluator | CommandLineFacade | implemented |
| R10 | Optional `[AllowAnonymous]` on dynamic controllers (`AllowAnonymousForAuthHeaderEvaluatorHook`, default `false`) to avoid double-checks by a host-wide `FallbackPolicy` | ASP.NET Core only | implemented |
| R11 | No breaking changes; no dependency from UJMW to AuthTokenHandling | all | fulfilled |

## Getting Started

```csharp
UjmwHostConfiguration.SetAuthHeaderEvaluator(
	(string rawAuthHeader, Type contractType, MethodInfo targetContractMethod, string apiGroupName,
	 string callingMachine, ref int httpReturnCode, ref string failedReason,
	 ref KeyValuePair<string, string>[] responseHeaders) => {

		if (string.IsNullOrWhiteSpace(rawAuthHeader)) {
			httpReturnCode = 401;
			failedReason = "Authentication required";
			responseHeaders = new KeyValuePair<string, string>[] {
				new KeyValuePair<string, string>(
					"WWW-Authenticate",
					"Bearer resource_metadata=\"{RequestBaseUrl}/.well-known/oauth-protected-resource\""
				)
			};
			return false;
		}
		return true;
	}
);
```

See `dotnet/test/UJMW.DemoWebService.net10.0/Startup.cs` for the demo usage.

## Advanced: Avoiding Double Authorization (ASP.NET Core)

When the host uses a global `FallbackPolicy` (e.g. `RequireAuthenticatedUser`) but authentication of UJMW endpoints is done by the UJMW evaluator, set:

```csharp
options.AllowAnonymousForAuthHeaderEvaluatorHook = true;
```

The `[AllowAnonymous]` attribute is emitted only if `EnableAuthHeaderEvaluatorHook` is also `true`. Use it only when an evaluator is registered.

## Compatibility Notes

- Existing code assigning `UjmwHostConfiguration.AuthHeaderEvaluator` keeps working; it only produces an obsolete warning (CS0618).
- Legacy evaluators receive no API group name and cannot return response headers.
- The last registration (property or `SetAuthHeaderEvaluator`) wins.

## Important Artifacts

| Artifact | Purpose |
|----------|---------|
| `UjmwHostConfiguration` (DynamicController, DynamicWcfHost, CommandLineFacade) | Delegates, registration, adapters, `ResolveRequestBaseUrlPlaceholder` |
| `AuthHeaderInterceptorAttribute` | ASP.NET Core invocation, API group resolution, header writing |
| `DynamicUjmwControllerOptions.AllowAnonymousForAuthHeaderEvaluatorHook` / `DynamicUjmwControllerFactory` | Optional `[AllowAnonymous]` emission |
| `DispatchMessageInspector` / `ServiceBehaviorToApplyDispatchHooks` / `UjmwServiceHostFactory` | WCF invocation, group name derivation and header writing |
| `UjmwHostConfigurationTests` | Minimal tests (placeholder, legacy round trip, reverse adapter) |
