# Change log
This files contains a version history including all changes relevant for semantic Versioning...

*(it is automatically maintained using the ['KornSW-VersioningUtil'](https://github.com/KornSW/VersioningUtil))*


## Upcoming Changes

* **new Feature**: CORS-Headers (for WCF Endpoints) can now be configured separately
* **new Feature**: WCF-Contract selector will now prefer Contract-Interfaces where the name matches to the endpoint-url instead of only checking the version-string from url



## v 2.4.0
released **2024-06-03**, including:
 - **new Feature**: added a new Hook named 'ArgumentPreEvaluator' (for WCF & WebAPI), which can be used to inspect method-arguments before invocation. This is helpful to build a centralized parameter-guard
 - Fix: HTTP Header names for side channel are now evaluated case-insensitive instead of only working when headers are configured completely lower



## v 2.3.0
released **2024-05-22**, including:
 - **new Feature**: added some errorhandling for WCF

   

## v 2.2.1
released **2024-05-16**, including:
 - AuthHeaderEvaluator can now return a failedReason byref
 - Fix: authHeaderEvaluator for WCF now passed methodInfo also for Methods from inherited contracts (was null before)



## v 2.2.0
released **2024-05-15**, including:
 - **new Feature**: Added Support to configure CORS-Headers in WCF.
 - **new Feature**: option 'HideExeptionMessageInFaultProperty' in MCV & .NET core WebApi



## v 2.1.1
released **2024-04-30**, including:
 - Fix: WCF AuthHeaderEvaluator Hook now passes a correct MethodInfo for the 'calledContractMethod' instead of a MethodInfo pointing to the concrete implementation.



## v 2.1.0
released **2024-04-29**, including:
 - **new Feature**: A new hook 'UjmwClientConfiguration.HttpClientFactory' allows to customize HttpClient creation. This can be used to adjust proxy-settings, while the default is to use NO PROXY!
 - Fix: 'AuthHeaderEvaluator'-Hook is now working for ASP.net Core
 - Fix: AssemblyInfo for .NET-Fx 4 Projects are now without wildcard, to ensure propper versions instead of 999.x



## v 2.0.0
released **2024-04-09**, including:
 - **breaking Change**: DisableNtlm=false was replaced by RequireNtlm=true and is applied via separate dispatch-behaviour
 - **new Feature**: added hook for Logging
 - **new Feature**: added hook to customize the 'WebHttpBinding' for WCF
 - Fix: WCF now provides the 'fault'-Property on exceptions properly (instead of html-error-page)



## v 1.6.0
released **2024-04-03**, including:
 - **new Feature**: added MySetting "*GracetimeForSetupPhase*" for WCF to fix MultiThreading problems and ensure that there is enough time for configurative IHttpModules to adjust the UjmwHostConfiguration



## v 1.5.0
released **2024-03-15**, including:
 - **new Feature**: added a global url-discovery hook and support for retry-policies



## v 1.4.0
released **2024-03-07**, including:
 - **new Feature**: now supporting generic contract-interfaces



## v 1.3.0
released **2024-03-04**, including:
 - **new Feature**: ASP.net Core controllers are now using attributes for swashbuckle (if present)



## v 1.2.1
released **2024-02-16**, including:
 - fulfilled compatibility conventions for 'AuthTokenHandling'



## v 1.2.0
released **2024-02-15**, including:
 - completed impl. the **new Feature** of full sidechannel support for WCF and ASP.net core WebAPI



## v 1.1.2
released **2023-10-17**, including:
 - new revision without significant changes



## v 1.1.1
released **2023-10-17**, including:
 - added hook for logging of exceptions which were thrown during host creation (when WCF is using our factory)



## v 1.1.0
released **2023-10-11**, including:
 - **new Feature**: dynamic (MVC-)controllers for asp.net core webapi
 - -



## v 1.0.0
released **2023-08-16**, including:
 - got wcf-facade and dynamic client running... got compatible with previous **MVP**



