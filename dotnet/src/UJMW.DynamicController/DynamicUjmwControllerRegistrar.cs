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

    public void AddControllerFor<TService>(Action<DynamicUjmwControllerOptions> optionsConfigurator) {
      DynamicUjmwControllerOptions opt = new DynamicUjmwControllerOptions();
      optionsConfigurator.Invoke(opt);
      AddControllerFor<TService>(opt);
    }
    public void AddControllerFor<TService>(DynamicUjmwControllerOptions options = null) {
      this.AddControllerFor(typeof(TService), options);
    }

    public void AddControllerFor(Type serviceType, string controllerRoute) {
      this.AddControllerFor(serviceType, new DynamicUjmwControllerOptions { ControllerRoute = controllerRoute });
    }

    public void AddControllerFor(Type serviceType, Action<DynamicUjmwControllerOptions> optionsConfigurator) {
      DynamicUjmwControllerOptions opt = new DynamicUjmwControllerOptions();
      optionsConfigurator.Invoke(opt);
      AddControllerFor(serviceType, opt);
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

      CreateAndRegisterController(feature, serviceType, serviceType, options, builder);
    }

    private static void CreateAndRegisterController(
      ControllerFeature feature, Type serviceTypeForCurrentController, Type serviceTypeForRootController, DynamicUjmwControllerOptions options, ModuleBuilder builder
    ) {

      Type dynamicController = DynamicUjmwControllerFactory.BuildDynamicControllerType(
        serviceTypeForCurrentController, options, 
        out string resolvedControllerRoute, out string controllerTitle, out string resolvedControllerName,
        builder, serviceTypeForRootController
      );

      feature.Controllers.Add(dynamicController.GetTypeInfo());

      SelfAnnouncementHelper.RegisterEndpoint(
        serviceTypeForCurrentController, controllerTitle, resolvedControllerRoute, EndpointCategory.DynamicUjmwFacade, options
      );


      List<PropertyInfo> allProperties = new List<PropertyInfo>();
      CollectAllPropertiesForType(serviceTypeForCurrentController, allProperties);
      foreach (PropertyInfo subServiceProperty in allProperties) {
      
        if(subServiceProperty.CanRead && !subServiceProperty.PropertyType.IsValueType) {

          DynamicUjmwControllerOptions dedicatedOptions = options.Clone();

          //to risky to duplicate... reset to default automatic!
          dedicatedOptions.ControllerTitle = null;
          dedicatedOptions.ControllerNamePattern = $"{resolvedControllerName}.{subServiceProperty.Name}";
           
          //let the route step-down
          dedicatedOptions.ControllerRoute = $"{resolvedControllerRoute}/{subServiceProperty.Name}";
          dedicatedOptions.WrapperNamePattern = $"{resolvedControllerName}_{subServiceProperty.Name}_[Method]";

          //let the navigation path (to request the service instance) step-down
          var dedicatedPath  = dedicatedOptions.SubServiceNavPath.ToList();
          dedicatedPath.Add(subServiceProperty);
          dedicatedOptions.SubServiceNavPath = dedicatedPath.ToArray();

          //recurse!
          CreateAndRegisterController(feature, subServiceProperty.PropertyType, serviceTypeForRootController, dedicatedOptions, builder);  
        }
      }

    }

    internal static void CollectAllPropertiesForType(Type t, List<PropertyInfo> target) {
      foreach (PropertyInfo pi in t.GetProperties()) {
        target.Add(pi);
      }
      if (t.BaseType != null) {
        CollectAllPropertiesForType(t.BaseType, target);
      }
      foreach (Type intf in t.GetInterfaces()) {
        CollectAllPropertiesForType(intf, target);
      }
    }

  }

}
