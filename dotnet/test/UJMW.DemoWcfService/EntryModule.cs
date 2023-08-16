using System.Diagnostics;
using System.Reflection;
using System.ServiceModel;
using System.Web;
using System.Web.UJMW;

namespace UJMW.DemoWcfService {

  //  CONFIGURED IN web.config:

  //  <system.webServer>
  //    <modules runAllManagedModulesForAllRequests="true" >
  //      <add name="ConfigurativeEntryPointModule" type="UJMW.DemoWcfService.EntryModule" />

  public class EntryModule : IHttpModule {

    public void Init(HttpApplication context) {

      UjmwServiceBehaviour.AuthHeaderEvaluator = (
        (string rawAuthHeader, MethodInfo calledContractMethod, string callingMachine, ref int httpReturnCode) => {
          //in this demo - any auth header is ok - but there must be one ;-)
          if (string.IsNullOrWhiteSpace(rawAuthHeader)) {
            httpReturnCode = 403;
            return false;
          }
          return true;
        }
      );

      //OTHER POSSIBLE OPTIONS...

      //UjmwServiceBehaviour.RequestSidechannelProcessor = (
      //  (MethodInfo calledContractMethod, IEnumerable<KeyValuePair<string, string>> requestSidechannelContainer) => {
      //    ... here you gona extract flowed binding identifiers and apply them to your ambience room
      //  }
      //);

      //UjmwServiceBehaviour.ResponseSidechannelCapturer = (
      //  (MethodInfo calledContractMethod, IEnumerable<KeyValuePair<string, string>> responseSidechannelContainer) => {
      //    ... here you gona collect some additional processing information (like may be performace counters) to send them back
      //  }
      //);

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

    public void Dispose() {
    }

  }

}
