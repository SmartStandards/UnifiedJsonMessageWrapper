using DistributedDataFlow;
using Security.AccessTokenHandling;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting.Contexts;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Web.Hosting;
using System.Web.ModelBinding;
using System.Web.Services.Description;
using System.Web.UJMW;
using System.Web.UJMW.SelfAnnouncement;

namespace UJMW.DemoWcfService {

  //  CONFIGURED IN web.config:

  //  <system.webServer>
  //    <modules runAllManagedModulesForAllRequests="true" >
  //      <add name="ConfigurativeEntryPointModule" type="UJMW.DemoWcfService.EntryModule" />

  public class EntryModule : IHttpModule {

    private static bool _IsInitialized = false;
    public void Init(HttpApplication context) {

      context.BeginRequest += (s, a) => {
        SelfAnnouncementHelper.OnApplicationStarted();
      };

      if (_IsInitialized) {
        return;
      }
      else {
        _IsInitialized = true;
      }

      if (AmbientField.ContextAdapter == null) {
        AmbientField.ContextAdapter = new AmbienceToAppdomainAdapter();
      }

      UjmwHostConfiguration.RequireNtlm = false;
      UjmwHostConfiguration.ForceHttps = false;

      UjmwHostConfiguration.AuthHeaderEvaluator = (
        (string rawAuthHeader, Type contractType, MethodInfo targetContractMethod, string callingMachine, ref int httpReturnCode, ref string failedReason) => {
         
          if(contractType == typeof(IAnnouncementTriggerEndpoint)) {
            return true;
          }
          
          //in this demo - any auth header is ok - but there must be one ;-)
          if (string.IsNullOrWhiteSpace(rawAuthHeader)) {
            httpReturnCode = 403;
            failedReason = "This demo requires at least ANY string as authheader!";
            return false;
          }
          return true;
        }
      );
      //UjmwHostConfiguration.ArgumentPreEvaluator = (
      //  Type contractType,
      //  MethodInfo calledContractMethod,
      //  object[] arguments
      //) => {

      //  contractType.ToString();
      //};
        
      //in this sample were using the AmbienceHub from our 'SmartAmbience' framework
      //which allows us to handle contextual discriminated values very easy:

      AmbienceHub.DefineFlowingContract(
        "tenant-identifiers",
        (contract) => {
          contract.IncludeExposedAmbientFieldInstances("currentTenant");
        }
      );

      UjmwHostConfiguration.ConfigureRequestSidechannel(
        (serviceType, sideChannel) => {
          if (HasDataFlowSideChannelAttribute.TryReadFrom(serviceType, out string contractName)) {
            sideChannel.AcceptUjmwUnderlineProperty();
            sideChannel.AcceptHttpHeader("my-ambient-data");
      


            //sideChannel.AcceptNoChannelProvided(
            //  (ref IDictionary<string, string> defaultData) => {
            //    defaultData["currentTenant"] = "(fallback)";
            //  }
            //);

          }
          else {
            sideChannel.AcceptNoChannelProvided(
              (ref IDictionary<string, string> defaultData) => {
                defaultData["currentTenant"] = "(fallback)";
              }
            );
          }

          sideChannel.ProcessDataVia(
            (incommingData) => AmbienceHub.RestoreValuesFrom(incommingData, contractName)
          );

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

      UjmwHostConfiguration.CorsAllowOrigin = "{origin}";

      UjmwHostConfiguration.SetupCompleted();

      string announcementInfoFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "_AnnouncementInfo.txt");

      SelfAnnouncementHelper.Configure(
        (string[] baseUrls, EndpointInfo[] endpoints, bool act, ref string info) => {

          var sb = new StringBuilder();
          string timestamp = DateTime.Now.ToLongTimeString();

          Console.WriteLine("--------------------------------------");
          if (act) {
            Console.WriteLine("ANNOUNCE:");
          }
          else {
            Console.WriteLine("UN-ANNOUNCE:");
          }
          Console.WriteLine("--------------------------------------");
          foreach (EndpointInfo ep in endpoints) {
            foreach (string url in baseUrls) {
              Console.WriteLine(ep.ToString(url));
              sb.Append(ep.ToString(url));
              if (act) {
                sb.AppendLine(" >> ONLINE @" + timestamp);
              }
              else {
                sb.AppendLine(" >> offline @" + timestamp);
              }

            }
          }
          Console.WriteLine("--------------------------------------");

          File.WriteAllText(announcementInfoFile, sb.ToString());

          info = "was additionally written into file '_AnnouncementInfo.txt'";

        },
        autoTriggerInterval: 1
      );

      //kann einkommentiert werden, um datei direkt anzuzeigen
      //System.Diagnostics.Process.Start(announcementInfoFile);

    }

    public void Dispose() {
    }

  }

}
