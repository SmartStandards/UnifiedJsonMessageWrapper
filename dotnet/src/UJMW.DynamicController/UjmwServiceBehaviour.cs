using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using System.Runtime.ConstrainedExecution;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

    internal static RequestSidechannelProcessingMethod RequestSidechannelProcessor { get; set; } = null;

    internal static ResponseSidechannelCaptureMethod ResponseSidechannelCapturer { get; set; } = null;

    public static void SetRequestSidechannelProcessor(RequestSidechannelProcessingMethod method) {
      RequestSidechannelProcessor = method;
    }
    /// <summary> Overload that is compatible to the signature of 'AmbienceHub.RestoreValuesFrom' (from the 'SmartAmbience' Nuget Package) </summary>
    public static void SetRequestSidechannelProcessor(Action<IEnumerable<KeyValuePair<string, string>>> method) {
      ResponseSidechannelCapturer = (mi, container) => {
        method.Invoke(container);
      };
    }

    public static void SetResponseSidechannelCapturer(ResponseSidechannelCaptureMethod method) {
      ResponseSidechannelCapturer = method;
    }
    /// <summary> Overload that is compatible to the signature of 'AmbienceHub.CaptureCurrentValuesTo' (from the 'SmartAmbience' Nuget Package) </summary>
    public static void SetResponseSidechannelCapturer(Action<IDictionary<string, string>> method) {
      ResponseSidechannelCapturer = (mi, container) => {
        method.Invoke(container);
      };
    }

    //public delegate bool AuthHeaderEvaluatorMethod(
    //  string rawAuthHeader,
    //  MethodInfo calledContractMethod,
    //  string callingMachine,
    //  ref int httpReturnCode
    //);

    //public static AuthHeaderEvaluatorMethod AuthHeaderEvaluator { get; set; } = (
    //  (string rawAuthHeader, MethodInfo calledContractMethod, string callingMachine, ref int httpReturnCode) => {
    //    return true;
    //  }
    //);

  }

}
