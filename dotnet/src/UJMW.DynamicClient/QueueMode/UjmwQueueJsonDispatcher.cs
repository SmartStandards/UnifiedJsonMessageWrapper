using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// Thread-safe buffer of callback invocations recorded on the processor side (UJMW callback wire format).
  /// Sequence numbers ('#') are global across all callback parameters of one job.
  /// When the buffer is full, the recording thread blocks (freeze) until the buffer was drained
  /// or finally throws an 'UjmwQueueCallbackBufferOverflowException'.
  /// </summary>
  public class UjmwQueueCallbackBuffer {

    private readonly object _SyncRoot = new object();
    private readonly List<KeyValuePair<string, JObject>> _Pending = new List<KeyValuePair<string, JObject>>();
    private readonly int _Limit;
    private readonly TimeSpan _MaxFreezeTime;
    private long _LastSequence = 0;

    /// <summary>
    /// Creates a buffer with the given limit and maximum freeze time.
    /// </summary>
    public UjmwQueueCallbackBuffer(int limit, TimeSpan maxFreezeTime) {

      if (limit < 1) {
        limit = 1;
      }

      _Limit = limit;
      _MaxFreezeTime = maxFreezeTime;

    }

    /// <summary>
    /// Number of invocations which are waiting to be sent.
    /// </summary>
    public int PendingCount {
      get {
        lock (_SyncRoot) {
          return _Pending.Count;
        }
      }
    }

    /// <summary>
    /// Records an invocation (blocks while the buffer is full).
    /// </summary>
    public void Record(string invokesPropertyName, object[] arguments) {

      lock (_SyncRoot) {

        DateTime deadline = DateTime.UtcNow.Add(_MaxFreezeTime);

        while (_Pending.Count >= _Limit) {

          TimeSpan remaining = deadline - DateTime.UtcNow;
          if (remaining <= TimeSpan.Zero) {
            throw new UjmwQueueCallbackBufferOverflowException(
              $"The callback buffer of the queued job is full (limit {_Limit}) and was not drained within {_MaxFreezeTime.TotalSeconds} seconds."
            );
          }

          Monitor.Wait(_SyncRoot, remaining);
        }

        _LastSequence++;

        JObject invocation = new JObject();
        invocation[UjmwAsyncProtocol._CallbackSequencePropertyName] = _LastSequence;

        for (int i = 0; i < arguments.Length; i++) {

          JToken value = JValue.CreateNull();
          if (arguments[i] != null) {
            value = JToken.FromObject(arguments[i]);
          }

          invocation[UjmwAsyncProtocol.GetCallbackArgumentName(i + 1)] = value;
        }

        _Pending.Add(new KeyValuePair<string, JObject>(invokesPropertyName, invocation));

      }

    }

    /// <summary>
    /// Takes all pending invocations as JSON object (or null if nothing is pending).
    /// If the transport fails, the taken invocations must be handed back via 'Restore'.
    /// </summary>
    public string Take(out int takenCount) {

      lock (_SyncRoot) {

        takenCount = _Pending.Count;

        if (_Pending.Count == 0) {
          return null;
        }

        JObject result = BuildJson(_Pending);
        _Pending.Clear();

        Monitor.PulseAll(_SyncRoot);

        return result.ToString(Formatting.None);
      }

    }

    /// <summary>
    /// Hands invocations which could not be delivered back into the buffer (in front of newer ones).
    /// </summary>
    public void Restore(string callbackInvocationsJson) {

      if (string.IsNullOrWhiteSpace(callbackInvocationsJson)) {
        return;
      }

      JObject json = JObject.Parse(callbackInvocationsJson);
      List<KeyValuePair<string, JObject>> restored = new List<KeyValuePair<string, JObject>>();

      foreach (JProperty property in json.Properties()) {

        JArray items = property.Value as JArray;
        if (items == null) {
          continue;
        }

        foreach (JToken item in items) {

          JObject invocation = item as JObject;
          if (invocation != null) {
            restored.Add(new KeyValuePair<string, JObject>(property.Name, invocation));
          }

        }

      }

      lock (_SyncRoot) {
        _Pending.InsertRange(0, restored.OrderBy((entry) => entry.Value.Value<long>(UjmwAsyncProtocol._CallbackSequencePropertyName)));
      }

    }

    /// <summary>
    /// Merges all pending invocations into the given response object and clears the buffer.
    /// </summary>
    public void DrainInto(JObject response) {

      lock (_SyncRoot) {

        foreach (KeyValuePair<string, JObject> entry in _Pending) {

          JArray items = response[entry.Key] as JArray;
          if (items == null) {
            items = new JArray();
            response[entry.Key] = items;
          }

          items.Add(entry.Value);
        }

        _Pending.Clear();
        Monitor.PulseAll(_SyncRoot);

      }

    }

    private static JObject BuildJson(List<KeyValuePair<string, JObject>> entries) {

      JObject result = new JObject();

      foreach (KeyValuePair<string, JObject> entry in entries) {

        JArray items = result[entry.Key] as JArray;
        if (items == null) {
          items = new JArray();
          result[entry.Key] = items;
        }

        items.Add(entry.Value);
      }

      return result;
    }

  }

  /// <summary>
  /// Thrown inside the implementation, when a callback invocation can not be buffered anymore.
  /// </summary>
  public class UjmwQueueCallbackBufferOverflowException : Exception {

    public UjmwQueueCallbackBufferOverflowException(string message) : base(message) {
    }

  }

  /// <summary>
  /// Result of a dispatched job.
  /// </summary>
  public class UjmwQueueDispatchResult {

    private string _ResponseJson = null;
    private bool _Cancelled = false;

    /// <summary>
    /// The UJMW response JSON (null, if the job was cancelled).
    /// </summary>
    public string ResponseJson {
      get {
        return _ResponseJson;
      }
      set {
        _ResponseJson = value;
      }
    }

    /// <summary>
    /// True, if the implementation aborted because of the cancellation request.
    /// </summary>
    public bool Cancelled {
      get {
        return _Cancelled;
      }
      set {
        _Cancelled = value;
      }
    }

  }

  /// <summary>
  /// Lightweight, reflection-based dispatcher (without ASP.NET Core dependency), which invokes a contract
  /// implementation based on a UJMW request JSON and builds the UJMW response JSON
  /// (see 'doc/epics/epic-async-queue-mode.md', 9.2).
  /// </summary>
  public class UjmwQueueJsonDispatcher {

    private readonly Type _ContractType;
    private readonly Dictionary<string, MethodInfo[]> _MethodsByTransportName;
    private Action<MethodInfo, IDictionary<string, string>> _SideChannelRestorer = null;
    private Func<MethodInfo, IDictionary<string, string>> _BackChannelCapturer = null;

    /// <summary>
    /// Creates a dispatcher for the given contract type (sub-services are not supported in phase 1).
    /// </summary>
    public UjmwQueueJsonDispatcher(Type contractType) {

      if (contractType == null) {
        throw new ArgumentNullException(nameof(contractType));
      }

      _ContractType = contractType;
      _MethodsByTransportName = new Dictionary<string, MethodInfo[]>(StringComparer.OrdinalIgnoreCase);

      List<MethodInfo> allMethods = new List<MethodInfo>();
      CollectMethods(contractType, allMethods);

      foreach (IGrouping<string, MethodInfo> group in allMethods.GroupBy((method) => method.GetNameOrOverride(false), StringComparer.OrdinalIgnoreCase)) {
        _MethodsByTransportName[group.Key] = group.ToArray();
      }

    }

    /// <summary>
    /// The contract type.
    /// </summary>
    public Type ContractType {
      get {
        return _ContractType;
      }
    }

    /// <summary>
    /// Optional: restores the inbound sidechannel ('_' of the request) into the ambient context of the job.
    /// </summary>
    public Action<MethodInfo, IDictionary<string, string>> SideChannelRestorer {
      get {
        return _SideChannelRestorer;
      }
      set {
        _SideChannelRestorer = value;
      }
    }

    /// <summary>
    /// Optional: captures the outgoing backchannel ('_' of the response) after the invocation.
    /// </summary>
    public Func<MethodInfo, IDictionary<string, string>> BackChannelCapturer {
      get {
        return _BackChannelCapturer;
      }
      set {
        _BackChannelCapturer = value;
      }
    }

    /// <summary>
    /// Returns all transport-level method names of the contract.
    /// </summary>
    public string[] TransportMethodNames {
      get {
        return _MethodsByTransportName.Keys.ToArray();
      }
    }

    /// <summary>
    /// Returns all transport-level method names belonging to the given C# method name (all overloads).
    /// </summary>
    public string[] GetTransportMethodNames(string csharpMethodName) {

      List<string> result = new List<string>();

      foreach (KeyValuePair<string, MethodInfo[]> entry in _MethodsByTransportName) {
        if (entry.Value.Any((method) => method.Name.Equals(csharpMethodName, StringComparison.OrdinalIgnoreCase))) {
          result.Add(entry.Key);
        }
      }

      return result.ToArray();
    }

    /// <summary>
    /// Resolves the contract method of a transport-level method name (overloads are matched by their parameter names).
    /// </summary>
    public MethodInfo ResolveMethod(string transportMethodName, JObject request) {

      MethodInfo[] candidates;
      if (!_MethodsByTransportName.TryGetValue(transportMethodName, out candidates)) {
        throw new MissingMethodException($"The contract '{_ContractType.Name}' has no method '{transportMethodName}'.");
      }

      if (candidates.Length == 1) {
        return candidates[0];
      }

      string[] provided = request.Properties().Select((property) => property.Name).Where((name) => name != "_").ToArray();

      MethodInfo best = null;
      int bestScore = int.MinValue;

      foreach (MethodInfo candidate in candidates) {

        string[] dtoParams = candidate.GetParameters()
          .Where((param) => UjmwAsyncProtocol.IsTransportedAsDtoProperty(param) && !param.IsOut)
          .Select((param) => param.Name)
          .ToArray();

        int matches = provided.Count((name) => dtoParams.Contains(name, StringComparer.OrdinalIgnoreCase));
        int unknown = provided.Length - matches;
        int score = (matches * 10) - (unknown * 100) - (dtoParams.Length - matches);

        if (score > bestScore) {
          bestScore = score;
          best = candidate;
        }

      }

      return best;
    }

    /// <summary>
    /// Invokes the implementation for the given request and returns the UJMW response JSON.
    /// Business exceptions are transported as 'fault', a cancellation (if requested via the token) is reported as 'Cancelled'.
    /// </summary>
    public UjmwQueueDispatchResult Dispatch(
      object implementation, string transportMethodName, string requestJson,
      CancellationToken cancellationToken, UjmwQueueCallbackBuffer callbackBuffer
    ) {

      if (implementation == null) {
        throw new ArgumentNullException(nameof(implementation));
      }

      UjmwQueueDispatchResult result = new UjmwQueueDispatchResult();
      JObject response = new JObject();
      JsonSerializer serializer = CreateSerializer();

      MethodInfo method = null;

      try {

        JObject request = new JObject();
        if (!string.IsNullOrWhiteSpace(requestJson)) {
          request = JObject.Parse(requestJson);
        }

        method = this.ResolveMethod(transportMethodName, request);

        this.RestoreSideChannel(method, request);

        ParameterInfo[] parameters = method.GetParameters();
        object[] arguments = this.BuildArguments(parameters, request, serializer, cancellationToken, callbackBuffer);

        object returnValue;
        try {
          returnValue = method.Invoke(implementation, arguments);
        }
        catch (TargetInvocationException ex) {
          Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
          throw;
        }

        if (method.ReturnType != typeof(void)) {
          response["return"] = ToToken(returnValue, serializer);
        }

        for (int i = 0; i < parameters.Length; i++) {
          if (parameters[i].ParameterType.IsByRef) {
            response[parameters[i].Name] = ToToken(arguments[i], serializer);
          }
        }

        response["fault"] = JValue.CreateNull();

      }
      catch (Exception ex) {

        if (IsCancellation(ex) && cancellationToken.IsCancellationRequested) {
          result.Cancelled = true;
          return result;
        }

        response = new JObject();
        response["fault"] = ex.Message;

      }

      this.CaptureBackChannel(method, response);

      if (callbackBuffer != null) {
        callbackBuffer.DrainInto(response);
      }

      result.ResponseJson = response.ToString(Formatting.None);
      return result;
    }

    #region " Helpers "

    private object[] BuildArguments(
      ParameterInfo[] parameters, JObject request, JsonSerializer serializer,
      CancellationToken cancellationToken, UjmwQueueCallbackBuffer callbackBuffer
    ) {

      object[] arguments = new object[parameters.Length];

      for (int i = 0; i < parameters.Length; i++) {

        ParameterInfo param = parameters[i];

        if (UjmwAsyncProtocol.IsCancellationTokenParameter(param)) {
          arguments[i] = cancellationToken;
          continue;
        }

        if (UjmwAsyncProtocol.IsCallbackParameter(param)) {
          arguments[i] = CreateRecordingDelegate(param, callbackBuffer);
          continue;
        }

        Type valueType = param.ParameterType;
        if (valueType.IsByRef) {
          valueType = valueType.GetElementType();
        }

        JToken token = FindProperty(request, param.Name);

        if (token != null && !param.IsOut) {
          arguments[i] = token.ToObject(valueType, serializer);
        }
        else if (param.IsOptional && !param.IsOut) {
          arguments[i] = param.DefaultValue;
          if (arguments[i] == DBNull.Value || arguments[i] == Type.Missing) {
            arguments[i] = GetDefault(valueType);
          }
        }
        else {
          arguments[i] = GetDefault(valueType);
        }

      }

      return arguments;
    }

    /// <summary>
    /// Creates a delegate of the callback type which records every invocation into the buffer.
    /// </summary>
    private static Delegate CreateRecordingDelegate(ParameterInfo param, UjmwQueueCallbackBuffer callbackBuffer) {

      if (callbackBuffer == null) {
        callbackBuffer = new UjmwQueueCallbackBuffer(int.MaxValue, TimeSpan.Zero);
      }

      Type[] argumentTypes = UjmwAsyncProtocol.GetCallbackArgumentTypes(param.ParameterType);
      ParameterExpression[] lambdaParams = argumentTypes.Select((argumentType) => Expression.Parameter(argumentType)).ToArray();

      MethodInfo recordMethod = typeof(UjmwQueueCallbackBuffer).GetMethod(nameof(UjmwQueueCallbackBuffer.Record));

      Expression body = Expression.Call(
        Expression.Constant(callbackBuffer),
        recordMethod,
        Expression.Constant(UjmwAsyncProtocol.GetCallbackInvokesPropertyName(param.Name)),
        Expression.NewArrayInit(typeof(object), lambdaParams.Select((p) => (Expression)Expression.Convert(p, typeof(object))))
      );

      return Expression.Lambda(param.ParameterType, body, lambdaParams).Compile();
    }

    private void RestoreSideChannel(MethodInfo method, JObject request) {

      if (_SideChannelRestorer == null) {
        return;
      }

      Dictionary<string, string> sideChannel = new Dictionary<string, string>();

      JObject underline = request["_"] as JObject;
      if (underline != null) {
        foreach (JProperty property in underline.Properties()) {

          string value = null;
          if (property.Value.Type == JTokenType.String) {
            value = property.Value.Value<string>();
          }
          else if (property.Value.Type != JTokenType.Null) {
            value = property.Value.ToString(Formatting.None);
          }

          sideChannel[property.Name] = value;
        }
      }

      _SideChannelRestorer.Invoke(method, sideChannel);

    }

    private void CaptureBackChannel(MethodInfo method, JObject response) {

      if (_BackChannelCapturer == null || method == null) {
        return;
      }

      IDictionary<string, string> backChannel = _BackChannelCapturer.Invoke(method);
      if (backChannel == null) {
        return;
      }

      response["_"] = JObject.FromObject(backChannel);

    }

    private static JToken FindProperty(JObject request, string name) {

      foreach (JProperty property in request.Properties()) {
        if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
          return property.Value;
        }
      }

      return null;
    }

    private static JToken ToToken(object value, JsonSerializer serializer) {

      if (value == null) {
        return JValue.CreateNull();
      }

      return JToken.FromObject(value, serializer);
    }

    private static object GetDefault(Type type) {

      if (type.IsValueType) {
        return Activator.CreateInstance(type);
      }

      return null;
    }

    private static bool IsCancellation(Exception ex) {

      Exception current = ex;

      while (current != null) {

        if (current is OperationCanceledException) {
          return true;
        }

        current = current.InnerException;
      }

      return false;
    }

    private static JsonSerializer CreateSerializer() {

      JsonSerializerSettings settings = new JsonSerializerSettings();
      settings.DateFormatHandling = DateFormatHandling.IsoDateFormat;
      settings.ContractResolver = new CamelCasePropertyNamesContractResolver();

      return JsonSerializer.Create(settings);
    }

    private static void CollectMethods(Type type, List<MethodInfo> target) {

      foreach (MethodInfo method in type.GetMethods()) {
        if (!method.IsSpecialName && !target.Contains(method)) {
          target.Add(method);
        }
      }

      foreach (Type baseInterface in type.GetInterfaces()) {
        CollectMethods(baseInterface, target);
      }

    }

    #endregion

  }

}
