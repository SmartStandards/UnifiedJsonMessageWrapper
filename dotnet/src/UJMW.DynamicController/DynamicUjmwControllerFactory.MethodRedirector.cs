using Logging.SmartStandards;
using Logging.SmartStandards.CopyForUJMW;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SmartStandards;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

namespace System.Web.UJMW {

  partial class DynamicUjmwControllerFactory {

    partial class DynamicControllerBase<TServiceInterface> {

      /// <summary>
      /// Maps a request DTO onto a contract method invocation (and its results back onto a response DTO).
      /// Supports the synchronous execution as well as the execution of long-running operations
      /// via the operation store (see 'doc/epics/epic-long-running-operations.md').
      /// </summary>
      private class MethodRedirector {

        private readonly Type _ContractType;
        private readonly MethodInfo _ContractMethod;
        private readonly Type _ResponseDtoType;
        private readonly int _ParamCount;

        private readonly List<DtoValueMapper> _RequestDtoValueMappers = new List<DtoValueMapper>();
        private readonly List<DtoValueMapper> _ResponseDtoValueMappers = new List<DtoValueMapper>();

        private readonly int _CancellationTokenIndex = -1;
        private readonly List<CallbackBinding> _CallbackBindings = new List<CallbackBinding>();

        private readonly PropertyInfo _ReturnProp;
        private readonly PropertyInfo _FaultProp;
        private readonly PropertyInfo _AsyncProp;
        private readonly PropertyInfo _RequestSidechannelProp;
        private readonly PropertyInfo _ResponseSidechannelProp;

        private readonly IncommingRequestSideChannelConfiguration _InboundSideChannelCfg;
        private readonly OutgoingResponseSideChannelConfiguration _OutboundSideChannelCfg;

        /// <summary>
        /// Creates the (expensive) mapping information for one method of a dynamic controller.
        /// </summary>
        public MethodRedirector(Type controllerType, string methodName) {

          _ContractType = typeof(TServiceInterface);

          MethodInfo contractMethod;
          if (!DynamicUjmwControllerFactory.TryGetContractMethod(_ContractType, methodName, out contractMethod)) {
            throw new Exception($"Cannot find a Method named '{methodName}' on Contract Interface '{_ContractType.FullName}'!");
          }
          _ContractMethod = contractMethod;

          MethodInfo controllerMethod = controllerType.GetMethod(contractMethod.GetNameOrOverride(true));
          if (controllerMethod == null) {
            controllerMethod = controllerType.GetMethod(methodName);
          }

          //the first parameter is always the request DTO (long-running operations have an additional 'wait' parameter)
          Type requestDtoType = controllerMethod.GetParameters()[0].ParameterType;
          _ResponseDtoType = controllerMethod.ReturnType;

          ParameterInfo[] serviceMethodParams = contractMethod.GetParameters();
          _ParamCount = serviceMethodParams.Length;

          for (int idx = 0; idx < _ParamCount; idx++) {

            ParameterInfo param = serviceMethodParams[idx];

            if (UjmwAsyncProtocol.IsCancellationTokenParameter(param)) {
              _CancellationTokenIndex = idx;
            }
            else if (UjmwAsyncProtocol.IsCallbackParameter(param)) {
              _CallbackBindings.Add(new CallbackBinding(param, idx, _ResponseDtoType));
            }
            else {

              if (!param.IsOut) {
                _RequestDtoValueMappers.Add(new DtoValueMapper(requestDtoType, _ResponseDtoType, param.Name, idx));
              }

              if (param.IsOut || param.ParameterType.IsByRef) {
                _ResponseDtoValueMappers.Add(new DtoValueMapper(requestDtoType, _ResponseDtoType, param.Name, idx));
              }

            }

          }

          if (contractMethod.ReturnType != null && contractMethod.ReturnType != typeof(void)) {
            _ReturnProp = _ResponseDtoType.GetProperty(UjmwReturnPropertyName);
          }

          _FaultProp = _ResponseDtoType.GetProperty(UjmwFaultPropertyName);
          _AsyncProp = _ResponseDtoType.GetProperty(UjmwAsyncProtocol._AsyncPropertyName);

          _RequestSidechannelProp = requestDtoType.GetProperty(UjmwSideChannelPropertyName);
          _ResponseSidechannelProp = _ResponseDtoType.GetProperty(UjmwSideChannelPropertyName);

          _InboundSideChannelCfg = UjmwHostConfiguration.GetRequestSideChannelConfiguration(_ContractType);
          _OutboundSideChannelCfg = UjmwHostConfiguration.GetResponseSideChannelConfiguration(_ContractType);

        }

