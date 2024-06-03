using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace System.Web.UJMW {

  public static class SetupExtensions {

    public static void AddDynamicUjmwControllers(this IServiceCollection services, Action<DynamicUjmwControllerRegistrar> configMethod) {
      IMvcBuilder builder = services.AddMvc(
        //o => o.Conventions.Add(new GenericControllerRouteConvention(true))
      );

      var registrar = new DynamicUjmwControllerRegistrar();

      configMethod.Invoke(registrar);

      builder.ConfigureApplicationPartManager(
        (apm) => apm.FeatureProviders.Add(registrar)
      );

    }

    internal static bool TryGetValue(this IHeaderDictionary headers, string headerName, out string headerValue) {
      foreach (var entry in headers) {
        if (entry.Key.Equals(headerName, StringComparison.InvariantCultureIgnoreCase)) {
          headerValue = entry.Value.ToString();
          return true;
        }
      }
      headerValue = null;
      return false;
    }

  }

}
