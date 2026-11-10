using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// Specification tests for the shared protocol primitives of the long-running operations feature
  /// (see 'doc/epics/epic-long-running-operations.md').
  /// </summary>
  [TestClass]
  public class UjmwAsyncProtocolTests {

    public interface IProtocolProbeContract {

      int LongRunner(int a, CancellationToken c, out int b);

      void WithCallbacks(CancellationToken c, Action plain, Action<int> one, Action<string, bool> two);

      int NotLongRunning(int a, Action<int> callback);

      void TwoTokens(CancellationToken c1, CancellationToken c2);

      void WithFunc(Func<int> notACallback);

    }

    /// <summary>
    /// Returns the parameter of the probe contract method with the given name.
    /// </summary>
    private static ParameterInfo GetParam(string methodName, string paramName) {

      MethodInfo method = typeof(IProtocolProbeContract).GetMethod(methodName);
      foreach (ParameterInfo param in method.GetParameters()) {
        if (param.Name == paramName) {
          return param;
        }
      }

      throw new ArgumentException(paramName);
    }

    [TestMethod]
    public void IsLongRunningMethod_DetectsCancellationTokenAtAnyPosition() {

      Assert.IsTrue(UjmwAsyncProtocol.IsLongRunningMethod(typeof(IProtocolProbeContract).GetMethod(nameof(IProtocolProbeContract.LongRunner))));
      Assert.IsTrue(UjmwAsyncProtocol.IsLongRunningMethod(typeof(IProtocolProbeContract).GetMethod(nameof(IProtocolProbeContract.WithCallbacks))));
      Assert.IsFalse(UjmwAsyncProtocol.IsLongRunningMethod(typeof(IProtocolProbeContract).GetMethod(nameof(IProtocolProbeContract.NotLongRunning))));

    }

    [TestMethod]
    public void IsLongRunningMethod_RejectsMoreThanOneCancellationToken() {

      MethodInfo method = typeof(IProtocolProbeContract).GetMethod(nameof(IProtocolProbeContract.TwoTokens));

      Assert.ThrowsException<NotSupportedException>(() => UjmwAsyncProtocol.IsLongRunningMethod(method));

    }

    [TestMethod]
    public void ParameterClassification_DistinguishesTokensCallbacksAndRegularParams() {

      Assert.IsTrue(UjmwAsyncProtocol.IsCancellationTokenParameter(GetParam(nameof(IProtocolProbeContract.LongRunner), "c")));
      Assert.IsFalse(UjmwAsyncProtocol.IsCancellationTokenParameter(GetParam(nameof(IProtocolProbeContract.LongRunner), "a")));

      Assert.IsTrue(UjmwAsyncProtocol.IsCallbackParameter(GetParam(nameof(IProtocolProbeContract.WithCallbacks), "plain")));
      Assert.IsTrue(UjmwAsyncProtocol.IsCallbackParameter(GetParam(nameof(IProtocolProbeContract.WithCallbacks), "one")));
      Assert.IsTrue(UjmwAsyncProtocol.IsCallbackParameter(GetParam(nameof(IProtocolProbeContract.WithCallbacks), "two")));
      Assert.IsFalse(UjmwAsyncProtocol.IsCallbackParameter(GetParam(nameof(IProtocolProbeContract.WithCallbacks), "c")));

      //only 'Action<...>' is supported - no 'Func<...>'
      Assert.IsFalse(UjmwAsyncProtocol.IsCallbackParameter(GetParam(nameof(IProtocolProbeContract.WithFunc), "notACallback")));

      Assert.IsTrue(UjmwAsyncProtocol.IsTransportedAsDtoProperty(GetParam(nameof(IProtocolProbeContract.LongRunner), "a")));
      Assert.IsFalse(UjmwAsyncProtocol.IsTransportedAsDtoProperty(GetParam(nameof(IProtocolProbeContract.LongRunner), "c")));
      Assert.IsFalse(UjmwAsyncProtocol.IsTransportedAsDtoProperty(GetParam(nameof(IProtocolProbeContract.WithCallbacks), "one")));

    }

    [TestMethod]
    public void GetCallbackArgumentTypes_ReturnsGenericArgumentsInPositionalOrder() {

      Type[] none = UjmwAsyncProtocol.GetCallbackArgumentTypes(typeof(Action));
      Type[] two = UjmwAsyncProtocol.GetCallbackArgumentTypes(typeof(Action<string, bool>));

      Assert.AreEqual(0, none.Length);
      Assert.AreEqual(2, two.Length);
      Assert.AreEqual(typeof(string), two[0]);
      Assert.AreEqual(typeof(bool), two[1]);

    }

    [TestMethod]
    public void GetCallbackInvokesPropertyName_AppendsInvokesSuffix() {

      Assert.AreEqual("currentTicketIdCallbackInvokes", UjmwAsyncProtocol.GetCallbackInvokesPropertyName("currentTicketIdCallback"));

    }

    [TestMethod]
    public void ComputePollingHash_IsBase64UrlOfSha256OfUtf8ConcatenationWithoutPadding() {

      //well known SHA-256 test vector: SHA-256("abc") = ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
      //standard Base64: "ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0="
      string hash = UjmwAsyncProtocol.ComputePollingHash("ab", "c");

      Assert.AreEqual("ungWv48Bz-pBQUDeXa4iI7ADYaOWF3qctBD_YfIAFa0", hash);

    }

    [TestMethod]
    public void ComputePollingHash_UsesUtf8ForNonAsciiCharacters() {

      string expected;
      using (SHA256 sha = SHA256.Create()) {
        byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("\u00e4\u00f6\u00fcSalt"));
        expected = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
      }

      Assert.AreEqual(expected, UjmwAsyncProtocol.ComputePollingHash("\u00e4\u00f6\u00fc", "Salt"));

    }

    [TestMethod]
    public void GenerateRandomAlphanumeric_ProducesRequestedLengthAndOnlyAlphanumericChars() {

      string secret = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSecretLength);
      string salt = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSaltLength);

      Assert.AreEqual(8, secret.Length);
      Assert.AreEqual(6, salt.Length);

      foreach (char c in secret + salt) {
        bool isAlphanumeric = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
        Assert.IsTrue(isAlphanumeric, $"Unexpected char '{c}'");
      }

      Assert.AreNotEqual(
        UjmwAsyncProtocol.GenerateRandomAlphanumeric(32),
        UjmwAsyncProtocol.GenerateRandomAlphanumeric(32)
      );

    }

    [TestMethod]
    public void GetFibonacciWindowSeconds_ReturnsFullValueOfTheWindow() {

      Assert.AreEqual(1, UjmwAsyncProtocol.GetFibonacciWindowSeconds(0));
      Assert.AreEqual(1, UjmwAsyncProtocol.GetFibonacciWindowSeconds(0.5));
      Assert.AreEqual(1, UjmwAsyncProtocol.GetFibonacciWindowSeconds(1.5));
      Assert.AreEqual(1, UjmwAsyncProtocol.GetFibonacciWindowSeconds(2.5));
      Assert.AreEqual(2, UjmwAsyncProtocol.GetFibonacciWindowSeconds(4));
      Assert.AreEqual(3, UjmwAsyncProtocol.GetFibonacciWindowSeconds(5));
      Assert.AreEqual(3, UjmwAsyncProtocol.GetFibonacciWindowSeconds(7.9));

      //example from the spec: a poll after 9 seconds falls into the window 8..13 and receives 5 (not 4)
      Assert.AreEqual(5, UjmwAsyncProtocol.GetFibonacciWindowSeconds(9));

      Assert.AreEqual(8, UjmwAsyncProtocol.GetFibonacciWindowSeconds(13));
      Assert.AreEqual(13, UjmwAsyncProtocol.GetFibonacciWindowSeconds(30));

    }

    [TestMethod]
    public void CalculateDefaultPollDelaySeconds_AppliesLoadDependentThrottling() {

      //window 8..13 => 5
      Assert.AreEqual(5, UjmwAsyncProtocol.CalculateDefaultPollDelaySeconds(9, 0, 1));
      Assert.AreEqual(5, UjmwAsyncProtocol.CalculateDefaultPollDelaySeconds(9, 99, 1));

      //throttlingLevel 1: doubles at 100, triples at 200
      Assert.AreEqual(10, UjmwAsyncProtocol.CalculateDefaultPollDelaySeconds(9, 100, 1));
      Assert.AreEqual(15, UjmwAsyncProtocol.CalculateDefaultPollDelaySeconds(9, 200, 1));

      //throttlingLevel 2: doubles at 50, triples at 100
      Assert.AreEqual(10, UjmwAsyncProtocol.CalculateDefaultPollDelaySeconds(9, 50, 2));
      Assert.AreEqual(15, UjmwAsyncProtocol.CalculateDefaultPollDelaySeconds(9, 100, 2));

    }

    [TestMethod]
    public void IsTerminalExecState_OnlyForCancelledCompletedAndFailed() {

      Assert.IsTrue(UjmwAsyncProtocol.IsTerminalExecState(UjmwAsyncProtocol._ExecStateCancelled));
      Assert.IsTrue(UjmwAsyncProtocol.IsTerminalExecState(UjmwAsyncProtocol._ExecStateCompleted));
      Assert.IsTrue(UjmwAsyncProtocol.IsTerminalExecState(UjmwAsyncProtocol._ExecStateFailed));

      Assert.IsFalse(UjmwAsyncProtocol.IsTerminalExecState(UjmwAsyncProtocol._ExecStateCancellationRequested));
      Assert.IsFalse(UjmwAsyncProtocol.IsTerminalExecState(UjmwAsyncProtocol._ExecStateQueued));
      Assert.IsFalse(UjmwAsyncProtocol.IsTerminalExecState(UjmwAsyncProtocol._ExecStateRunning));

    }

  }

}
