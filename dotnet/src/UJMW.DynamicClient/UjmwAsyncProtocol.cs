using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// Shared protocol primitives for UJMW long-running operations (async execution, polling, cancellation and callbacks).
  /// This file is shared between the dynamic client and the dynamic controller (see 'doc/epics/epic-long-running-operations.md').
  /// </summary>
  internal static class UjmwAsyncProtocol {

    #region " Constants "

    /// <summary>
    /// Reserved wrapper property which carries the async execution state.
    /// </summary>
    public const string _AsyncPropertyName = "async";

    /// <summary>
    /// Suffix of the response property which carries the invocations of a callback parameter.
    /// </summary>
    public const string _CallbackInvokesSuffix = "Invokes";

    /// <summary>
    /// Name of the sequence number within a callback invocation entry.
    /// </summary>
    public const string _CallbackSequencePropertyName = "#";

    /// <summary>
    /// Prefix of the positional argument names within a callback invocation entry ('arg1' ... 'argN').
    /// </summary>
    public const string _CallbackArgumentPrefix = "arg";

    public const string _AsyncIdPropertyName = "asyncId";
    public const string _ExecStatePropertyName = "execState";
    public const string _PollNbfPropertyName = "pollNbf";
    public const string _CancellablePropertyName = "cancellable";
    public const string _PollingSecretPropertyName = "pollingSecret";

    public const string _WaitQueryParam = "wait";
    public const string _AsyncIdQueryParam = "asyncId";
    public const string _PollingSaltQueryParam = "pollingSalt";
    public const string _PollingHashQueryParam = "pollingHash";

    public const int _PollingSecretLength = 8;
    public const int _PollingSaltLength = 6;

    public const string _WrongPollingAuthFault = "Wrong asyncId or pollingHash";

    public const int _ExecStateCancelled = -2;
    public const int _ExecStateCancellationRequested = -1;
    public const int _ExecStateQueued = 0;
    public const int _ExecStateRunning = 1;
    public const int _ExecStateCompleted = 2;
    public const int _ExecStateFailed = 3;

    private const string _AlphanumericChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    #endregion

    #region " Parameter Classification "

    /// <summary>
    /// Returns true, if the given method declares a 'CancellationToken' parameter (convention for potentially long-running operations).
    /// Throws a 'NotSupportedException' if more than one 'CancellationToken' parameter is declared.
    /// </summary>
    public static bool IsLongRunningMethod(MethodInfo method) {

      int tokenCount = 0;
      foreach (ParameterInfo param in method.GetParameters()) {
        if (IsCancellationTokenParameter(param)) {
          tokenCount++;
        }
      }

      if (tokenCount > 1) {
        throw new NotSupportedException($"UJMW does not support more than one CancellationToken parameter (method '{method.Name}')!");
      }

      return (tokenCount == 1);
    }

    /// <summary>
    /// Returns true, if the parameter is a 'CancellationToken' (which is never part of the request DTO).
    /// </summary>
    public static bool IsCancellationTokenParameter(ParameterInfo param) {
      return (param.ParameterType == typeof(CancellationToken));
    }

    /// <summary>
    /// Returns true, if the parameter is a callback ('Action' or 'Action&lt;...&gt;' - 'Func&lt;...&gt;' is NOT supported).
    /// </summary>
    public static bool IsCallbackParameter(ParameterInfo param) {
      return IsCallbackType(param.ParameterType);
    }

    /// <summary>
    /// Returns true, if the type is 'Action' or a closed generic 'Action&lt;...&gt;'.
    /// </summary>
    public static bool IsCallbackType(Type type) {

      if (type == typeof(Action)) {
        return true;
      }

      if (!type.IsGenericType) {
        return false;
      }

      Type genericDefinition = type.GetGenericTypeDefinition();
      if (genericDefinition.Namespace != "System" || !genericDefinition.Name.StartsWith("Action`")) {
        return false;
      }

      return (genericDefinition.Assembly == typeof(Action).Assembly || genericDefinition.Assembly == typeof(Action<,,,,>).Assembly);
    }

    /// <summary>
    /// Returns true, if the parameter is transported as regular DTO property (false for tokens and callbacks).
    /// </summary>
    public static bool IsTransportedAsDtoProperty(ParameterInfo param) {
      return (!IsCancellationTokenParameter(param) && !IsCallbackParameter(param));
    }

    /// <summary>
    /// Returns the argument types of a callback delegate type in positional order.
    /// </summary>
    public static Type[] GetCallbackArgumentTypes(Type callbackType) {

      if (callbackType == typeof(Action)) {
        return Type.EmptyTypes;
      }

      return callbackType.GetGenericArguments();
    }

    /// <summary>
    /// Returns the name of the response property, which carries the invocations of the given callback parameter.
    /// </summary>
    public static string GetCallbackInvokesPropertyName(string callbackParamName) {
      return callbackParamName + _CallbackInvokesSuffix;
    }

    /// <summary>
    /// Returns the name of the positional argument within a callback invocation entry (1-based).
    /// </summary>
    public static string GetCallbackArgumentName(int oneBasedPosition) {
      return _CallbackArgumentPrefix + oneBasedPosition.ToString();
    }

    #endregion

    #region " Polling Authentication "

    /// <summary>
    /// Computes 'Base64Url(SHA-256(UTF-8(pollingSecret + pollingSalt)))' without padding.
    /// </summary>
    public static string ComputePollingHash(string pollingSecret, string pollingSalt) {

      byte[] input = Encoding.UTF8.GetBytes(pollingSecret + pollingSalt);
      byte[] hash;
      using (SHA256 sha = SHA256.Create()) {
        hash = sha.ComputeHash(input);
      }

      return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Verifies the given polling hash using a constant-time comparison.
    /// </summary>
    public static bool VerifyPollingHash(string pollingSecret, string pollingSalt, string pollingHash) {

      if (string.IsNullOrEmpty(pollingSecret) || string.IsNullOrEmpty(pollingSalt) || string.IsNullOrEmpty(pollingHash)) {
        return false;
      }

      string expected = ComputePollingHash(pollingSecret, pollingSalt);
      if (expected.Length != pollingHash.Length) {
        return false;
      }

      int difference = 0;
      for (int i = 0; i < expected.Length; i++) {
        difference = difference | (expected[i] ^ pollingHash[i]);
      }

      return (difference == 0);
    }

    /// <summary>
    /// Generates a cryptographically random string of the given length using 'A-Z', 'a-z' and '0-9'.
    /// </summary>
    public static string GenerateRandomAlphanumeric(int length) {

      StringBuilder result = new StringBuilder(length);
      byte[] buffer = new byte[1];

      using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) {
        while (result.Length < length) {

          rng.GetBytes(buffer);

          //rejection sampling to avoid a modulo bias (62 * 4 = 248)
          if (buffer[0] < 248) {
            result.Append(_AlphanumericChars[buffer[0] % 62]);
          }

        }
      }

      return result.ToString();
    }

    #endregion

    #region " Poll Interval "

    /// <summary>
    /// Returns the full Fibonacci value of the window (bounded by 1, 2, 3, 5, 8, 13, 21, ...) into which the elapsed time falls.
    /// Example: 9 seconds fall into the window 8..13 and result in 5.
    /// </summary>
    public static int GetFibonacciWindowSeconds(double elapsedSeconds) {

      int lower = 1;
      int upper = 2;

      if (elapsedSeconds < upper) {
        return 1;
      }

      while (elapsedSeconds >= upper) {

        int next = lower + upper;
        lower = upper;
        upper = next;

        if (upper > 1000000) {
          break;
        }

      }

      return (upper - lower);
    }

    /// <summary>
    /// Default poll delay: ROUND(Fibonacci(window of elapsed time) * ((overallActiveOperations * throttlingLevel) \ 100 + 1)).
    /// </summary>
    public static int CalculateDefaultPollDelaySeconds(double elapsedSeconds, int overallActiveOperations, int throttlingLevel) {

      int fibonacci = GetFibonacciWindowSeconds(elapsedSeconds);
      int throttlingFactor = ((overallActiveOperations * throttlingLevel) / 100) + 1;

      return fibonacci * throttlingFactor;
    }

    #endregion

    /// <summary>
    /// Returns true for cancelled, completed and failed.
    /// </summary>
    public static bool IsTerminalExecState(int execState) {
      return (execState == _ExecStateCancelled || execState == _ExecStateCompleted || execState == _ExecStateFailed);
    }

  }

}
