using Microsoft.AspNetCore.Mvc.Controllers;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Web.UJMW.SelfAnnouncement;

namespace System.Web.UJMW {

  partial class DynamicUjmwControllerRegistrar {

    private List<Tuple<Type, DynamicUjmwControllerOptions>> _QueueEntries = new List<Tuple<Type, DynamicUjmwControllerOptions>>();

    /// <summary>
    /// True, if at least one queue-mode controller was requested (used to register the default queuing service).
    /// </summary>
    internal bool HasQueueEntries {
      get {
        return _QueueEntries.Count > 0;
      }
    }

    /// <summary>
    /// Registers a queue-mode controller for the given contract (async queue mode, see 'doc/epics/epic-async-queue-mode.md').
    /// The calls are not executed by a local implementation, but enqueued into the 'IUjmwQueuingService'
    /// and processed by queue processors via '&lt;route&gt;/_queue'.
    /// </summary>
    public void AddQueueFor<TService>(string controllerRoute) {
      this.AddQueueFor(typeof(TService), new DynamicUjmwControllerOptions { ControllerRoute = controllerRoute });
    }

    /// <summary>
    /// Registers a queue-mode controller for the given contract (see 'AddQueueFor(Type, DynamicUjmwControllerOptions)').
    /// </summary>
    public void AddQueueFor<TService>(Action<DynamicUjmwControllerOptions> optionsConfigurator) {
      DynamicUjmwControllerOptions options = new DynamicUjmwControllerOptions();
      optionsConfigurator.Invoke(options);
      this.AddQueueFor(typeof(TService), options);
    }

    /// <summary>
    /// Registers a queue-mode controller for the given contract (see 'AddQueueFor(Type, DynamicUjmwControllerOptions)').
    /// </summary>
    public void AddQueueFor<TService>(DynamicUjmwControllerOptions options = null) {
      this.AddQueueFor(typeof(TService), options);
    }

    /// <summary>
    /// Registers a queue-mode controller for the given contract (see 'AddQueueFor(Type, DynamicUjmwControllerOptions)').
    /// </summary>
    public void AddQueueFor(Type serviceType, string controllerRoute) {
      this.AddQueueFor(serviceType, new DynamicUjmwControllerOptions { ControllerRoute = controllerRoute });
    }

    /// <summary>
    /// Registers a queue-mode controller for the given contract (see 'AddQueueFor(Type, DynamicUjmwControllerOptions)').
    /// </summary>
    public void AddQueueFor(Type serviceType, Action<DynamicUjmwControllerOptions> optionsConfigurator) {
      DynamicUjmwControllerOptions options = new DynamicUjmwControllerOptions();
      optionsConfigurator.Invoke(options);
      this.AddQueueFor(serviceType, options);
    }

    /// <summary>
    /// Registers a queue-mode controller for the given contract. Sub-services (properties of the contract)
    /// are not supported (decided Q-SUB) and are rejected immediately.
    /// </summary>
    public void AddQueueFor(Type serviceType, DynamicUjmwControllerOptions options = null) {

      if (serviceType == null) {
        throw new ArgumentNullException(nameof(serviceType));
      }

      List<PropertyInfo> allProperties = new List<PropertyInfo>();
      CollectAllPropertiesForType(serviceType, allProperties);

      foreach (PropertyInfo property in allProperties) {
        if (property.CanRead && !property.PropertyType.IsValueType) {
          throw new NotSupportedException(
            $"The contract '{serviceType.FullName}' exposes the sub-service property '{property.Name}', which is not supported by the UJMW queue mode!"
          );
        }
      }

      if (options == null) {
        options = new DynamicUjmwControllerOptions();
      }

      _QueueEntries.Add(new Tuple<Type, DynamicUjmwControllerOptions>(serviceType, options));

      if (_GrabbedControllerFeature != null) {
        CreateAndRegisterQueueControllers(_GrabbedControllerFeature, serviceType, options);
      }

    }

    private void PopulateQueueControllers(ControllerFeature feature) {

      foreach (Tuple<Type, DynamicUjmwControllerOptions> entry in _QueueEntries) {
        CreateAndRegisterQueueControllers(feature, entry.Item1, entry.Item2);
      }

    }

    /// <summary>
    /// Emits the contract controller (constructor dependency: 'IUjmwQueuingService') and the '_queue' controller.
    /// </summary>
    private static void CreateAndRegisterQueueControllers(ControllerFeature feature, Type serviceType, DynamicUjmwControllerOptions options) {

      ModuleBuilder builder;
      if (UjmwHostConfiguration.UseCombinedDynamicAssembly) {
        if (_CombinedBuilder == null) {
          _CombinedBuilder = DynamicUjmwControllerFactory.CreateAssemblyModuleBuilder("UJMW.InMemoryControllers");
        }
        builder = _CombinedBuilder;
      }
      else {
        builder = DynamicUjmwControllerFactory.CreateAssemblyModuleBuilder("UJMW.InMemoryControllers.Queue." + serviceType.Name);
      }

      string apiGroupNameWithFallback = options.ApiGroupName;
      if (apiGroupNameWithFallback == null && UjmwHostConfiguration.EnableApiGroupNameFallback) {
        apiGroupNameWithFallback = serviceType.Assembly.GetName().Name;
      }

      Type contractController = DynamicUjmwControllerFactory.BuildDynamicControllerType(
        serviceType, options,
        out string resolvedControllerRoute, out string controllerTitle, out string resolvedControllerName,
        builder, typeof(IUjmwQueuingService), apiGroupNameWithFallback
      );

      UjmwQueueRegistry.RegisterController(contractController, serviceType, resolvedControllerRoute);
      feature.Controllers.Add(contractController.GetTypeInfo());

      SelfAnnouncementHelper.RegisterEndpoint(
        serviceType, controllerTitle, resolvedControllerRoute,
        EndpointCategory.DynamicUjmwFacade, options, apiGroupNameWithFallback
      );

      Type queueController = DynamicUjmwControllerFactory.BuildQueueControllerType(
        serviceType, options, resolvedControllerRoute, resolvedControllerName, builder, apiGroupNameWithFallback
      );

      feature.Controllers.Add(queueController.GetTypeInfo());

    }

  }

}