        #region " Synchronous Execution "

        /// <summary>
        /// Executes the method synchronously within the current request (classic UJMW behaviour).
        /// Callback invocations are collected and delivered all together within the response.
        /// </summary>
        public object InvokeSynchronous(
          Func<TServiceInterface> svcInstanceGetter, object requestDto,
          IHeaderDictionary requestHeaders, IHeaderDictionary responseHeaders,
          DynamicUjmwControllerOptions options, ContextualArgumentCollectorDelegate contextualArgumentCollector,
          CancellationToken requestAborted
        ) {

          object responseDto = Activator.CreateInstance(_ResponseDtoType);
          UjmwSynchronousCallbackRecorder callbackRecorder = new UjmwSynchronousCallbackRecorder();

          try {

            object[] serviceMethodParams = this.MapRequest(requestDto);

            IDictionary<string, string> dummySideChannelContent;
            this.RestoreInboundSideChannel(requestDto, requestHeaders, contextualArgumentCollector, out dummySideChannelContent);

            IDictionary<string, string> contextualArguments = this.CollectContextualArguments(options, contextualArgumentCollector, requestDto);

            this.Execute(
              svcInstanceGetter, serviceMethodParams, responseDto, options, contextualArguments, requestAborted, callbackRecorder
            );

          }
          catch (Exception ex) {
            this.ApplyFault(responseDto, ex);
          }

          this.WriteCallbackInvocations(responseDto, callbackRecorder.GetInvocations());

          Dictionary<string, string> backChannelContent = this.CaptureOutgoingBackChannel();
          this.WriteOutgoingBackChannel(responseDto, responseHeaders, backChannelContent);

          return responseDto;
        }

        #endregion

        #region " Async Execution (Long-Running) "

        /// <summary>
        /// Hands the execution over to the operation store and waits up to the given time for its termination.
        /// Everything which depends on the current HTTP request (DTO mapping, inbound sidechannel, contextual arguments)
        /// is evaluated here, the ambient context is restored again on the background thread.
        /// </summary>
        public object InitiateAsync(
          string operationScope, Func<TServiceInterface> svcInstanceGetter, object requestDto,
          IHeaderDictionary requestHeaders, IHeaderDictionary responseHeaders,
          DynamicUjmwControllerOptions options, ContextualArgumentCollectorDelegate contextualArgumentCollector,
          TimeSpan wait
        ) {

          object[] serviceMethodParams;
          IDictionary<string, string> sideChannelSnapshot;
          IDictionary<string, string> contextualArguments;

          try {

            serviceMethodParams = this.MapRequest(requestDto);

            bool sideChannelProcessed = this.RestoreInboundSideChannel(
              requestDto, requestHeaders, contextualArgumentCollector, out sideChannelSnapshot
            );
            if (!sideChannelProcessed) {
              sideChannelSnapshot = null;
            }

            contextualArguments = this.CollectContextualArguments(options, contextualArgumentCollector, requestDto);

          }
          catch (Exception ex) {
            object faultResponseDto = Activator.CreateInstance(_ResponseDtoType);
            this.ApplyFault(faultResponseDto, ex);
            return faultResponseDto;
          }

          UjmwOperationBody body = (CancellationToken cancellationToken, IUjmwCallbackRecorder callbackRecorder) => {

            //the ambient context of the initiating request is restored before the service method is invoked
            if (sideChannelSnapshot != null) {
              _InboundSideChannelCfg.ProcessingMethod.Invoke(_ContractMethod, sideChannelSnapshot);
            }

            object responseDto = Activator.CreateInstance(_ResponseDtoType);
            bool failed = false;

            try {
              this.Execute(
                svcInstanceGetter, serviceMethodParams, responseDto, options, contextualArguments, cancellationToken, callbackRecorder
              );
            }
            catch (Exception ex) {

              OperationCanceledException cancellation = FindCancellation(ex);
              if (cancellation != null && cancellationToken.IsCancellationRequested) {
                throw cancellation;
              }

              this.ApplyFault(responseDto, ex);
              failed = true;

            }

            Dictionary<string, string> backChannelContent = this.CaptureOutgoingBackChannel();

            return new UjmwOperationOutcome(new AsyncResult(responseDto, backChannelContent), failed);
          };

          UjmwOperationSnapshot snapshot = UjmwHostConfiguration.OperationStore.Initiate(operationScope, body, wait);

          return this.CreateResponseFromSnapshot(snapshot, responseHeaders);
        }

