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

    public static RequestSidechannelProcessingMethod RequestSidechannelProcessor { get; set; } = null;

    public static ResponseSidechannelCaptureMethod ResponseSidechannelCapturer { get; set; } = null;

    public static ServiceContractInterfaceSelectorMethod ContractSelector { get; set; } = (
      (Type serviceImplementationType, string url, out Type serviceContractInterfaceType) => {

        Type[] contractInterfaces = serviceImplementationType.GetInterfaces().Where(
          (i) => i.GetCustomAttributes(true).Where((a) => a.GetType() == typeof(System.ServiceModel.ServiceContractAttribute)).Any()
        ).ToArray();

        if (contractInterfaces.Length == 0) {
          serviceContractInterfaceType = serviceImplementationType;
          return false;
        }

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
              return true;
            }
          }
        }

        serviceContractInterfaceType = contractInterfaces.First();
        return true;
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
