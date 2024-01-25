using DistributedDataFlow;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Remoting.Contexts;
using System.ServiceModel;
using System.Threading;
using System.Web;
using System.Web.ModelBinding;
using System.Web.UJMW;

namespace UJMW.DemoWcfService {

  //  CONFIGURED IN web.config:

  //  <system.webServer>
  //    <modules runAllManagedModulesForAllRequests="true" >
  //      <add name="ConfigurativeEntryPointModule" type="UJMW.DemoWcfService.EntryModule" />

  public class EntryModule : IHttpModule {

    private static bool _IsInitialized = false;
    public void Init(HttpApplication context) {

      if (_IsInitialized) {
        return;
      }
      else {
        _IsInitialized = true;
      }

      if(AmbientField.ContextAdapter == null) {
        AmbientField.ContextAdapter = new AmbienceToAppdomainAdapter();
      }

      UjmwHostConfiguration.DiableNtlm = false;
      UjmwHostConfiguration.ForceHttps = false;

      UjmwHostConfiguration.AuthHeaderEvaluator = (
        (string rawAuthHeader, MethodInfo calledContractMethod, string callingMachine, ref int httpReturnCode) => {
          //in this demo - any auth header is ok - but there must be one ;-)
          if (string.IsNullOrWhiteSpace(rawAuthHeader)) {
            httpReturnCode = 403;
            return false;
          }
          return true;
        }
      );

      //in this sample were using the AmbienceHub from our 'SmartAmbience' framework
      //which allows us to handle contextual discriminated values very easy:

      AmbienceHub.DefineFlowingContract(
        "tenant-identifiers",
        (contract) => {
          contract.IncludeExposedAmbientFieldInstances("\"currentTenant\"");
        }
      );

      UjmwHostConfiguration.ConfigureRequestSidechannel(
        (serviceType, sideChannel) => {
          if (HasDataFlowSideChannelAttribute.TryReadFrom(serviceType, out string contractName)) {

            sideChannel.AcceptHttpHeader("my-ambient-data");
            sideChannel.AcceptUjmwUnderlineProperty();

            sideChannel.ProcessDataVia(
              (incommingData) => AmbienceHub.RestoreValuesFrom(incommingData, contractName)
            );
          }
          else {
            sideChannel.AcceptNoChannelProvided(
              (ref IDictionary<string, string> defaultData) => {
                defaultData["ProfileIdentifier"] = "(Independent)";
              }
            );
          }
        }
      );

      UjmwHostConfiguration.ConfigureResponseSidechannel(
        (serviceType, sideChannel) => {
          if (HasDataFlowBackChannelAttribute.TryReadFrom(serviceType, out string contractName)) {

            sideChannel.ProvideHttpHeader("my-ambient-data");
            sideChannel.ProvideUjmwUnderlineProperty();

            sideChannel.CaptureDataVia(
              (snapshot)=>AmbienceHub.CaptureCurrentValuesTo(snapshot, contractName)
            );
          }
          else {
            sideChannel.ProvideNoChannel();
          }
        }
      ); 
      
    //UjmwServiceBehaviour.ContractSelector = (
    //  (Type serviceImplementationType, string url, out Type serviceContractInterfaceType) => { 
    //    ... here you could choose, which implmentend service-contract interface to be used (my be related to the related url)
    //  }
    //);

    //UjmwServiceBehaviour.ForceHttps = true;

    //UNDER DEVEOPMENT
    //UjmwServiceBehaviour.BlExceptionHandler = (method, ex) => {
    //  string msg = $"EXCEPTION (from {method.DeclaringType.FullName}.{method.Name}): {ex.Message}";
    //  Debug.WriteLine(msg);
    //  throw new FaultException(msg); //WARNING: exposing error details is only a good idea for non-prod env's!
    //};

  }

    public static void RestoreValuesFrom(
      IEnumerable<KeyValuePair<string, string>> sourceToRestore, string flowingContractName) { 
    }
    public static void RestoreValuesFrom(
      IEnumerable<KeyValuePair<string, string>> sourceToRestore) {
    }

    public void Dispose() {
    }

  }

}