        /// <summary>
        /// Creates the response DTO (identical for POST, GET and DELETE) from a snapshot of the operation store.
        /// </summary>
        public object CreateResponseFromSnapshot(UjmwOperationSnapshot snapshot, IHeaderDictionary responseHeaders) {

          object responseDto = null;
          Dictionary<string, string> backChannelContent = null;

          if (snapshot.Outcome != null) {

            AsyncResult result = snapshot.Outcome.Payload as AsyncResult;
            if (result != null) {
              responseDto = result.ResponseDto;
              backChannelContent = result.BackChannelContent;
            }

            if (responseDto == null && snapshot.Outcome.Failed) {
              responseDto = Activator.CreateInstance(_ResponseDtoType);
              this.ApplyFault(responseDto, new Exception("The long-running operation failed unexpectedly!"));
            }

          }

          if (responseDto == null) {
            responseDto = Activator.CreateInstance(_ResponseDtoType);
          }

          if (_AsyncProp != null) {

            UjmwAsyncState asyncState = new UjmwAsyncState();
            asyncState.ExecState = snapshot.ExecState;
            asyncState.Cancellable = snapshot.Cancellable;
            asyncState.PollingSecret = snapshot.PollingSecret;

            if (snapshot.AsyncId > 0) {
              asyncState.AsyncId = snapshot.AsyncId.ToString();
            }

            if (snapshot.PollNbf > DateTime.MinValue) {
              asyncState.PollNbf = DateTime.SpecifyKind(snapshot.PollNbf, DateTimeKind.Utc);
            }

            _AsyncProp.SetValue(responseDto, asyncState);

          }

          this.WriteCallbackInvocations(responseDto, snapshot.CallbackInvocations);

          if (backChannelContent != null) {
            this.WriteOutgoingBackChannel(responseDto, responseHeaders, backChannelContent);
          }

          return responseDto;
        }

        /// <summary>
        /// Creates a response DTO which only carries the given fault message.
        /// </summary>
        public object CreateFaultResponse(string faultMessage) {

          object responseDto = Activator.CreateInstance(_ResponseDtoType);

          if (_FaultProp != null) {
            _FaultProp.SetValue(responseDto, faultMessage);
          }

          return responseDto;
        }

        /// <summary>
        /// Returns the 'OperationCanceledException' if the given exception is or wraps one (otherwise null).
        /// </summary>
        private static OperationCanceledException FindCancellation(Exception ex) {

          Exception current = ex;

          while (current != null) {

            OperationCanceledException cancellation = current as OperationCanceledException;
            if (cancellation != null) {
              return cancellation;
            }

            current = current.InnerException;

          }

          return null;
        }

        #endregion

        #region " Mapping & Execution "

        /// <summary>
        /// Maps the request DTO onto the argument array of the contract method.
        /// </summary>
        private object[] MapRequest(object requestDto) {

          if (requestDto == null) {
            throw new Exception($"The request DTO is null or could not be deserialized!");
          }

          object[] serviceMethodParams = new object[_ParamCount];

          foreach (DtoValueMapper requestDtoValueMapper in _RequestDtoValueMappers) {
            requestDtoValueMapper.MapRequestDtoToParam(requestDto, serviceMethodParams);
          }

          return serviceMethodParams;
        }

        /// <summary>
        /// Collects the contextual arguments for the contextualization hook (must be evaluated within the request).
        /// </summary>
        private IDictionary<string, string> CollectContextualArguments(
          DynamicUjmwControllerOptions options, ContextualArgumentCollectorDelegate contextualArgumentCollector, object requestDto
        ) {

          if (options.ContextualizationHook == null) {
            return null;
          }

          IDictionary<string, string> contextualArguments = new Dictionary<string, string>();
          contextualArgumentCollector.Invoke(ref contextualArguments, Array.Empty<string>(), requestDto);

          return contextualArguments;
        }

