#if NET5
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;

namespace System.Web.UJMW {

  /// <summary>
  /// Contract used to validate the async queue mode end-to-end (queue host + remote processor + dynamic client over real HTTP).
  /// </summary>
  public interface IQueueEndToEndService {

    /// <summary>
    /// A classic (synchronous) operation.
    /// </summary>
    int Add(int a, int b);

    /// <summary>
    /// A long-running operation reporting its progress via callback.
    /// </summary>
    string Render(CancellationToken cancellationToken, int steps, Action<int> onProgress);

  }

  /// <summary>
  /// Contract exposing a sub-service (not supported by the queue mode, decided Q-SUB).
  /// </summary>
  public interface IQueueContractWithSubService {

    IQueueEndToEndService Sub { get; }

  }

  internal class QueueEndToEndService : IQueueEndToEndService {

    public int Add(int a, int b) {
      return a + b;
    }

    public string Render(CancellationToken cancellationToken, int steps, Action<int> onProgress) {

      for (int i = 1; i <= steps; i++) {
        cancellationToken.ThrowIfCancellationRequested();
        Thread.Sleep(50);
        onProgress.Invoke(i);
      }

      return "rendered " + steps.ToString();
    }

  }

  [TestClass]
  public class QueueModeEndToEndTests {

    private static WebApplication _App;
    private static string _BaseUrl;
    private static int _OriginalLongRunningWaitSeconds;

    /// <summary>
    /// Hosts a queue-mode controller for 'IQueueEndToEndService' on a dynamic loopback port.
    /// </summary>
    [ClassInitialize]
    public static void StartHost(TestContext context) {

      _OriginalLongRunningWaitSeconds = UjmwClientConfiguration.LongRunningWaitSeconds;

      //short poll intervals to keep the tests fast
      UjmwQueueOptions queueOptions = new UjmwQueueOptions();
      UjmwLongRunningOptions longRunningOptions = new UjmwLongRunningOptions();
      longRunningOptions.CalculateNbf = (operationStartTime, lastPollTime, overallActiveOperations) => lastPollTime.AddMilliseconds(200);
      queueOptions.LongRunningOptions = longRunningOptions;

      WebApplicationBuilder builder = WebApplication.CreateBuilder();
      builder.WebHost.UseUrls("http://127.0.0.1:0");

      builder.Services.AddSingleton<IUjmwQueuingService>(new UjmwInMemoryQueuingService(queueOptions));
      builder.Services.AddDynamicUjmwControllers((r) => {
        r.AddQueueFor<IQueueEndToEndService>((c) => {
          c.ControllerRoute = "qtest";
          c.ApiGroupName = "QueueTest";
          c.EnableAuthHeaderEvaluatorHook = false;
        });
      });

      _App = builder.Build();
      _App.MapControllers();
      _App.StartAsync().GetAwaiter().GetResult();

      IServer server = _App.Services.GetRequiredService<IServer>();
      IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>();
      _BaseUrl = addresses.Addresses.First().TrimEnd('/') + "/qtest";

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

    private static QueueProcessor<IQueueEndToEndService> StartRemoteProcessor() {

      UjmwQueueClient queueClient = new UjmwQueueClient((Type contractType) => _BaseUrl);

      QueueProcessor<IQueueEndToEndService> processor = new QueueProcessor<IQueueEndToEndService>(
        new QueueEndToEndService(), queueClient, 2
      );
      processor.Start(0);

      return processor;
    }

    [TestMethod]
    public void QueueHost_SynchronousCall_IsProcessedByRemoteProcessor() {

      IQueueEndToEndService client = DynamicClientFactory.CreateInstance<IQueueEndToEndService>(_BaseUrl);

      using (QueueProcessor<IQueueEndToEndService> processor = StartRemoteProcessor()) {
        Assert.AreEqual(5, client.Add(2, 3));
        Assert.AreEqual(42, client.Add(40, 2));
      }

    }

    [TestMethod]
    public void QueueHost_LongRunningCall_DeliversProgressAndResult() {

      int originalWaitSeconds = UjmwClientConfiguration.LongRunningWaitSeconds;

      //the global setting is restored immediately, because other test classes rely on its default value
      try {

        UjmwClientConfiguration.LongRunningWaitSeconds = 1;
        IQueueEndToEndService client = DynamicClientFactory.CreateInstance<IQueueEndToEndService>(_BaseUrl);

        List<int> progress = new List<int>();

        using (QueueProcessor<IQueueEndToEndService> processor = StartRemoteProcessor()) {

          string result = client.Render(CancellationToken.None, 5, (step) => progress.Add(step));

          Assert.AreEqual("rendered 5", result);
          CollectionAssert.AreEqual(new int[] { 1, 2, 3, 4, 5 }, progress.ToArray());

        }

      }
      finally {
        UjmwClientConfiguration.LongRunningWaitSeconds = originalWaitSeconds;
      }

    }

    [TestMethod]
    public void QueueHost_InfoEndpoint_ReturnsStatisticsWithoutCaching() {

      using (HttpClient httpClient = new HttpClient()) {

        HttpResponseMessage response = httpClient.GetAsync(_BaseUrl + "/_queue").GetAwaiter().GetResult();
        string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        Assert.AreEqual(200, (int)response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl.NoStore);

        JObject json = JObject.Parse(content);
        Assert.IsNotNull(json["statsTotal"]);
        Assert.AreEqual(JTokenType.Null, json["fault"].Type);

      }

    }

    [TestMethod]
    public void QueueHost_DequeueWithInvalidFilter_ReturnsFault() {

      using (HttpClient httpClient = new HttpClient()) {

        string url = _BaseUrl + "/_queue?processor=test&dequeue=" + Uri.EscapeDataString("a,!b");
        string content = httpClient.GetStringAsync(url).GetAwaiter().GetResult();

        JObject json = JObject.Parse(content);
        Assert.AreEqual(JTokenType.String, json["fault"].Type);
        Assert.AreEqual(JTokenType.Null, json["requestId"].Type);

      }

    }

    [TestMethod]
    public void AddQueueFor_ContractWithSubService_IsRejected() {

      bool rejected = false;

      ServiceCollection services = new ServiceCollection();
      try {
        services.AddDynamicUjmwControllers((r) => {
          r.AddQueueFor<IQueueContractWithSubService>("subtest");
        });
      }
      catch (NotSupportedException) {
        rejected = true;
      }

      Assert.IsTrue(rejected, "A NotSupportedException was expected.");

    }

  }

}
#endif
