using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace System.Web.UJMW {

  public sealed class DynamicUjmwControllerRegistrar : IApplicationFeatureProvider<ControllerFeature> {

    internal DynamicUjmwControllerRegistrar() {
    }

    private Dictionary<Type, DynamicUjmwControllerOptions> _Entries = new Dictionary<Type, DynamicUjmwControllerOptions>();
    private ControllerFeature _GrabbedControllerFeature = null;

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

    void IApplicationFeatureProvider<ControllerFeature>.PopulateFeature(
      IEnumerable<ApplicationPart> parts, ControllerFeature feature
    ) {
      foreach (var _Entry in _Entries) {
        DynamicUjmwControllerRegistrar.CreateAndRegisterController(feature, _Entry.Key, _Entry.Value);
      }
      _GrabbedControllerFeature = feature;
    }

    private static void CreateAndRegisterController(ControllerFeature feature, Type serviceType, DynamicUjmwControllerOptions options) {
      Type dynamicController = DynamicUjmwControllerFactory.BuildDynamicControllerType(serviceType, options, out string controllerRoute);
      feature.Controllers.Add(dynamicController.GetTypeInfo());
      lock (_AllRegisteredServiceTypesByRoute) {
        _AllRegisteredServiceTypesByRoute[controllerRoute] = serviceType;
      }
    }

    private static Dictionary<string, Type> _AllRegisteredServiceTypesByRoute = new Dictionary<string, Type>();

    public static KeyValuePair<string, Type>[] GetAllRegisteredServiceTypesByRoute() {
      lock (_AllRegisteredServiceTypesByRoute) {
        return _AllRegisteredServiceTypesByRoute.ToArray();
      }
    }

  }

}