        /// <summary>
        /// Invokes the contract method (routed over the contextualization hook, if configured) and maps
        /// the out-/ref-args and the return value onto the response DTO.
        /// </summary>
        private void Execute(
          Func<TServiceInterface> svcInstanceGetter, object[] serviceMethodParams, object responseDto,
          DynamicUjmwControllerOptions options, IDictionary<string, string> contextualArguments,
          CancellationToken cancellationToken, IUjmwCallbackRecorder callbackRecorder
        ) {

          if (_CancellationTokenIndex >= 0) {
            serviceMethodParams[_CancellationTokenIndex] = cancellationToken;
          }

          foreach (CallbackBinding callbackBinding in _CallbackBindings) {
            serviceMethodParams[callbackBinding.ParameterIndex] = callbackBinding.CreateDelegate(callbackRecorder);
          }

          if (UjmwHostConfiguration.EnableTraceMessagesForEveryIncommingRequest) {
            DevLogger.LogTrace(0, 72000, $"Invoking UJMW call to UJMW Operation '{_ContractMethod.Name}'");
          }

          bool innerInvokeWasCalled = false;

          //this encapsulation is only to support contextualization hooks exactly arround this closure...
          Action innerInvokeContextual = () => {

            innerInvokeWasCalled = true;

            TServiceInterface svc = svcInstanceGetter.Invoke();

            if (UjmwHostConfiguration.ArgumentPreEvaluator != null) {
              try {
                UjmwHostConfiguration.ArgumentPreEvaluator.Invoke(_ContractType, _ContractMethod, serviceMethodParams);
              }
              catch (TargetInvocationException ex) {
                throw new ApplicationException($"ArgumentPreEvaluator for '{_ContractMethod.Name}' has thrown an Exception: " + ex.InnerException.Message + " #72001", ex.InnerException);
              }
              catch (Exception ex) {
                throw new ApplicationException($"ArgumentPreEvaluator for '{_ContractMethod.Name}' has thrown an Exception: " + ex.Message + " #72001", ex);
              }
            }

            //invoke the service method
            object returnVal;
            try {
              returnVal = _ContractMethod.Invoke(svc, serviceMethodParams);
            }
            catch (TargetInvocationException ex) {
              throw new ApplicationException($"BL-Method '{_ContractMethod.Name}' has thrown an Exception: " + ex.InnerException.Message + " #72000", ex.InnerException);
            }
            catch (Exception ex) {
              throw new ApplicationException($"BL-Method '{_ContractMethod.Name}' has thrown an Exception: " + ex.Message + " #72000", ex);
            }

            //map the out-/ref-args
            foreach (DtoValueMapper responseDtoValueMapper in _ResponseDtoValueMappers) {
              responseDtoValueMapper.MapParamToResponseDto(serviceMethodParams, responseDto);
            }

            //map the return value
            if (_ReturnProp != null) {
              _ReturnProp.SetValue(responseDto, returnVal);
            }

          };

          //routing over contextualization hook around the internal invoke...
          if (options.ContextualizationHook != null) {

            options.ContextualizationHook.Invoke(contextualArguments, innerInvokeContextual);

            if (!innerInvokeWasCalled) {
              throw new Exception("The UJMW ContextualizationHook MUST NOT skip invoking the given inner action!");
            }

          }
          else {
            innerInvokeContextual.Invoke();
          }

        }

        /// <summary>
        /// Logs the exception and writes it into the 'fault' property.
        /// </summary>
        private void ApplyFault(object responseDto, Exception ex) {

          DevLogger.LogError(ex);

          if (_FaultProp == null) {
            return;
          }

          if (UjmwHostConfiguration.HideExeptionMessageInFaultProperty) {
            _FaultProp.SetValue(responseDto, "UJMW Invocation Error");
          }
          else {
            _FaultProp.SetValue(responseDto, ex.Message);
          }

        }

        /// <summary>
        /// Writes the callback invocations as '&lt;callbackName&gt;Invokes' arrays of { '#': n, 'arg1': ..., 'argN': ... }.
        /// </summary>
        private void WriteCallbackInvocations(object responseDto, UjmwCallbackInvocation[] invocations) {

          foreach (CallbackBinding callbackBinding in _CallbackBindings) {

            if (callbackBinding.InvokesProp == null) {
              continue;
            }

            Dictionary<string, object>[] entries = invocations.Where(
              (invocation) => invocation.CallbackName == callbackBinding.CallbackName
            ).OrderBy(
              (invocation) => invocation.Sequence
            ).Select(
              (invocation) => CreateInvocationEntry(invocation)
            ).ToArray();

            if (entries.Length > 0) {
              callbackBinding.InvokesProp.SetValue(responseDto, entries);
            }

          }

        }

