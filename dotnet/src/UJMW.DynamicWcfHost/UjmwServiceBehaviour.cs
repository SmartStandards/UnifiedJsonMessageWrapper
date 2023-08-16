using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace System.Web.UJMW {

  public class UjmwServiceBehaviour {

    private UjmwServiceBehaviour() {
    }

    public delegate void RequestSidechannelProcessingMethod(
      MethodInfo calledContractMethod,
      IEnumerable<KeyValuePair<string, string>> requestSidechannelContainer
    );

    public delegate void ResponseSidechannelCaptureMethod(
      MethodInfo calledContractMethod,
      IDictionary<string, string> responseSidechannelContainer
    );

    public delegate bool ServiceContractInterfaceSelectorMethod(
      Type serviceImplementationType,
      string url,
      out Type serviceContractInterfaceType
    );

    public delegate bool AuthHeaderEvaluatorMethod(
      string rawAuthHeader,
      MethodInfo calledContractMethod,
      string callingMachine,
      ref int httpReturnCode
    );

    //UNDER DEVELOPMENT...
    //https://stackoverflow.com/questions/17961564/wcf-exception-handling-using-ierrorhandler
    //https://www.c-sharpcorner.com/UploadFile/b182bf/centralize-exception-handling-in-wcf-part-10/
    //public static Action<MethodInfo,Exception> BlExceptionHandler { get; set; } = null;

    public static RequestSidechannelProcessingMethod RequestSidechannelProcessor { get; set; } = null;

    public static ResponseSidechannelCaptureMethod ResponseSidechannelCapturer { get; set; } = null;

    public static ServiceContractInterfaceSelectorMethod ContractSelector { get; set; } = (
      (Type serviceImplementationType, string url, out Type serviceContractInterfaceType) => {

        Type[] allInterfaces = serviceImplementationType.GetInterfaces().Where(
         (i) => (i != typeof(IDisposable))
        ).ToArray();

        if (allInterfaces.Length == 0) {
          //use the concrete impl. type only if there is relly no interface implemented
          serviceContractInterfaceType = serviceImplementationType;
          return false;
        }

        Type[] contractInterfaces = allInterfaces.Where(
          (i) => i.GetCustomAttributes(true).Where((a) => a.GetType() == typeof(System.ServiceModel.ServiceContractAttribute)).Any()
        ).ToArray();

        if (contractInterfaces.Length > 1) {
          string[] urlTokens = url.Split('/');
          string versionFromUrl = null;
          for (int i = urlTokens.Length - 1; i > 0; i--) {
            if (Regex.IsMatch(urlTokens[i], "^([vV][0-9]{1,})$")) {
              versionFromUrl = urlTokens[i].ToLower();
              break;
            }
          }
          if (versionFromUrl != null) {
            var versionMatchingInterface = contractInterfaces.Where((i) => ("." + i.FullName.ToLower() + ".").Contains(versionFromUrl)).FirstOrDefault();
            if (versionMatchingInterface != null) {
              serviceContractInterfaceType = versionMatchingInterface;
              //prefer a interface (with a 'ServiceContractAttribute') wich is matching the version
              return true;
            }
          }
        }

        if (contractInterfaces.Length > 0) {
          //prefer the first interface (with a 'ServiceContractAttribute')
          serviceContractInterfaceType = contractInterfaces.First();
          return true;
        }
        else {
          //otherwise use the first interface (without a 'ServiceContractAttribute')
          serviceContractInterfaceType = allInterfaces.First();
          return false;
        }

      }
    );

    public static AuthHeaderEvaluatorMethod AuthHeaderEvaluator { get; set; } = (
      (string rawAuthHeader, MethodInfo calledContractMethod, string callingMachine, ref int httpReturnCode) => {
        return true;
      }
    );

    public static bool ForceHttps { get; set; } = false;

  }

}
