using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Web.UJMW;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace System {

  /// <summary>
  /// Invokes web calls by executing an external command line process.
  /// </summary>
  public class CommandLineExecutor : IAbstractCallInvoker {
    private readonly Type _ContractType;
    private readonly string _ExePath;
    private OutgoingRequestSideChannelConfiguration _RequestSidechannelCfg;
    private IncommingResponseSideChannelConfiguration _ResponseSidechannelCfg;

    public CommandLineExecutor(Type applicableType, string exePath) {
      _ContractType = applicableType;
      _ExePath = exePath;
      _RequestSidechannelCfg = UjmwClientConfiguration.GetRequestSideChannelConfiguration(applicableType);
      _ResponseSidechannelCfg = UjmwClientConfiguration.GetResponseSideChannelConfiguration(applicableType);
    }

    /// <summary>
    /// Invokes a method by calling an external executable that uses CommandLineWrapper.
    /// </summary>
    /// <param name="methodName">The method to invoke.</param>
    /// <param name="arguments">The argument values.</param>
    /// <param name="argumentNames">The argument names (must match method parameter names).</param>
    /// <param name="methodSignatureString">Unused, for compatibility.</param>
    /// <returns>The result from the external process, deserialized from JSON.</returns>
    public object InvokeCall(string methodName, object[] arguments, string[] argumentNames, string methodSignatureString) {

      var exePath = _ExePath;

      MethodInfo method = UjmwWebCallInvoker.FindMethod(_ContractType, methodName);
      ParameterInfo[] parameters = method.GetParameters();
      if (method == null) {
        throw new MissingMethodException(
          $"Method '{methodName}' not found in contract '{_ContractType.FullName}'."
        );
      }

      // Build paramsJson as a JSON object with argument names as properties
      var paramDict = new Dictionary<string, object>();
      if (argumentNames != null && arguments != null) {
        for (int i = 0; i < argumentNames.Length && i < arguments.Length; i++) {
          paramDict[argumentNames[i]] = arguments[i];
        }
      }

      ///// CAPTURE OUTGOING SIDECHANNEL /////
      if (
        (_RequestSidechannelCfg != null) && (
          _RequestSidechannelCfg.UnderlinePropertyIsProvided ||
          _RequestSidechannelCfg.ChannelsToProvide.Contains("_")
        )
      ) {
        var sideChannelContent = new Dictionary<string, string>();
        //string sideChannelJson = null;
        _RequestSidechannelCfg.CaptureMethod.Invoke(method, sideChannelContent);
        paramDict["_"] = sideChannelContent;
      }

      ///// (end) CAPTURE OUTGOING SIDECHANNEL /////

      string paramsJson = System.Text.Json.JsonSerializer.Serialize(paramDict);

      // Prepare process start info
      var psi = new ProcessStartInfo {
        FileName = exePath,
        Arguments = $"\"{methodName}\" \"{paramsJson.Replace("\"", "\\\"")}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8
      };

      try {
        using (var process = Process.Start(psi)) {
          if (process == null)
            throw new InvalidOperationException("Failed to start process.");

          string rawJsonResponse = process.StandardOutput.ReadToEnd();
          string error = process.StandardError.ReadToEnd();
          process.WaitForExit();

          if (process.ExitCode != 0) {
            throw new Exception($"Process exited with code {process.ExitCode}: {error}");
          }

          // Try to deserialize the output as JSON
          try {

            var objectDeserializer = new JsonSerializer();
            object returnValue = null;
            using (StringReader sr = new StringReader(rawJsonResponse)) {
              using (JsonTextReader jr = new JsonTextReader(sr)) {
                jr.Read();
                if (jr.TokenType != JsonToken.StartObject) {
                  throw new Exception("Response is no valid JSON: " + rawJsonResponse);
                }

                IDictionary<string, string> backChannelContent = null;
                string currentPropName = "";
                while (jr.Read()) {

                  if (jr.TokenType == JsonToken.PropertyName) {
                    currentPropName = jr.Value.ToString();
                    if (currentPropName == "_") {
                      jr.Read();
                      backChannelContent = objectDeserializer.Deserialize<Dictionary<string, string>>(jr);
                    }
                    else if (currentPropName.Equals("return", StringComparison.InvariantCultureIgnoreCase)) {
                      jr.Read();
                      if (method.ReturnType != typeof(void)) {
                        returnValue = objectDeserializer.Deserialize(jr, method.ReturnType);
                      }
                    }
                    else if (currentPropName.Equals("fault", StringComparison.InvariantCultureIgnoreCase)) {
                      string faultMessage = jr.ReadAsString();
                      if (!String.IsNullOrWhiteSpace(faultMessage)) {
                        //UjmwClientConfiguration.FaultRepsonseHandler.Invoke(fullUrl, method, faultMessage);
                      }
                    }
                    else {
                      jr.Read();
                      var param = parameters.Where((p) => p.Name.Equals(currentPropName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                      object value = null;
                      if (param != null && param.ParameterType.IsByRef) {
                        Type typeToDeserialize = param.ParameterType.GetElementType();
                        value = objectDeserializer.Deserialize(jr, typeToDeserialize);

                        int argIndex = Array.IndexOf(argumentNames, param.Name);
                        arguments[argIndex] = value;
                      }
                    }
                  }
                  else if (jr.TokenType == JsonToken.StartObject) {
                    string rawJson = jr.ReadAsString();

                  }
                  else {
                  }
                }
                ///// RESTORE INCOMMING BACKCHANNEL /////
                if (_ResponseSidechannelCfg.UnderlinePropertyIsAccepted || _ResponseSidechannelCfg.AcceptedChannels.Length > 0) {

                  bool backChannelreceived = false;
                  foreach (string channelName in _ResponseSidechannelCfg.AcceptedChannels) {
                    if (channelName == "_") {
                      if (backChannelContent != null) {
                        ////// PROCESS //////
                        _ResponseSidechannelCfg.ProcessingMethod.Invoke(method, backChannelContent);
                        backChannelreceived = true;
                        break;
                      }
                    }
                    //else if (responseHeaders != null) {
                    //  var hdr = responseHeaders.Where((h) => h.Key == channelName);
                    //  if (hdr.Any()) {
                    //    //will be done once, if we need in as json
                    //    string sideChannelFromHeader = hdr.First().Value.ToString();
                    //    backChannelContent = JsonConvert.DeserializeObject<Dictionary<string, string>>(sideChannelFromHeader);
                    //    ////// PROCESS //////
                    //    _ResponseSidechannelCfg.ProcessingMethod.Invoke(method, backChannelContent);
                    //    backChannelreceived = true;
                    //    break;
                    //  }
                    //}
                  }

                  if (!backChannelreceived) {
                    if (_ResponseSidechannelCfg.SkipAllowed) {
                      //if the whole getter is null, then (and only in this case) it will be a 'silent skip'
                      if (_ResponseSidechannelCfg.DefaultsGetterOnSkip != null) {
                        backChannelContent = new Dictionary<string, string>();
                        _ResponseSidechannelCfg.DefaultsGetterOnSkip.Invoke(ref backChannelContent);
                        //also null (when the DefaultsGetterOnSkip sets the ref handle to null) can be
                        //passed to the processing method...
                        _ResponseSidechannelCfg.ProcessingMethod.Invoke(method, backChannelContent);
                      }
                    }
                    else {
                      Trace.TraceWarning("Rejected incomming response because of missing side channel");
                      throw new Exception("Response has no SideChannel");
                    }
                  }

                }
                ///// (end) RESTORE INCOMMING BACKCHANNEL /////
              }
            }
            return returnValue;
            //return JsonSerializer.Deserialize<object>(rawJsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
          }
          catch {
            // If output is not valid JSON, return as string
            return rawJsonResponse;
          }
        }
      }
      catch (Exception ex) {
        throw new Exception($"Failed to invoke command line method: {ex.Message}", ex);
      }
    }
  }
}
