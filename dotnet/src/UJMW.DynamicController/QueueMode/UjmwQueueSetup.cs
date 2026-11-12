using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace System.Web.UJMW {

  /// <summary>
  /// DI wire-up of the UJMW async queue mode (see 'doc/epics/epic-async-queue-mode.md', 3.3).
  /// </summary>
  internal static class UjmwQueueSetup {

    /// <summary>
    /// Registers the default 'UjmwInMemoryQueuingService' (only if no custom 'IUjmwQueuingService' was registered before)
    /// and the hosted service which handles the shutdown.
    /// </summary>
    public static void RegisterQueueServices(IServiceCollection services) {

      bool queuingServiceRegistered = false;
      bool hostedServiceRegistered = false;

      foreach (ServiceDescriptor descriptor in services) {

        if (descriptor.ServiceType == typeof(IUjmwQueuingService)) {
          queuingServiceRegistered = true;
        }

        if (descriptor.ImplementationType == typeof(UjmwQueueHostedService)) {
          hostedServiceRegistered = true;
        }

      }

      if (!queuingServiceRegistered) {
        services.AddSingleton<IUjmwQueuingService>((IServiceProvider sp) => new UjmwInMemoryQueuingService());
      }

      if (!hostedServiceRegistered) {
        services.AddSingleton<IHostedService, UjmwQueueHostedService>();
      }

    }

  }

  /// <summary>
  /// On application stop: cancels the outer operations of all queue bridges first and then
  /// signals the shutdown to the default in-memory queuing service (decided Q-SHUT).
  /// </summary>
  internal class UjmwQueueHostedService : IHostedService {

    private readonly IServiceProvider _ServiceProvider;

    /// <summary>
    /// Creates the hosted service.
    /// </summary>
    public UjmwQueueHostedService(IServiceProvider serviceProvider) {
      _ServiceProvider = serviceProvider;
    }

    /// <summary>
    /// Nothing to do on startup.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) {
      return Task.CompletedTask;
    }

    /// <summary>
    /// Shuts down the bridges and the in-memory queuing service.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) {

      UjmwQueueRegistry.ShutdownBridges();

      UjmwInMemoryQueuingService inMemoryService = _ServiceProvider.GetService<IUjmwQueuingService>() as UjmwInMemoryQueuingService;
      if (inMemoryService != null) {
        inMemoryService.Shutdown();
      }

      return Task.CompletedTask;
    }

  }

}
