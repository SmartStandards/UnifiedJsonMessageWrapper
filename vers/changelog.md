# Change log
This files contains a version history including all changes relevant for semantic Versioning...

*(it is automatically maintained using the ['KornSW-VersioningUtil'](https://github.com/KornSW/VersioningUtil))*


## Upcoming Changes

* **breaking Change**: DisableNtlm=false was replaced by RequireNtlm=true and is applied via separate dispatch-behaviour
* **new Feature**: added hook to customize the 'WebHttpBinding' for WCF
* **new Feature**: added hook for Logging
* Fix: WCF can now provides the 'fault'-Property on exceptions properly (instead of html-error-page)



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
 - completet impl. the **new Feature** of full sidechannel support for WCF and ASP.net core WebAPI



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



