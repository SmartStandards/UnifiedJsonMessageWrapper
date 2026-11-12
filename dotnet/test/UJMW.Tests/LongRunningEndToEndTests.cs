#if NET5
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// Contract used to validate long-running operations end-to-end (dynamic controller + dynamic client over real HTTP).
  /// </summary>
  public interface ILongRunningTestService {

    /// <summary>
    /// Counts from 1 to 'upTo' (reporting every step via callback) and returns 'upTo'.
    /// </summary>
    int Count(CancellationToken cancellationToken, int upTo, int delayMs, Action<int> onProgress, out string label);

    /// <summary>
    /// Blocks until the operation is cancelled (or the given time has elapsed).
    /// </summary>
    void WaitForCancel(CancellationToken cancellationToken, int maxSeconds);

    /// <summary>
    /// A classic (not long-running) operation.
    /// </summary>
    int Quick(int a);

  }

  internal class LongRunningTestService : ILongRunningTestService {

    /// <summary>
    /// Counts from 1 to 'upTo' (reporting every step via callback) and returns 'upTo'.
    /// </summary>
    public int Count(CancellationToken cancellationToken, int upTo, int delayMs, Action<int> onProgress, out string label) {

      for (int i = 1; i <= upTo; i++) {

        cancellationToken.ThrowIfCancellationRequested();

        if (delayMs > 0) {
          Thread.Sleep(delayMs);
        }

        onProgress.Invoke(i);

      }

      label = "counted to " + upTo.ToString();

      return upTo;
    }

    /// <summary>
    /// Blocks until the operation is cancelled (or the given time has elapsed).
    /// </summary>
    public void WaitForCancel(CancellationToken cancellationToken, int maxSeconds) {

      cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(maxSeconds));
      cancellationToken.ThrowIfCancellationRequested();

    }

    /// <summary>
    /// A classic (not long-running) operation.
    /// </summary>
    public int Quick(int a) {
      return a + 1;
    }

  }

  [TestClass]
  public class LongRunningEndToEndTests {

    private static WebApplication _App;
    private static string _BaseUrl;
    private static int _OriginalLongRunningWaitSeconds;

    /// <summary>
    /// Hosts the dynamic controller for 'ILongRunningTestService' on a dynamic loopback port.
    /// </summary>
    [ClassInitialize]
    public static void StartHost(TestContext context) {

      _OriginalLongRunningWaitSeconds = UjmwClientConfiguration.LongRunningWaitSeconds;

      //short poll intervals to keep the tests fast
      UjmwLongRunningOptions storeOptions = new UjmwLongRunningOptions();
      storeOptions.CalculateNbf = (operationStartTime, lastPollTime, overallActiveOperations) => lastPollTime.AddMilliseconds(200);
      UjmwHostConfiguration.OperationStore = new UjmwInMemoryOperationStore(storeOptions);

      WebApplicationBuilder builder = WebApplication.CreateBuilder();
      builder.WebHost.UseUrls("http://127.0.0.1:0");

      builder.Services.AddSingleton<ILongRunningTestService>(new LongRunningTestService());
      builder.Services.AddEndpointsApiExplorer();
      builder.Services.AddDynamicUjmwControllers((r) => {
        r.AddControllerFor<ILongRunningTestService>((c) => {
          c.ControllerRoute = "lrtest";
          c.ApiGroupName = "LongRunningTest";
          c.EnableAuthHeaderEvaluatorHook = false;
        });
      });

      _App = builder.Build();
      _App.MapControllers();
      _App.StartAsync().GetAwaiter().GetResult();

      IServer server = _App.Services.GetRequiredService<IServer>();
      IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>();
      _BaseUrl = addresses.Addresses.First().TrimEnd('/') + "/lrtest";

    }

    /// <summary>
    /// Stops the host and restores the global client configuration.
    /// </summary>
    [ClassCleanup]
    public static void StopHost() {

      UjmwClientConfiguration.LongRunningWaitSeconds = _OriginalLongRunningWaitSeconds;

      if (_App != null) {
        _App.StopAsync().GetAwaiter().GetResult();
        ((IDisposable)_App).Dispose();
        _App = null;
      }

    }

    /// <summary>
    /// Restores the global client configuration after each test (other test classes rely on its default value
    /// and 'ClassCleanup' may run delayed).
    /// </summary>
    [TestCleanup]
    public void RestoreClientConfiguration() {

      UjmwClientConfiguration.LongRunningWaitSeconds = _OriginalLongRunningWaitSeconds;

    }

    [TestMethod]
    public void DynamicClient_LongRunningOperation_PollsAndReplaysCallbacksInOrder() {

      UjmwClientConfiguration.LongRunningWaitSeconds = 1;
      ILongRunningTestService client = DynamicClientFactory.CreateInstance<ILongRunningTestService>(_BaseUrl);

      List<int> progress = new List<int>();
      string label;

      int result = client.Count(CancellationToken.None, 15, 150, (step) => progress.Add(step), out label);

      Assert.AreEqual(15, result);
      Assert.AreEqual("counted to 15", label);
      CollectionAssert.AreEqual(Enumerable.Range(1, 15).ToArray(), progress.ToArray());

    }

    [TestMethod]
    public void DynamicClient_LongRunningOperation_CompletesWithinWaitTime() {

      UjmwClientConfiguration.LongRunningWaitSeconds = 10;
      ILongRunningTestService client = DynamicClientFactory.CreateInstance<ILongRunningTestService>(_BaseUrl);

      List<int> progress = new List<int>();
      string label;

      int result = client.Count(CancellationToken.None, 3, 0, (step) => progress.Add(step), out label);

      Assert.AreEqual(3, result);
      Assert.AreEqual("counted to 3", label);
      CollectionAssert.AreEqual(new int[] { 1, 2, 3 }, progress.ToArray());

    }

    [TestMethod]
    public void DynamicClient_LongRunningOperation_CancellationIsPropagated() {

      UjmwClientConfiguration.LongRunningWaitSeconds = 1;
      ILongRunningTestService client = DynamicClientFactory.CreateInstance<ILongRunningTestService>(_BaseUrl);

      using (CancellationTokenSource cts = new CancellationTokenSource()) {

        cts.CancelAfter(TimeSpan.FromMilliseconds(1500));

        bool cancelled = false;

        try {
          client.WaitForCancel(cts.Token, 30);
        }
        catch (OperationCanceledException) {
          cancelled = true;
        }

        Assert.IsTrue(cancelled, "An OperationCanceledException was expected.");

      }

    }

    [TestMethod]
    public void DynamicClient_ClassicOperation_StillWorks() {

      ILongRunningTestService client = DynamicClientFactory.CreateInstance<ILongRunningTestService>(_BaseUrl);

      Assert.AreEqual(42, client.Quick(41));

    }

    [TestMethod]
    public void ApiExplorer_LongRunningOperation_ExposesPostGetAndDelete() {

      IApiDescriptionGroupCollectionProvider provider = _App.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>();

      ApiDescription[] countDescriptions = provider.ApiDescriptionGroups.Items.SelectMany(
        (group) => group.Items
      ).Where(
        (description) => description.RelativePath != null && description.RelativePath.EndsWith("lrtest/Count", StringComparison.OrdinalIgnoreCase)
      ).ToArray();

      string[] verbs = countDescriptions.Select((description) => description.HttpMethod).OrderBy((verb) => verb).ToArray();
      string allDescriptions = string.Join(", ", provider.ApiDescriptionGroups.Items.SelectMany((group) => group.Items).Select((d) => d.HttpMethod + " " + d.RelativePath));
      CollectionAssert.AreEqual(new string[] { "DELETE", "GET", "POST" }, verbs, allDescriptions);

      ApiDescription getDescription = countDescriptions.Single((description) => description.HttpMethod == "GET");
      string[] getParams = getDescription.ParameterDescriptions.Select((p) => p.Name).ToArray();
      CollectionAssert.IsSubsetOf(new string[] { "asyncId", "pollingSalt", "pollingHash" }, getParams);

      ApiDescription deleteDescription = countDescriptions.Single((description) => description.HttpMethod == "DELETE");
      string[] deleteParams = deleteDescription.ParameterDescriptions.Select((p) => p.Name).ToArray();
      CollectionAssert.IsSubsetOf(new string[] { "asyncId", "pollingSalt", "pollingHash", "wait" }, deleteParams);

      ApiDescription postDescription = countDescriptions.Single((description) => description.HttpMethod == "POST");
      Assert.IsTrue(postDescription.ParameterDescriptions.Any((p) => p.Name == "wait"), "POST must expose the 'wait' query parameter.");

      ApiDescription[] quickDescriptions = provider.ApiDescriptionGroups.Items.SelectMany(
        (group) => group.Items
      ).Where(
        (description) => description.RelativePath != null && description.RelativePath.EndsWith("lrtest/Quick", StringComparison.OrdinalIgnoreCase)
      ).ToArray();

      Assert.AreEqual(1, quickDescriptions.Length, "Classic operations must only expose POST.");
      Assert.AreEqual("POST", quickDescriptions[0].HttpMethod);

    }

    [TestMethod]
    public void RawHttp_PostWithoutWait_ExecutesSynchronously() {

      JObject response = this.Send(HttpMethod.Post, _BaseUrl + "/Count", "{ \"upTo\": 3, \"delayMs\": 0 }");

      Assert.AreEqual(3, response.Value<int>("return"));
      Assert.AreEqual("counted to 3", response.Value<string>("label"));

      JArray invokes = response["onProgressInvokes"] as JArray;
      Assert.IsNotNull(invokes, "The 'onProgressInvokes' property is missing.");
      Assert.AreEqual(3, invokes.Count);
      Assert.AreEqual(1, invokes[0].Value<int>("#"));
      Assert.AreEqual(1, invokes[0].Value<int>("arg1"));
      Assert.AreEqual(3, invokes[2].Value<int>("arg1"));

    }

    [TestMethod]
    public void RawHttp_PollingAuthentication_AndCancellation() {

      JObject initiated = this.Send(HttpMethod.Post, _BaseUrl + "/WaitForCancel?wait=0", "{ \"maxSeconds\": 30 }");

      JObject asyncState = initiated["async"] as JObject;
      Assert.IsNotNull(asyncState, "The 'async' property is missing.");

      string asyncId = asyncState.Value<string>("asyncId");
      string pollingSecret = asyncState.Value<string>("pollingSecret");
      Assert.IsFalse(string.IsNullOrEmpty(asyncId));
      Assert.IsFalse(string.IsNullOrEmpty(pollingSecret));
      Assert.IsTrue(asyncState.Value<int>("execState") >= 0);

      //wrong hash -> no information about the operation must be exposed
      JObject rejected = this.Send(HttpMethod.Get, _BaseUrl + "/WaitForCancel?asyncId=" + asyncId + "&pollingSalt=abcdef&pollingHash=wrong", null);
      Assert.AreEqual(UjmwAsyncProtocol._WrongPollingAuthFault, rejected.Value<string>("fault"));

      //valid poll
      JObject polled = this.Send(HttpMethod.Get, this.BuildControlUrl("WaitForCancel", asyncId, pollingSecret, null), null);
      Assert.IsTrue(string.IsNullOrEmpty(polled.Value<string>("fault")));
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateRunning, polled["async"].Value<int>("execState"));

      //cancellation (with wait, so that the operation can react)
      JObject cancelled = this.Send(HttpMethod.Delete, this.BuildControlUrl("WaitForCancel", asyncId, pollingSecret, "5"), null);
      Assert.AreEqual(UjmwAsyncProtocol._ExecStateCancelled, cancelled["async"].Value<int>("execState"));

    }

    /// <summary>
    /// Builds a GET/DELETE url including a fresh 'pollingSalt' and the matching 'pollingHash'.
    /// </summary>
    private string BuildControlUrl(string methodName, string asyncId, string pollingSecret, string wait) {

      string pollingSalt = UjmwAsyncProtocol.GenerateRandomAlphanumeric(UjmwAsyncProtocol._PollingSaltLength);
      string pollingHash = UjmwAsyncProtocol.ComputePollingHash(pollingSecret, pollingSalt);

      string url = _BaseUrl + "/" + methodName + "?asyncId=" + asyncId + "&pollingSalt=" + pollingSalt + "&pollingHash=" + pollingHash;

      if (wait != null) {
        url = url + "&wait=" + wait;
      }

      return url;
    }

    /// <summary>
    /// Sends a raw HTTP request and parses the JSON response.
    /// </summary>
    private JObject Send(HttpMethod method, string url, string jsonBody) {

      using (HttpClient httpClient = new HttpClient()) {
        using (HttpRequestMessage request = new HttpRequestMessage(method, url)) {

          if (jsonBody != null) {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
          }

          using (HttpResponseMessage response = httpClient.Send(request)) {

            string rawResponse = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.IsTrue(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode} for {method} {url}: {rawResponse}");

            return JObject.Parse(rawResponse);
          }

        }
      }

    }

  }

}
#endif
