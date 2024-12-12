using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Web.UJMW.SelfAnnouncement;

namespace System.Web.UJMW {

  public sealed class DynamicUjmwControllerRegistrar : IApplicationFeatureProvider<ControllerFeature> {

    internal DynamicUjmwControllerRegistrar() {
    }

    private Dictionary<Type, DynamicUjmwControllerOptions> _Entries = new Dictionary<Type, DynamicUjmwControllerOptions>();
    private ControllerFeature _GrabbedControllerFeature = null;

    void IApplicationFeatureProvider<ControllerFeature>.PopulateFeature(
      IEnumerable<ApplicationPart> parts, ControllerFeature feature
    ) {
      foreach (var _Entry in _Entries) {
        DynamicUjmwControllerRegistrar.CreateAndRegisterController(feature, _Entry.Key, _Entry.Value);
      }

      if (_AnnouncementTriggerEndpointRequested) {
        Type controllerType = typeof(AnnouncementTriggerEndpointController);
        feature.Controllers.Add(controllerType.GetTypeInfo());
        SelfAnnouncementHelper.RegisterEndpoint(
          "UJMW.AnnouncementTriggerEndpoint",
          "UJMW AnnouncementTriggerEndpoint",
          AnnouncementTriggerEndpointController.Route,
           EndpointCategory.AnnouncementTriggerEndpoint
        );
      }

      _GrabbedControllerFeature = feature;
    }

    public void AddControllerFor<TService>(string controllerRoute) {
      this.AddControllerFor(typeof(TService), new DynamicUjmwControllerOptions { ControllerRoute = controllerRoute });
    }

    public void AddControllerFor<TService>(DynamicUjmwControllerOptions options = null) {
      this.AddControllerFor(typeof(TService), options);
    }

    public void AddControllerFor(Type serviceType, string controllerRoute) {
      this.AddControllerFor(serviceType, new DynamicUjmwControllerOptions { ControllerRoute = controllerRoute });
    }

    public void AddControllerFor(Type serviceType, DynamicUjmwControllerOptions options = null) {
      _Entries.Add(serviceType, options);
      if (_GrabbedControllerFeature != null) {
        DynamicUjmwControllerRegistrar.CreateAndRegisterController(_GrabbedControllerFeature, serviceType, options);
      }
    }

    private bool _AnnouncementTriggerEndpointRequested = false;
    public void AddAnnouncementTriggerEndpoint() {
      _AnnouncementTriggerEndpointRequested = true;
    }

    private static ModuleBuilder _CombinedBuilder = null;

    private static void CreateAndRegisterController(ControllerFeature feature, Type serviceType, DynamicUjmwControllerOptions options) {

      ModuleBuilder builder;
      if (UjmwHostConfiguration.UseCombinedDynamicAssembly) {
        if (_CombinedBuilder == null) {
          _CombinedBuilder = DynamicUjmwControllerFactory.CreateAssemblyModuleBuilder("UJMW.InMemoryControllers");
        }
        builder = _CombinedBuilder;
      }
      else {
        builder = DynamicUjmwControllerFactory.CreateAssemblyModuleBuilder("UJMW.InMemoryControllers." + serviceType.Name);
      }

      Type dynamicController = DynamicUjmwControllerFactory.BuildDynamicControllerType(
        serviceType, options, out string controllerRoute, out string controllerTitle, builder
      );

      feature.Controllers.Add(dynamicController.GetTypeInfo());

      SelfAnnouncementHelper.RegisterEndpoint(
        serviceType, controllerTitle, controllerRoute, EndpointCategory.DynamicUjmwFacade, options
      );

    }

  }

}