        /// <summary>
        /// Creates the wire representation of a single callback invocation.
        /// </summary>
        private static Dictionary<string, object> CreateInvocationEntry(UjmwCallbackInvocation invocation) {

          Dictionary<string, object> entry = new Dictionary<string, object>();
          entry[UjmwAsyncProtocol._CallbackSequencePropertyName] = invocation.Sequence;

          for (int i = 0; i < invocation.Arguments.Length; i++) {
            entry[UjmwAsyncProtocol.GetCallbackArgumentName(i + 1)] = invocation.Arguments[i];
          }

          return entry;
        }

        #endregion

        #region " Sidechannel "

        /// <summary>
        /// Restores the incomming sidechannel into the ambient context. Returns true, if the processing method was invoked
        /// (the content which was passed to it is returned, so that it can be applied again on a background thread).
        /// </summary>
        private bool RestoreInboundSideChannel(
          object requestDto, IHeaderDictionary requestHeaders,
          ContextualArgumentCollectorDelegate contextualArgumentCollector,
          out IDictionary<string, string> processedContent
        ) {

          processedContent = null;

          foreach (string acceptedChannel in _InboundSideChannelCfg.AcceptedChannels) {
            if (acceptedChannel == "_") {
              if (_RequestSidechannelProp != null) {
                IDictionary<string, string> container = (IDictionary<string, string>)_RequestSidechannelProp.GetValue(requestDto);
                if (container != null) {
                  //overlay the endpoint-individual 'contextualArgument's
                  contextualArgumentCollector.Invoke(ref container, _InboundSideChannelCfg.ContextualArgumentsToOverlay, requestDto);
                  //now invoke the propagation into the ambience-room
                  _InboundSideChannelCfg.ProcessingMethod.Invoke(_ContractMethod, container);
                  processedContent = container;
                  return true;
                }
              }
            }
            else { //lets look into the http header
              if (requestHeaders.TryGetValue(acceptedChannel, out string rawSideChannelContent)) {
                IDictionary<string, string> sideChannelContent = JsonConvert.DeserializeObject<Dictionary<string, string>>(rawSideChannelContent);
                //overlay the endpoint-individual 'contextualArgument's
                contextualArgumentCollector.Invoke(ref sideChannelContent, _InboundSideChannelCfg.ContextualArgumentsToOverlay, requestDto);
                //now invoke the propagation into the ambience-room
                _InboundSideChannelCfg.ProcessingMethod.Invoke(_ContractMethod, sideChannelContent);
                processedContent = sideChannelContent;
                return true;
              }
            }
          }

          if (_InboundSideChannelCfg.AcceptedChannels.Length == 0) {
            return false;
          }

          if (!_InboundSideChannelCfg.SkipAllowed) {
            string msg = "Rejected incomming request because of missing side channel";
            DevLogger.LogWarning(0, 72003, msg);
            throw new Exception(msg);
          }

          //if the whole getter is null, then (and only in this case) it will be a 'silent skip'
          if (_InboundSideChannelCfg.DefaultsGetterOnSkip == null) {
            return false;
          }

          IDictionary<string, string> defaultContent = new Dictionary<string, string>();
          //also null (when the DefaultsGetterOnSkip sets the ref handle to null) can be passed to the processing method...
          _InboundSideChannelCfg.DefaultsGetterOnSkip.Invoke(ref defaultContent);
          //overlay the endpoint-individual 'contextualArgument's
          contextualArgumentCollector.Invoke(ref defaultContent, _InboundSideChannelCfg.ContextualArgumentsToOverlay, requestDto);
          //now invoke the propagation into the ambience-room
          _InboundSideChannelCfg.ProcessingMethod.Invoke(_ContractMethod, defaultContent);
          processedContent = defaultContent;

          return true;
        }

        /// <summary>
        /// Captures the outgoing backchannel from the current ambient context (null, if no channel is provided).
        /// </summary>
        private Dictionary<string, string> CaptureOutgoingBackChannel() {

          if (!_OutboundSideChannelCfg.ChannelsToProvide.Any()) {
            return null;
          }

          Dictionary<string, string> backChannelContainer = new Dictionary<string, string>();
          _OutboundSideChannelCfg.CaptureMethod.Invoke(_ContractMethod, backChannelContainer);

          return backChannelContainer;
        }

