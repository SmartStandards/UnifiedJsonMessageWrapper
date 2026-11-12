#if NET5
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// Specification tests for the processor-side 'UjmwQueueJsonDispatcher' (see 'doc/epics/epic-async-queue-mode.md', 9.2).
  /// </summary>
  [TestClass]
  public class UjmwQueueJsonDispatcherTests {

    public interface IDispatchProbe {

      int Add(int a, int b);

      void Split(string text, out string head, ref int counter);

      string Run(CancellationToken cancellationToken, int steps, Action<string, int> reportProgress, Action<int> reportCount);

      string Fail(string message);

    }

    public class DispatchProbe : IDispatchProbe {

      public int Add(int a, int b) {
        return a + b;
      }

      public void Split(string text, out string head, ref int counter) {
        head = text.Substring(0, 1);
        counter = counter + 1;
      }

      public string Run(CancellationToken cancellationToken, int steps, Action<string, int> reportProgress, Action<int> reportCount) {

        for (int i = 1; i <= steps; i++) {
          cancellationToken.ThrowIfCancellationRequested();
          reportProgress.Invoke("step", i);
          reportCount.Invoke(i);
        }

        return "ran " + steps;
      }

      public string Fail(string message) {
        throw new InvalidOperationException(message);
      }

    }

    private static UjmwQueueJsonDispatcher CreateDispatcher() {
      return new UjmwQueueJsonDispatcher(typeof(IDispatchProbe));
    }

    [TestMethod]
    public void Dispatch_ReturnValue() {

      UjmwQueueDispatchResult result = CreateDispatcher().Dispatch(
        new DispatchProbe(), "Add", "{ \"a\": 2, \"b\": 3 }", CancellationToken.None, null
      );

      JObject response = JObject.Parse(result.ResponseJson);
      Assert.AreEqual(5, response["return"].Value<int>());
      Assert.AreEqual(JTokenType.Null, response["fault"].Type);

    }

    [TestMethod]
    public void Dispatch_OutAndRefValues() {

      UjmwQueueDispatchResult result = CreateDispatcher().Dispatch(
        new DispatchProbe(), "Split", "{ \"text\": \"Hello\", \"counter\": 41 }", CancellationToken.None, null
      );

      JObject response = JObject.Parse(result.ResponseJson);
      Assert.AreEqual("H", response["head"].Value<string>());
      Assert.AreEqual(42, response["counter"].Value<int>());

    }

    [TestMethod]
    public void Dispatch_BusinessExceptionBecomesFault() {

      UjmwQueueDispatchResult result = CreateDispatcher().Dispatch(
        new DispatchProbe(), "Fail", "{ \"message\": \"Boom\" }", CancellationToken.None, null
      );

      Assert.IsFalse(result.Cancelled);
      Assert.AreEqual("Boom", JObject.Parse(result.ResponseJson)["fault"].Value<string>());

    }

    [TestMethod]
    public void Dispatch_UnknownMethodBecomesFault() {

      UjmwQueueDispatchResult result = CreateDispatcher().Dispatch(
        new DispatchProbe(), "Nope", "{}", CancellationToken.None, null
      );

      StringAssert.Contains(JObject.Parse(result.ResponseJson)["fault"].Value<string>(), "Nope");

    }

    [TestMethod]
    public void Dispatch_CallbacksAreRecordedWithGlobalSequence() {

      UjmwQueueCallbackBuffer buffer = new UjmwQueueCallbackBuffer(100, TimeSpan.FromSeconds(1));

      UjmwQueueDispatchResult result = CreateDispatcher().Dispatch(
        new DispatchProbe(), "Run", "{ \"steps\": 2 }", CancellationToken.None, buffer
      );

      JObject response = JObject.Parse(result.ResponseJson);
      Assert.AreEqual("ran 2", response["return"].Value<string>());

      JArray progress = (JArray)response["reportProgressInvokes"];
      JArray count = (JArray)response["reportCountInvokes"];

      Assert.AreEqual(2, progress.Count);
      Assert.AreEqual(2, count.Count);
      Assert.AreEqual(1, progress[0]["#"].Value<long>());
      Assert.AreEqual(2, count[0]["#"].Value<long>());
      Assert.AreEqual(3, progress[1]["#"].Value<long>());
      Assert.AreEqual("step", progress[1]["arg1"].Value<string>());
      Assert.AreEqual(2, progress[1]["arg2"].Value<int>());
      Assert.AreEqual(0, buffer.PendingCount);

    }

    [TestMethod]
    public void Dispatch_CancellationIsReported() {

      using (CancellationTokenSource cts = new CancellationTokenSource()) {

        cts.Cancel();

        UjmwQueueDispatchResult result = CreateDispatcher().Dispatch(
          new DispatchProbe(), "Run", "{ \"steps\": 2 }", cts.Token, null
        );

        Assert.IsTrue(result.Cancelled);
        Assert.IsNull(result.ResponseJson);
      }

    }

    [TestMethod]
    public void Dispatch_SideChannelIsRestoredAndBackChannelCaptured() {

      UjmwQueueJsonDispatcher dispatcher = CreateDispatcher();
      IDictionary<string, string> restored = null;

      dispatcher.SideChannelRestorer = (MethodInfo method, IDictionary<string, string> sideChannel) => {
        restored = sideChannel;
      };

      dispatcher.BackChannelCapturer = (MethodInfo method) => {
        Dictionary<string, string> backChannel = new Dictionary<string, string>();
        backChannel["tenant"] = "back";
        return backChannel;
      };

      UjmwQueueDispatchResult result = dispatcher.Dispatch(
        new DispatchProbe(), "Add", "{ \"a\": 1, \"b\": 1, \"_\": { \"tenant\": \"T1\" } }", CancellationToken.None, null
      );

      Assert.IsNotNull(restored);
      Assert.AreEqual("T1", restored["tenant"]);
      Assert.AreEqual("back", JObject.Parse(result.ResponseJson)["_"]["tenant"].Value<string>());

    }

    [TestMethod]
    public void CallbackBuffer_TakeAndRestorePreserveOrder() {

      UjmwQueueCallbackBuffer buffer = new UjmwQueueCallbackBuffer(10, TimeSpan.FromSeconds(1));
      buffer.Record("aInvokes", new object[] { 1 });
      buffer.Record("bInvokes", new object[] { 2 });

      int taken;
      string json = buffer.Take(out taken);
      Assert.AreEqual(2, taken);
      Assert.AreEqual(0, buffer.PendingCount);

      buffer.Record("aInvokes", new object[] { 3 });
      buffer.Restore(json);

      JObject response = new JObject();
      buffer.DrainInto(response);

      JArray a = (JArray)response["aInvokes"];
      Assert.AreEqual(1, a[0]["#"].Value<long>());
      Assert.AreEqual(3, a[1]["#"].Value<long>());

    }

    [TestMethod]
    public void CallbackBuffer_OverflowThrowsAfterFreeze() {

      UjmwQueueCallbackBuffer buffer = new UjmwQueueCallbackBuffer(1, TimeSpan.FromMilliseconds(50));
      buffer.Record("aInvokes", new object[] { 1 });

      Assert.ThrowsException<UjmwQueueCallbackBufferOverflowException>(() => {
        buffer.Record("aInvokes", new object[] { 2 });
      });

    }

    [TestMethod]
    public void GetTransportMethodNames_CoversCSharpName() {

      CollectionAssert.AreEquivalent(new string[] { "Run" }, CreateDispatcher().GetTransportMethodNames("Run"));

    }

  }

}
#endif
