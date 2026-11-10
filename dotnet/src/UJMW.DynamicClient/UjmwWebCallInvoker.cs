using Logging.SmartStandards;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace System.Web.UJMW {

  //developed on base of 'https://github.com/KornSW/W3bstract/tree/master/W3bstract/W3bstract.WebServiceConnector/Client'

  internal class UjmwWebCallInvoker: IAbstractCallInvoker {

    private Type _ContractType;
    private IHttpPostExecutor _HttpPostExecutor;
    private Func<string> _UrlGetter;
    private OutgoingRequestSideChannelConfiguration _RequestSidechannelCfg;
    private IncommingResponseSideChannelConfiguration _ResponseSidechannelCfg;
    private Action _OnDisposeInvoked;

    public UjmwWebCallInvoker(
      Type applicableType,
      IHttpPostExecutor httpPostExecutor,
      Func<string> urlGetter,
      Action onDisposeInvoked = null
    ) {
      _ContractType = applicableType;
      _HttpPostExecutor = httpPostExecutor;
      _UrlGetter = urlGetter;
      _RequestSidechannelCfg = UjmwClientConfiguration.GetRequestSideChannelConfiguration(applicableType);
      _ResponseSidechannelCfg = UjmwClientConfiguration.GetResponseSideChannelConfiguration(applicableType);
      _OnDisposeInvoked = onDisposeInvoked;
      if (_OnDisposeInvoked == null) {
        _OnDisposeInvoked = (() => { });
      }
    }

    //internal static MethodInfo FindMethod(Type declaringType,string methodName) {
    //  MethodInfo m = declaringType.GetMethod(methodName);
    //  if(m != null) {
    //    return m; //99%
    //  }

    //  if(declaringType.BaseType != null) {
    //    m = FindMethod(declaringType.BaseType, methodName);
    //    if (m != null) {
    //      return m;
    //    }
    //  }
    //  foreach (Type iType in declaringType.GetInterfaces()){
    //    m = FindMethod(iType, methodName);
    //    if (m != null) {
    //      return m;
    //    }
    //  }
    //  return null;
    //}

    private object _UrlReloadLock = new object();
    private string _CachedEndpointUrl = null;
    private DateTime _EndpointUrlCacheTime = DateTime.MinValue;

    /// <summary>
    /// Fixed polling interval (ms) which overrides the 'pollNbf' of the server, or -1 (default: respect the 'pollNbf').
    /// </summary>
    private volatile int _FixedPollIntervalMs = -1;

    /// <summary>
    /// Overrides the polling interval of long-running operations (0 becomes 500 ms, negative values restore the default).
    /// </summary>
    public void OverrideNbfPolling(int fixedSeconds) {

      if (fixedSeconds < 0) {
        _FixedPollIntervalMs = -1;
      }
      else if (fixedSeconds == 0) {
        _FixedPollIntervalMs = 500;
      }
      else {
        _FixedPollIntervalMs = fixedSeconds * 1000;
      }

    }

    //WARNING: WILL BE INVOKED VIA EMIT - WEAK REFERENCE!!!
    public object InvokeCall(string uniqueMethodNameOnTransportLayer, MethodInfo method, object[] arguments, string[] argumentNames, string methodSignatureString) {
         
      if(uniqueMethodNameOnTransportLayer != null && uniqueMethodNameOnTransportLayer == nameof(IDisposable.Dispose)) {
        _OnDisposeInvoked.Invoke();
        return null;
      }

      lock (_UrlReloadLock) {
        if (_EndpointUrlCacheTime < DateTime.Now) {
          _CachedEndpointUrl = _UrlGetter.Invoke();
          _EndpointUrlCacheTime = DateTime.Now.AddSeconds(
            UjmwClientConfiguration.UrlGetterCacheSec
          );
        }
      }

      string endpointUrl = _CachedEndpointUrl;

      int currentTry = 0;
      do {
        currentTry++;

        int httpCode = 0;
        try {

          return InvokeWebCallInternal(endpointUrl, uniqueMethodNameOnTransportLayer, method, arguments, argumentNames, methodSignatureString, ref httpCode);

        }
        catch (Exception ex) {

          if (httpCode >= 200 && httpCode < 300) {
            //exceptions directly BEFORE sending or AFTER receiving should not cause a retry,
            //because they are not related to the transport layer
            throw; 
          }

          if (!UjmwClientConfiguration.RetryDecider.Invoke(_ContractType, ex, currentTry, httpCode, ref endpointUrl)) {
            throw;
          }
        }

        Threading.Thread.Sleep(100); //< security feature 1
      } while (currentTry < 20); //< security feature 2
      throw new Exception("UJMW Dynamic Client detected an endless loop caused by the 'UjmwClientConfiguration.RetryDecider'!");

    }

    private object InvokeWebCallInternal(
      string rootUrl, string uniqueMethodNameOnTransportLayer, MethodInfo method, object[] arguments, string[] argumentNames, string methodSignatureString, ref int httpReturnCode
    ) {
      //a little hack to handle exceptions BEFORE sending the request should be handled in the same way as
      //as exceptions AFTER successfully received a response
      httpReturnCode = 200;

      string fullUrl;
      if (rootUrl.EndsWith("/")) {
        fullUrl = rootUrl;
      }
      else {
        fullUrl = rootUrl + "/";
      }

      if (uniqueMethodNameOnTransportLayer != null) {
        fullUrl = fullUrl + uniqueMethodNameOnTransportLayer;
      }
      else {

        httpReturnCode = -1; //in case of exceptions during call

        //call the info-endpoint
        _HttpPostExecutor.ExecuteHttpPost( //will become a HTTP-GET because of content==null
          fullUrl,
          null, new Dictionary<string, string>(),
          out string infoRawResponse, out var infoResponseHeaders,
          out string infoReasonPhrase
        );

        return infoRawResponse;
      }

      ParameterInfo[] parameters = method.GetParameters();

      //a 'CancellationToken' parameter marks a potentially long-running operation (see 'doc/epics/epic-long-running-operations.md')
      bool isLongRunning = UjmwAsyncProtocol.IsLongRunningMethod(method);
      CancellationToken cancellationToken = this.GetCancellationTokenArgument(parameters, arguments);
      cancellationToken.ThrowIfCancellationRequested();

      //MethodInfo method = FindMethod(_ContractType, methodName);
      var requestContent = new Dictionary<string, object>();
      Dictionary<string, string> requestHeaders = null;

      ///// CAPTURE OUTGOING SIDECHANNEL /////
      if (_RequestSidechannelCfg.UnderlinePropertyIsProvided || _RequestSidechannelCfg.ChannelsToProvide.Any()) {
        var sideChannelContent = new Dictionary<string, string>();
        string sideChannelJson = null;

        _RequestSidechannelCfg.CaptureMethod.Invoke(method, sideChannelContent);

        foreach (string channel in _RequestSidechannelCfg.ChannelsToProvide) {
          if (channel == "_") {
            requestContent["_"] = sideChannelContent;
          }
          else { 
            if(requestHeaders == null) {
              requestHeaders = new Dictionary<string, string>();
              //will be done once, if we need in as json
              sideChannelJson = JsonConvert.SerializeObject(sideChannelContent);
            }
            requestHeaders[channel] = sideChannelJson;
          }
        }
      }
      ///// (end) CAPTURE OUTGOING SIDECHANNEL /////

      int paramIndex = 0;
      foreach (var param in parameters) {
        if (!UjmwAsyncProtocol.IsTransportedAsDtoProperty(param)) {
          //CANCELLATIONTOKEN or CALLBACK (never part of the request DTO)
        }
        else if (param.ParameterType.IsByRef) {
          if (param.IsOut) {
            //OUT

            //the response may omit an out value (for example a long-running operation which completed without setting it)
            //so we need a default for value types, because the emitted proxy cannot unbox null
            Type outType = param.ParameterType.GetElementType();
            if (arguments[paramIndex] == null && outType.IsValueType) {
              arguments[paramIndex] = Activator.CreateInstance(outType);
            }

          }
          else {
            //REF
            requestContent[param.Name] = arguments[paramIndex];
          }
        }
        else if (param.IsOptional) {
          bool optionalValueNotProvided = (arguments[paramIndex] != DBNull.Value);
          //OPT
          if (optionalValueNotProvided) {
            requestContent[param.Name] = arguments[paramIndex];
          }
        }
        else {
          requestContent[param.Name] = arguments[paramIndex];
        }
        paramIndex++;
      }

      var jss = new JsonSerializerSettings();
      jss.Formatting = Formatting.Indented;
      jss.DateFormatHandling = DateFormatHandling.IsoDateFormat;
      jss.ContractResolver = new CamelCasePropertyNamesContractResolver();
      string rawJsonRequest = JsonConvert.SerializeObject(requestContent, jss);

			// ############### HTTP POST #############################################
			IHttpRequestExecutor verbExecutor = _HttpPostExecutor as IHttpRequestExecutor;

			//transports without explicit verb support fall back to a synchronous POST (without 'wait')
			bool useAsyncProtocol = (isLongRunning && verbExecutor != null);

			string initiatingUrl = fullUrl;
			if (useAsyncProtocol) {
				initiatingUrl = fullUrl + "?" + UjmwAsyncProtocol._WaitQueryParam + "=" + UjmwClientConfiguration.LongRunningWaitSeconds.ToString(CultureInfo.InvariantCulture);
			}

			string rawJsonResponse;
			IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders;
			string reasonPhrase;

			httpReturnCode = -1; //in case of exceptions during call

			if (useAsyncProtocol) {
				httpReturnCode = verbExecutor.ExecuteHttpRequest(
					"POST", initiatingUrl,
					rawJsonRequest, requestHeaders,
					out rawJsonResponse, out responseHeaders,
					out reasonPhrase
				);
			}
			else {
				httpReturnCode = _HttpPostExecutor.ExecuteHttpPost(
					initiatingUrl,
					rawJsonRequest, requestHeaders,
					out rawJsonResponse, out responseHeaders,
					out reasonPhrase
				);
			}

			rawJsonResponse = this.EnsureSuccessfulResponse(initiatingUrl, method, rawJsonRequest, httpReturnCode, reasonPhrase, rawJsonResponse);

			// #######################################################################

			UjmwResponse response = this.ParseResponse(rawJsonResponse, responseHeaders, method, parameters, arguments, argumentNames);
			this.ReplayCallbackInvocations(response);

			if (useAsyncProtocol && !response.IsTerminal) {
				//NOTE: the 'httpReturnCode' stays at the success code of the initiating POST,
				//so that failures during polling never lead to a retry (which would initiate the operation again)
				response = this.PollUntilTerminated(fullUrl, method, parameters, arguments, argumentNames, cancellationToken, response);
			}

			return this.CompleteCall(fullUrl, method, cancellationToken, response);
		}

		/// <summary>
		/// Returns the value of the 'CancellationToken' argument (or 'CancellationToken.None' if the method has no such parameter).
		/// </summary>
		private CancellationToken GetCancellationTokenArgument(ParameterInfo[] parameters, object[] arguments) {

			for (int i = 0; i < parameters.Length; i++) {
				if (UjmwAsyncProtocol.IsCancellationTokenParameter(parameters[i]) && arguments[i] is CancellationToken) {
					return (CancellationToken)arguments[i];
				}
			}

			return CancellationToken.None;
		}

		/// <summary>
		/// Throws if the HTTP status code indicates no success and returns the (unwrapped) raw JSON response.
		/// </summary>
		private string EnsureSuccessfulResponse(
			string url, MethodInfo method, string rawJsonRequest, int httpReturnCode, string reasonPhrase, string rawJsonResponse
		) {

			// Informational responses (100 – 199)
			// Successful responses(200 – 299)
			// Redirection messages(300 – 399)
			// Client error responses(400 – 499)
			// Server error responses(500 – 599)
			if (httpReturnCode == 401 || httpReturnCode == 403) {
				throw new UnauthorizedAccessException($"Authorization issue! Received HTTP code {httpReturnCode} - {reasonPhrase} (URL: '{url}'). #{httpReturnCode}");
			}
			else if (httpReturnCode < 200 || httpReturnCode > 299) {

				if (UjmwClientConfiguration.RequestErrorDiagnosticHook != null) {
					UjmwClientConfiguration.RequestErrorDiagnosticHook.Invoke(
						url,
						method,
						rawJsonRequest,
						httpReturnCode,
						reasonPhrase,
						rawJsonResponse
					);
				}

				throw new Exception($"Response indicates no success! Received HTTP code {httpReturnCode} - '{reasonPhrase}' (URL: '{url}'). #{httpReturnCode}");
			}

			//some old technologies can only return XML-encapulated replies
			//this is an hack to support this
			if (rawJsonResponse.StartsWith("<UJMW>")) {
				rawJsonResponse = rawJsonResponse.Substring(6, rawJsonResponse.Length - 13);
			}

			return rawJsonResponse;
		}

		/// <summary>
		/// Parses a UJMW response. 'out'/'ref' values are written into the arguments immediately,
		/// everything else (return value, fault, async state, callback invocations, backchannel) is collected.
		/// </summary>
		private UjmwResponse ParseResponse(
			string rawJsonResponse, IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders,
			MethodInfo method, ParameterInfo[] parameters, object[] arguments, string[] argumentNames
		) {

			UjmwResponse response = new UjmwResponse();
			response.ResponseHeaders = responseHeaders;

			JsonSerializer objectDeserializer = new JsonSerializer();

			using (StringReader sr = new StringReader(rawJsonResponse)) {
				using (JsonTextReader jr = new JsonTextReader(sr)) {

					jr.Read();
					if (jr.TokenType != JsonToken.StartObject) {
						throw new Exception("Response is no valid JSON: " + rawJsonResponse);
					}

					while (jr.Read()) {

						if (jr.TokenType != JsonToken.PropertyName) {
							continue;
						}

						string currentPropName = jr.Value.ToString();

						if (currentPropName == "_") {
							jr.Read();
							response.BackChannelContent = objectDeserializer.Deserialize<Dictionary<string, string>>(jr);
						}
						else if (currentPropName.Equals("return", StringComparison.InvariantCultureIgnoreCase)) {
							jr.Read();
							if (method.ReturnType != typeof(void)) {
								response.ReturnValue = objectDeserializer.Deserialize(jr, method.ReturnType);
							}
							else {
								jr.Skip();
							}
						}
						else if (currentPropName.Equals("fault", StringComparison.InvariantCultureIgnoreCase)) {
							response.FaultMessage = jr.ReadAsString();
						}
						else if (currentPropName.Equals(UjmwAsyncProtocol._AsyncPropertyName, StringComparison.InvariantCultureIgnoreCase)) {
							jr.Read();
							JToken asyncToken = JToken.ReadFrom(jr);
							this.ApplyAsyncState(response, asyncToken);
						}
						else {

							jr.Read();

							ParameterInfo param = parameters.Where((p) => p.Name.Equals(currentPropName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
							if (param != null && param.ParameterType.IsByRef) {

								Type typeToDeserialize = param.ParameterType.GetElementType();
								object value = objectDeserializer.Deserialize(jr, typeToDeserialize);

								int argIndex = Array.IndexOf(argumentNames, param.Name);
								arguments[argIndex] = value;

							}
							else {

								int callbackIndex = this.FindCallbackParameterIndex(parameters, currentPropName);
								if (callbackIndex >= 0) {
									JToken invokesToken = JToken.ReadFrom(jr);
									this.CollectCallbackInvocations(response, parameters[callbackIndex], arguments[callbackIndex] as Delegate, invokesToken);
								}
								else {
									jr.Skip();
								}

							}

						}

					}

				}
			}

			return response;
		}

		/// <summary>
		/// Returns the index of the callback parameter whose invocations are transported in the given property (or -1).
		/// </summary>
		private int FindCallbackParameterIndex(ParameterInfo[] parameters, string propertyName) {

			for (int i = 0; i < parameters.Length; i++) {
				if (UjmwAsyncProtocol.IsCallbackParameter(parameters[i])) {
					string invokesPropertyName = UjmwAsyncProtocol.GetCallbackInvokesPropertyName(parameters[i].Name);
					if (invokesPropertyName.Equals(propertyName, StringComparison.InvariantCultureIgnoreCase)) {
						return i;
					}
				}
			}

			return -1;
		}

		/// <summary>
		/// Reads the 'async' object of a response.
		/// </summary>
		private void ApplyAsyncState(UjmwResponse response, JToken asyncToken) {

			JObject asyncObject = asyncToken as JObject;
			if (asyncObject == null) {
				return;
			}

			response.HasAsyncState = true;

			JToken asyncIdToken = asyncObject[UjmwAsyncProtocol._AsyncIdPropertyName];
			if (asyncIdToken != null && asyncIdToken.Type != JTokenType.Null) {
				response.AsyncId = asyncIdToken.ToString();
			}

			JToken execStateToken = asyncObject[UjmwAsyncProtocol._ExecStatePropertyName];
			if (execStateToken != null && execStateToken.Type != JTokenType.Null) {
				response.ExecState = execStateToken.Value<int>();
			}

			JToken pollingSecretToken = asyncObject[UjmwAsyncProtocol._PollingSecretPropertyName];
			if (pollingSecretToken != null && pollingSecretToken.Type == JTokenType.String) {
				response.PollingSecret = pollingSecretToken.Value<string>();
			}

			JToken pollNbfToken = asyncObject[UjmwAsyncProtocol._PollNbfPropertyName];
			if (pollNbfToken != null) {

				if (pollNbfToken.Type == JTokenType.Date) {
					response.PollNbf = pollNbfToken.Value<DateTime>().ToUniversalTime();
				}
				else if (pollNbfToken.Type == JTokenType.String) {

					DateTime parsedNbf;
					bool parsed = DateTime.TryParse(
						pollNbfToken.Value<string>(), CultureInfo.InvariantCulture,
						DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsedNbf
					);

					if (parsed) {
						response.PollNbf = parsedNbf;
					}

				}

			}

		}

		/// <summary>
		/// Collects the invocations of a callback parameter (an array of '{ "#": n, "arg1": ..., "argN": ... }' entries).
		/// </summary>
		private void CollectCallbackInvocations(UjmwResponse response, ParameterInfo callbackParam, Delegate callback, JToken invokesToken) {

			JArray invokes = invokesToken as JArray;
			if (invokes == null || callback == null) {
				return;
			}

			Type[] argumentTypes = UjmwAsyncProtocol.GetCallbackArgumentTypes(callbackParam.ParameterType);

			foreach (JToken invokeToken in invokes) {

				JObject invoke = invokeToken as JObject;
				if (invoke == null) {
					continue;
				}

				long sequence = invoke.Value<long>(UjmwAsyncProtocol._CallbackSequencePropertyName);

				object[] callbackArguments = new object[argumentTypes.Length];
				for (int i = 0; i < argumentTypes.Length; i++) {

					JToken argumentToken = invoke[UjmwAsyncProtocol.GetCallbackArgumentName(i + 1)];
					if (argumentToken != null) {
						callbackArguments[i] = argumentToken.ToObject(argumentTypes[i]);
					}
					else if (argumentTypes[i].IsValueType) {
						callbackArguments[i] = Activator.CreateInstance(argumentTypes[i]);
					}

				}

				response.CallbackInvocations.Add(new CallbackInvocation(sequence, callback, callbackArguments));
			}

		}

		/// <summary>
		/// Replays all collected callback invocations of a response strictly in ascending '#' order (across all callback parameters).
		/// </summary>
		private void ReplayCallbackInvocations(UjmwResponse response) {

			List<CallbackInvocation> invocations = response.CallbackInvocations;
			invocations.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

			foreach (CallbackInvocation invocation in invocations) {
				try {
					invocation.Callback.DynamicInvoke(invocation.Arguments);
				}
				catch (TargetInvocationException ex) {
					//unwrap the exception of the callers delegate
					ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
					throw;
				}
			}

			invocations.Clear();
		}

		/// <summary>
		/// Internal polling loop for a long-running operation (GET, respecting the 'pollNbf').
		/// When the callers 'CancellationToken' is cancelled, one DELETE request is sent and the polling continues until the operation has terminated.
		/// </summary>
		private UjmwResponse PollUntilTerminated(
			string operationUrl, MethodInfo method, ParameterInfo[] parameters, object[] arguments, string[] argumentNames,
			CancellationToken cancellationToken, UjmwResponse initiatingResponse
		) {

			string asyncId = initiatingResponse.AsyncId;
			string pollingSecret = initiatingResponse.PollingSecret;

			if (string.IsNullOrEmpty(asyncId) || string.IsNullOrEmpty(pollingSecret)) {
				throw new Exception($"The response of the long-running operation '{method.Name}' indicates a non-terminated operation, but provides no 'asyncId' or 'pollingSecret' (URL: '{operationUrl}')!");
			}

			IHttpRequestExecutor verbExecutor = (IHttpRequestExecutor)_HttpPostExecutor;
			UjmwResponse response = initiatingResponse;
			bool cancellationSent = false;

			while (!response.IsTerminal) {

				string httpMethod = "GET";

				if (!cancellationSent && cancellationToken.IsCancellationRequested) {
					httpMethod = "DELETE";
					cancellationSent = true;
				}
				else {

					//after the cancellation was sent, the sleep must not be shortened by the (already cancelled) token anymore
					CancellationToken sleepToken = cancellationToken;
					if (cancellationSent) {
						sleepToken = CancellationToken.None;
					}

					UjmwClientConfiguration.PollSleeper.Invoke(this.GetPollDelay(response), sleepToken);

					if (!cancellationSent && cancellationToken.IsCancellationRequested) {
						continue;
					}

				}

				string pollUrl = this.BuildPollingUrl(operationUrl, asyncId, pollingSecret, (httpMethod == "DELETE"));

				string rawJsonResponse;
				IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders;
				string reasonPhrase;

				int httpCode = verbExecutor.ExecuteHttpRequest(
					httpMethod, pollUrl,
					null, null,
					out rawJsonResponse, out responseHeaders,
					out reasonPhrase
				);

				rawJsonResponse = this.EnsureSuccessfulResponse(pollUrl, method, null, httpCode, reasonPhrase, rawJsonResponse);

				response = this.ParseResponse(rawJsonResponse, responseHeaders, method, parameters, arguments, argumentNames);
				this.ReplayCallbackInvocations(response);

			}

			return response;
		}

		/// <summary>
		/// Returns the time to wait until the next poll (based on the 'pollNbf' of the last response).
		/// </summary>
		private TimeSpan GetPollDelay(UjmwResponse response) {

			int fixedPollIntervalMs = _FixedPollIntervalMs;
			if (fixedPollIntervalMs >= 0) {
				//'OverrideNbfPolling' was used
				return TimeSpan.FromMilliseconds(fixedPollIntervalMs);
			}

			if (response.PollNbf == DateTime.MinValue) {
				//fallback if the server did not provide a 'pollNbf'
				return TimeSpan.FromSeconds(1);
			}

			TimeSpan delay = response.PollNbf - DateTime.UtcNow;
			if (delay < TimeSpan.Zero) {
				return TimeSpan.Zero;
			}

			return delay;
		}

		/// <summary>
		/// Builds the URL for a GET (poll) or DELETE (cancellation) request including a fresh 'pollingSalt' and the 'pollingHash'.
		/// </summary>
		private string BuildPollingUrl(string operationUrl, string asyncId, string pollingSecret, bool includeWait) {

			string pollingSalt = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSaltLength);
			string pollingHash = UjmwAsyncProtocol.ComputePollingHash(pollingSecret, pollingSalt);

			string url = (
				operationUrl +
				"?" + UjmwAsyncProtocol._AsyncIdQueryParam + "=" + Uri.EscapeDataString(asyncId) +
				"&" + UjmwAsyncProtocol._PollingSaltQueryParam + "=" + pollingSalt +
				"&" + UjmwAsyncProtocol._PollingHashQueryParam + "=" + pollingHash
			);

			if (includeWait) {
				url = url + "&" + UjmwAsyncProtocol._WaitQueryParam + "=" + UjmwClientConfiguration.LongRunningWaitSeconds.ToString(CultureInfo.InvariantCulture);
			}

			return url;
		}

		/// <summary>
		/// Evaluates the terminal response: cancellation, fault, backchannel and return value.
		/// </summary>
		private object CompleteCall(string fullUrl, MethodInfo method, CancellationToken cancellationToken, UjmwResponse response) {

			if (response.HasAsyncState && response.ExecState == UjmwAsyncProtocol._ExecStateCancelled) {
				throw new OperationCanceledException($"The long-running operation '{method.Name}' has been cancelled (URL: '{fullUrl}').", cancellationToken);
			}

			if (!String.IsNullOrWhiteSpace(response.FaultMessage)) {
				UjmwClientConfiguration.FaultRepsonseHandler.Invoke(fullUrl, method, response.FaultMessage);
			}

			IDictionary<string, string> backChannelContent = response.BackChannelContent;
			IEnumerable<KeyValuePair<string, IEnumerable<string>>> responseHeaders = response.ResponseHeaders;

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
					else if (responseHeaders != null) {
						var hdr = responseHeaders.Where((h) => h.Key == channelName);
						if (hdr.Any()) {
							//will be done once, if we need in as json
							string sideChannelFromHeader = hdr.First().Value.ToString();
							backChannelContent = JsonConvert.DeserializeObject<Dictionary<string, string>>(sideChannelFromHeader);
							////// PROCESS //////
							_ResponseSidechannelCfg.ProcessingMethod.Invoke(method, backChannelContent);
							backChannelreceived = true;
							break;
						}
					}
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

			return response.ReturnValue;
		}

		#region " Nested Types "

		/// <summary>
		/// A buffered callback invocation, which has to be replayed on the client side.
		/// </summary>
		private class CallbackInvocation {

			private long _Sequence;
			private Delegate _Callback;
			private object[] _Arguments;

			public CallbackInvocation(long sequence, Delegate callback, object[] arguments) {
				_Sequence = sequence;
				_Callback = callback;
				_Arguments = arguments;
			}

			public long Sequence {
				get {
					return _Sequence;
				}
			}

			public Delegate Callback {
				get {
					return _Callback;
				}
			}

			public object[] Arguments {
				get {
					return _Arguments;
				}
			}

		}

		/// <summary>
		/// The parsed content of a UJMW response.
		/// </summary>
		private class UjmwResponse {

			private object _ReturnValue = null;
			private string _FaultMessage = null;
			private IDictionary<string, string> _BackChannelContent = null;
			private IEnumerable<KeyValuePair<string, IEnumerable<string>>> _ResponseHeaders = null;
			private bool _HasAsyncState = false;
			private string _AsyncId = null;
			private int _ExecState = UjmwAsyncProtocol._ExecStateRunning;
			private DateTime _PollNbf = DateTime.MinValue;
			private string _PollingSecret = null;
			private List<CallbackInvocation> _CallbackInvocations = new List<CallbackInvocation>();

			public object ReturnValue {
				get {
					return _ReturnValue;
				}
				set {
					_ReturnValue = value;
				}
			}

			public string FaultMessage {
				get {
					return _FaultMessage;
				}
				set {
					_FaultMessage = value;
				}
			}

			public IDictionary<string, string> BackChannelContent {
				get {
					return _BackChannelContent;
				}
				set {
					_BackChannelContent = value;
				}
			}

			public IEnumerable<KeyValuePair<string, IEnumerable<string>>> ResponseHeaders {
				get {
					return _ResponseHeaders;
				}
				set {
					_ResponseHeaders = value;
				}
			}

			/// <summary>
			/// False, if the response contains no 'async' object (synchronous execution or server without async support).
			/// </summary>
			public bool HasAsyncState {
				get {
					return _HasAsyncState;
				}
				set {
					_HasAsyncState = value;
				}
			}

			public string AsyncId {
				get {
					return _AsyncId;
				}
				set {
					_AsyncId = value;
				}
			}

			public int ExecState {
				get {
					return _ExecState;
				}
				set {
					_ExecState = value;
				}
			}

			/// <summary>
			/// UTC; 'DateTime.MinValue' if not provided.
			/// </summary>
			public DateTime PollNbf {
				get {
					return _PollNbf;
				}
				set {
					_PollNbf = value;
				}
			}

			public string PollingSecret {
				get {
					return _PollingSecret;
				}
				set {
					_PollingSecret = value;
				}
			}

			public List<CallbackInvocation> CallbackInvocations {
				get {
					return _CallbackInvocations;
				}
			}

			/// <summary>
			/// True, if there is nothing left to poll (no 'async' object or a terminal 'execState').
			/// </summary>
			public bool IsTerminal {
				get {

					if (!_HasAsyncState) {
						return true;
					}

					return UjmwAsyncProtocol.IsTerminalExecState(_ExecState);
				}
			}

		}

		#endregion

	}

}