        /// <summary>
        /// Writes the captured backchannel into the configured channels ('_' property and/or http headers).
        /// </summary>
        private void WriteOutgoingBackChannel(object responseDto, IHeaderDictionary responseHeaders, Dictionary<string, string> backChannelContainer) {

          if (backChannelContainer == null) {
            return;
          }

          string serializedSnapshot = null;

          foreach (string channelName in _OutboundSideChannelCfg.ChannelsToProvide) {
            if (channelName == "_") {
              if (_ResponseSidechannelProp != null) {
                _ResponseSidechannelProp.SetValue(responseDto, backChannelContainer);
              }
            }
            else {
              if (serializedSnapshot == null) { //on-demand, but bufferred...
                serializedSnapshot = JsonConvert.SerializeObject(backChannelContainer);
              }
              responseHeaders[channelName] = serializedSnapshot;
            }
          }

        }

        #endregion

        /// <summary>
        /// Payload of an operation outcome: the response DTO and the backchannel captured on the background thread.
        /// </summary>
        private class AsyncResult {

          private readonly object _ResponseDto;
          private readonly Dictionary<string, string> _BackChannelContent;

          public AsyncResult(object responseDto, Dictionary<string, string> backChannelContent) {
            _ResponseDto = responseDto;
            _BackChannelContent = backChannelContent;
          }

          public object ResponseDto {
            get {
              return _ResponseDto;
            }
          }

          public Dictionary<string, string> BackChannelContent {
            get {
              return _BackChannelContent;
            }
          }

        }

        /// <summary>
        /// Binds a callback parameter ('Action' / 'Action&lt;...&gt;') of the contract method to a callback recorder.
        /// </summary>
        private class CallbackBinding {

          private static readonly MethodInfo _RecordMethod = typeof(IUjmwCallbackRecorder).GetMethod(nameof(IUjmwCallbackRecorder.Record));

          private readonly string _CallbackName;
          private readonly int _ParameterIndex;
          private readonly PropertyInfo _InvokesProp;
          private readonly Func<IUjmwCallbackRecorder, Delegate> _DelegateFactory;

          public CallbackBinding(ParameterInfo param, int parameterIndex, Type responseDtoType) {

            _CallbackName = param.Name;
            _ParameterIndex = parameterIndex;
            _InvokesProp = responseDtoType.GetProperty(UjmwAsyncProtocol.GetCallbackInvokesPropertyName(param.Name));

            //compiles once: (recorder) => (arg1, ..., argN) => recorder.Record("<name>", new object[] { arg1, ..., argN })
            ParameterExpression recorderParam = Expression.Parameter(typeof(IUjmwCallbackRecorder), "recorder");
            Type[] argumentTypes = UjmwAsyncProtocol.GetCallbackArgumentTypes(param.ParameterType);

            ParameterExpression[] argumentParams = new ParameterExpression[argumentTypes.Length];
            Expression[] boxedArguments = new Expression[argumentTypes.Length];
            for (int i = 0; i < argumentTypes.Length; i++) {
              argumentParams[i] = Expression.Parameter(argumentTypes[i], "arg" + (i + 1).ToString());
              boxedArguments[i] = Expression.Convert(argumentParams[i], typeof(object));
            }

            Expression recordCall = Expression.Call(
              recorderParam, _RecordMethod, Expression.Constant(_CallbackName), Expression.NewArrayInit(typeof(object), boxedArguments)
            );

            LambdaExpression callbackLambda = Expression.Lambda(param.ParameterType, recordCall, argumentParams);

            _DelegateFactory = Expression.Lambda<Func<IUjmwCallbackRecorder, Delegate>>(
              Expression.Convert(callbackLambda, typeof(Delegate)), recorderParam
            ).Compile();

          }

          public string CallbackName {
            get {
              return _CallbackName;
            }
          }

          public int ParameterIndex {
            get {
              return _ParameterIndex;
            }
          }

          public PropertyInfo InvokesProp {
            get {
              return _InvokesProp;
            }
          }

          /// <summary>
          /// Creates the delegate which is passed as callback argument into the service method.
          /// </summary>
          public Delegate CreateDelegate(IUjmwCallbackRecorder callbackRecorder) {
            return _DelegateFactory.Invoke(callbackRecorder);
          }

        }

      }

    }

  }

}
