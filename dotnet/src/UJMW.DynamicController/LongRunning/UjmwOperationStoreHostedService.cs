using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace System.Web.UJMW {

  /// <summary>
  /// Binds the lifetime of the UJMW operation store to the ASP.NET Core application:
  /// when the application stops, all running long-running operations get their cancellation token triggered.
  /// </summary>
  internal class UjmwOperationStoreHostedService : IHostedService {

    /// <summary>
    /// Nothing to do on startup (the store is created on demand).
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) {
      return Task.CompletedTask;
    }

    /// <summary>
    /// Cancels all running operations.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) {

      UjmwHostConfiguration.ShutdownOperationStore();

      return Task.CompletedTask;
    }

  }

}
