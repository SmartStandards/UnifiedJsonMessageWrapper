using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.UJMW;
using System.Threading;
using System.Threading.Tasks;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace System {

  public enum CommandLineCallMode {
    /// <summary>
    /// The external process is started for each method call.
    /// </summary>
    PerCall,
    /// <summary>
    /// The external process is started once and kept alive for multiple calls.
    /// </summary>
    Persistent
  }

  /// <summary>
  /// Invokes web calls by executing an external command line process.
  /// </summary>
  public class CommandLineExecutor : IAbstractCallInvoker, IDisposable {
    private readonly Type _ContractType;
    private readonly string _ExePath;
    private OutgoingRequestSideChannelConfiguration _RequestSidechannelCfg;
    private IncommingResponseSideChannelConfiguration _ResponseSidechannelCfg;
    private readonly CommandLineCallMode _CallMode;

    // Persistent process fields
    private Process _PersistentProcess;
    private StreamWriter _PersistentWriter;
    private StreamReader _PersistentReader;
    private readonly object _PersistentLock = new object();
    private int _TaskIdCounter = 0;
    private readonly Dictionary<string, TaskCompletionSource<string>> _PendingTasks = new Dictionary<string, TaskCompletionSource<string>>();
    private CancellationTokenSource _PersistentCts;

    public CommandLineExecutor(Type applicableType, string exePath, CommandLineCallMode callMode) {
      _ContractType = applicableType;
      _ExePath = exePath;
      _RequestSidechannelCfg = UjmwClientConfiguration.GetRequestSideChannelConfiguration(applicableType);
      _ResponseSidechannelCfg = UjmwClientConfiguration.GetResponseSideChannelConfiguration(applicableType);
      _CallMode = callMode;
      if (_CallMode == CommandLineCallMode.Persistent) {
        StartPersistentProcess();
      }
    }

    private void StartPersistentProcess() {
      lock (_PersistentLock) {
        if (_PersistentProcess != null && !_PersistentProcess.HasExited)
          return;

        var psi = new ProcessStartInfo {
          FileName = _ExePath,
          Arguments = "",
          RedirectStandardInput = true,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true,
          StandardOutputEncoding = Encoding.UTF8
        };

        _PersistentProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _PersistentProcess.Exited += (s, e) => OnPersistentProcessExited();
        _PersistentProcess.Start();
        _PersistentWriter = _PersistentProcess.StandardInput;
        _PersistentReader = _PersistentProcess.StandardOutput;
        _PersistentCts = new CancellationTokenSource();

        // Start background task to read output lines and dispatch to waiting tasks
        Task.Run(() => PersistentProcessOutputLoop(_PersistentCts.Token));
      }
    }

    private void OnPersistentProcessExited() {
      lock (_PersistentLock) {
        // Fail all pending tasks
        foreach (var tcs in _PendingTasks.Values)
          tcs.TrySetException(new Exception("Persistent process exited unexpectedly."));
        _PendingTasks.Clear();

        // Optionally, restart the process after a short delay
        Task.Run(async () => {
          await Task.Delay(500); // Small delay to avoid rapid restart loops
          try {
            StartPersistentProcess();
          } catch (Exception ex) {
            // Log or handle restart failure
          }
        });
      }
    }

    private async Task PersistentProcessOutputLoop(CancellationToken token) {
      try {
        while (!_PersistentProcess.HasExited && !_PersistentReader.EndOfStream && !token.IsCancellationRequested) {
          var line = await _PersistentReader.ReadLineAsync().ConfigureAwait(false);
          if (line == null) break;
          // Try to extract taskId (first word)
          var firstSpace = line.IndexOf(' ');
          if (firstSpace > 0) {
            var taskId = line.Substring(0, firstSpace);
            var payload = line.Substring(firstSpace + 1);
            TaskCompletionSource<string> tcs = null;
            lock (_PersistentLock) {
              if (_PendingTasks.TryGetValue(taskId, out tcs)) {
                _PendingTasks.Remove(taskId);
              }
            }
            tcs?.SetResult(payload);
          }
        }
      } catch (Exception ex) {
        // Set all pending tasks as failed
        lock (_PersistentLock) {
          foreach (var tcs in _PendingTasks.Values) {
            tcs.TrySetException(ex);
          }
          _PendingTasks.Clear();
        }
      }
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
      if (_CallMode == CommandLineCallMode.PerCall) {
        return InvokeCallPerCall(methodName, arguments, argumentNames, methodSignatureString);
      } else {
        return InvokeCallPersistent(methodName, arguments, argumentNames, methodSignatureString);
      }
    }

    private object InvokeCallPerCall(string methodName, object[] arguments, string[] argumentNames, string methodSignatureString) {
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

          // Find the line that matches the expected JSON format
          string jsonLine = null;
          using (var reader = new StringReader(rawJsonResponse)) {
            string line;
            while ((line = reader.ReadLine()) != null) {
              line = line.Trim();
              if (line.StartsWith("{") && line.Contains("\"return\"")) {
                jsonLine = line;
                break;
              }
            }
          }

          if (jsonLine == null) {
            throw new Exception("No valid JSON response with 'return' property found: " + rawJsonResponse);
          }

          // Try to deserialize the output as JSON
          try {
            var objectDeserializer = new JsonSerializer();
            object returnValue = null;
            string faultMessage = null;
            using (StringReader sr = new StringReader(jsonLine)) {
              using (JsonTextReader jr = new JsonTextReader(sr)) {
                jr.Read();
                if (jr.TokenType != JsonToken.StartObject) {
                  throw new Exception("Response is no valid JSON: " + jsonLine);
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
                      faultMessage = jr.ReadAsString();
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

                // Throw if fault property exists and is not empty
                if (!string.IsNullOrWhiteSpace(faultMessage)) {
                  throw new Exception($"Remote fault: {faultMessage}");
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
                  }

                  if (!backChannelreceived) {
                    if (_ResponseSidechannelCfg.SkipAllowed) {
                      if (_ResponseSidechannelCfg.DefaultsGetterOnSkip != null) {
                        backChannelContent = new Dictionary<string, string>();
                        _ResponseSidechannelCfg.DefaultsGetterOnSkip.Invoke(ref backChannelContent);
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
          }
          catch {
            // If output is not valid JSON, return as string
            return jsonLine ?? rawJsonResponse;
          }
        }
      }
      catch (Exception ex) {
        throw new Exception($"Failed to invoke command line method: {ex.Message}", ex);
      }
    }

    private object InvokeCallPersistent(string methodName, object[] arguments, string[] argumentNames, string methodSignatureString) {
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
        _RequestSidechannelCfg.CaptureMethod.Invoke(method, sideChannelContent);
        paramDict["_"] = sideChannelContent;
      }

      ///// (end) CAPTURE OUTGOING SIDECHANNEL /////

      string paramsJson = System.Text.Json.JsonSerializer.Serialize(paramDict);

      // Generate a unique taskId for this call
      string taskId;
      lock (_PersistentLock) {
        _TaskIdCounter++;
        taskId = "#TASK" + _TaskIdCounter;
      }

      // Prepare the line to send: <methodName> <paramsJson> <taskId>
      string lineToSend = $"{methodName} {paramsJson} {taskId}";

      // Prepare a TaskCompletionSource to await the response
      var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
      lock (_PersistentLock) {
        _PendingTasks[taskId] = tcs;
      }

      // Write the line to the process
      lock (_PersistentLock) {
        _PersistentWriter.WriteLine(lineToSend);
        _PersistentWriter.Flush();
      }

      // Wait for the response
      string responseLine = tcs.Task.GetAwaiter().GetResult();

      // The responseLine should be a JSON string
      string jsonLine = responseLine?.Trim();
      if (string.IsNullOrEmpty(jsonLine) || !jsonLine.StartsWith("{") || !jsonLine.Contains("\"return\"")) {
        throw new Exception("No valid JSON response with 'return' property found: " + responseLine);
      }

      // Try to deserialize the output as JSON
      try {
        var objectDeserializer = new JsonSerializer();
        object returnValue = null;
        string faultMessage = null;
        using (StringReader sr = new StringReader(jsonLine)) {
          using (JsonTextReader jr = new JsonTextReader(sr)) {
            jr.Read();
            if (jr.TokenType != JsonToken.StartObject) {
              throw new Exception("Response is no valid JSON: " + jsonLine);
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
                  faultMessage = jr.ReadAsString();
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

            // Throw if fault property exists and is not empty
            if (!string.IsNullOrWhiteSpace(faultMessage)) {
              throw new Exception($"Remote fault: {faultMessage}");
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
              }

              if (!backChannelreceived) {
                if (_ResponseSidechannelCfg.SkipAllowed) {
                  if (_ResponseSidechannelCfg.DefaultsGetterOnSkip != null) {
                    backChannelContent = new Dictionary<string, string>();
                    _ResponseSidechannelCfg.DefaultsGetterOnSkip.Invoke(ref backChannelContent);
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
      }
      catch {
        // If output is not valid JSON, return as string
        return jsonLine;
      }
    }

    public void Dispose() {
      if (_PersistentProcess != null) {
        try {
          if (!_PersistentProcess.HasExited) {
            _PersistentWriter.WriteLine("STOP");
            _PersistentWriter.Flush();
            _PersistentProcess.WaitForExit(2000);
          }
        } catch { }
        try { _PersistentWriter?.Dispose(); } catch { }
        try { _PersistentReader?.Dispose(); } catch { }
        try { _PersistentProcess?.Dispose(); } catch { }
        _PersistentCts?.Cancel();
        _PersistentCts?.Dispose();
      }
    }
  }
}
