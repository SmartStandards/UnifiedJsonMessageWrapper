# UnifiedJsonMessageWrapper ("UJMW") Specification

|            | Info                                                         |
| ---------- | ------------------------------------------------------------ |
| author:    | [KornSW](https://github.com/KornSW) + [derVodi](https://github.com/derVodi) |
| license:   | [Apache-2](https://choosealicense.com/licenses/apache-2.0/)  |
| version:   | 1.0.0                                                        |
| timestamp: | 2020-01-16                                                   |

### Contents

- [Motivation](#Motivation)
- [Transporting Arguments](#Transporting-arguments)
- [Naming](#Naming)
- [DataTypes](#DataTypes)
- [Encoding](#Encoding)
- [The "return"-Property](#The-return-Property)
- ["Out"-Arguments](#Out-Arguments)
- [The "fault"-Property](#The-fault-Property)
- [The "returnCode"-Pattern](#The-returncode-Pattern)
- [Side-Channels](#Side-Channels)
- ... [well known](#Reserved--well-known-Side-Channel-names)



# Motivation:

#### Problems of SOAP and its "Call-based" style

TBD

#### Problems of REST and its "CRUD" style

TBD

#### The truth within the middle

What we want is .......       TBD

#### a quick look on classical WCF-Wrappers

TBD



# Transporting Arguments

Each request and response must have the **wrapper object as json-root** containing named Arguments as properties.

The wrapper capsule is **ALWAYS required, also if there is none or just one argument**

```json
{
    "myNamedParam1": ...,
    "myNamedParam2": ...,
}
```

### Naming:  

*  Property names are always in [**chamelCase**](https://en.wikipedia.org/wiki/Camel_case)

### DataTypes

| Type                | Convention                                                   | Sample                         |
| ------------------- | ------------------------------------------------------------ | ------------------------------ |
| **Date / Time**     | must be in [**ISO8601**](https://en.wikipedia.org/wiki/ISO_8601) Format + should be [**UTC**](https://en.wikipedia.org/wiki/Coordinated_Universal_Time) | *2020-06-15T13:45:30.0000000Z* |
| **Byte[] / Binary** | must be in [**Base64**](https://en.wikipedia.org/wiki/Base64) | *TWFuIGlzIGRpc3Rpbmd==*        |
| **Numeric values**  | must not have 1000-separator-chars + must have the char "." for separating the decimal places | *123433454.23*                 |

### Encoding

* always [**UTF-8**](https://en.wikipedia.org/wiki/UTF-8)

* [mimetype](https://en.wikipedia.org/wiki/Media_type) "application/json"

  

# The "return"-Property

"return" is a constant name which is magic-value between the other argument Names. It represents the return-value of this Function, which is invoked. 

```json
{
    "return": ...
}
```

### VOID-Methods

If a VOID (a Method without a return-value) is invoked, the result-wrapper must not contain a "return" property. A "return"-property with a null-value is NOT allowed in this case, because this is reserved for Functions returning a null-value.

###  Why "return" instead of "result"

We never use "result" here because assuming that an return-value would have the semantic to be the "result" of an operation is highly BL-related and needs to be specified within the service-contract (using argument or method-names or comments) instead of the transport layer. "return" has the maximum level abstraction and is exactly right for this layer!



# "Out"-Arguments

Same way as in arguments  - a VOID which has only IN/OUT arguments will have exactly the same response-wrapper as the request-wrapper.



# The "fault"-Property

Is also a constant name which is magic-value **representing an Exception**. This means, that only response-wrappers can contain a "fault" Property AND **if it is exists, no other properties have to exist**!

```json
{
    "fault": "message"
}
```

Please note that the "fault"-Property should only used for critical, non-business Errors - like Exceptions. All regular possible failures of the executed operation should be transferred over other channels. For that were recommending the "returnCode"-Pattern as described below (maybe in combination with the "lastError" - SideChannel. Nevertheless also Exceptions from our BL should not affect the transport-layer technology. However, even exceptions from the BL should not affect the transport layer technology, and because of this, the usage of the fault-Property MUST NOT be accompanied by an http-response code other than 200!



# The "returnCode"-Pattern

This is just a propose for using an OUT-Arg which usually should have a name like "returnCode" to do something like a Try...Methods (in some Languages). In this case were not blocking the primary "return" value just for delivering an information about the success of a invoked method.

* The code does not have a dedicated semantic to always be an error - so it must not be called "errorCode"!

* If an returnCode was delivered, which indicated an error, then some additional details can be placed within the "lastError"-SideChannel (as described below)

  

# Side-Channels

This part is optional within the wrapper!

To avoid conflicts with the regular arguments, we need a sub-structure which is placed within a property named "_" (also a magic-value).

```json
{
    "_": {
      "myAdditionalData": ...,
    },
}
```

'Ambience' is a very complex concern, so that we cant give a full introduction here. It relates to 'aspect orientation' (AOP) and 'contextual' programming principles. Our [SmartAmbience](https://github.com/SmartStandards/SmartAmbience) Library will provide convenience for that and can easy be coupled with the UJMW side channel.



# About the Tooling

## Clients

### for .NET Framework & .NET core
  **IS STABLE** (see '/dotnet/UJMW.sln')
  The NuGet-Package ID is 'UJMW.DynamicClient'

### for JavaScript / TypeScript
  *IS COMMING SOON*
  The NPM-Package ID is 'UJMW.DynamicClient'

## Server Facades

## A Dynamic ServiceHost Factory for WCF (.NET fx 461)
  **IS STABLE** (see '/dotnet/UJMW.sln' ) - a Demo-Service is also included...
  The NuGet-Package ID is 'UJMW.DynamicWcfHost'  

```xml
<system.serviceModel>
  <serviceHostingEnvironment aspNetCompatibilityEnabled="false" multipleSiteBindingsEnabled="true" >
    <serviceActivations>
      <add relativeAddress="YourService.svc" service="TheNamespace.YourService, AssName"
           factory="System.Web.UJMW.UjmwServiceHostFactory, UJMW.DynamicWcfHost" />
```


this could also be necessary:
```xml
<runtime>
  <assemblyBinding xmlns = "urn:schemas-microsoft-com:asm.v1" >
    <dependentAssembly>
      <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
      <bindingRedirect oldVersion = "0.0.0.0-14.0.0.0" newVersion="8.0.0.0" />
    </dependentAssembly>
```

you can configure behavior in this way:

```xml
<system.webServer>
 <modules runAllManagedModulesForAllRequests="true">
   <add name="ConfigurativeEntryPointModule" type="UJMW.DemoWcfService.EntryModule"/>
```

```c#
public class EntryModule : IHttpModule {
    
  public void Init(HttpApplication context) {
    UjmwServiceBehaviour.AuthHeaderEvaluator = ...
    UjmwServiceBehaviour.RequestSidechannelProcessor = ...
    UjmwServiceBehaviour.ResponseSidechannelCapturer = ...
    UjmwServiceBehaviour.ContractSelector = ...
    UjmwServiceBehaviour.ForceHttps = true;
  }
    
  public void Dispose() { }
}
```



## A Dynamic Controller Factory for ASP.NET core WebAPI
  *IS COMMING SOON*
  The NuGet-Package ID is 'UJMW.DynamicController'

## Controller Code Generator for ASP.NET core WebAPI
  **IS STABLE** but discontinued (see '/Resources/for ASP.NET 5 MVC/...')
  The NuGet-Package ID is 'UJMW.Tools.CodeGen'

