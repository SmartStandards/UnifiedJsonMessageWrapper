//using Microsoft.AspNetCore.Builder;
//using Microsoft.AspNetCore.Hosting;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.OpenApi.Models;
//using System;
//using Microsoft.AspNetCore.Mvc.Formatters;
//using System.Threading.Tasks;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Logging.Configuration;
//using System.IO;
//using Swashbuckle.AspNetCore.SwaggerGen;
//using Microsoft.OpenApi.Writers;
//using Security.AccessTokenHandling;
//using Microsoft.AspNetCore.Authentication.Negotiate;
//using Security.AccessTokenHandling.OAuthServer;
//using Microsoft.AspNetCore.Mvc.ApplicationParts;
//using Microsoft.AspNetCore.Mvc.Controllers;
//using System.Collections.Generic;
//using System.Linq;
//using Microsoft.AspNetCore.Mvc;
//using System.Reflection;
//using Microsoft.AspNetCore.Mvc.ApplicationModels;
//using System.Web.UJMW;
//using Microsoft.AspNetCore.Authorization;

//namespace Security {

//      //services.AddSwaggerGen(c => {
//      //  c.ResolveConflictingActions(apiDescriptions => {
//      //    return apiDescriptions.First();
//      //  });

//      //services.AddControllers(o =>
//      //{
//      //  //o.Conventions.Add(new ControllerHidingConvention());
//      //});

//public class DemoService {

//    //public string Foo(int bar) {
//    //  return "you entered: " + bar.ToString();
//    //}

//    public string Foo2(int bar, out string baz, ref string boo) {
//      baz = "abcdefg";
//      boo = boo + baz;
//      //throw new NotImplementedException();
//      return "you entered: " + bar.ToString();
//    }

//    //1. aufruf aus usehll profileselectorwidget
//    public string[] GetAllowedPortfoliosForCurrentIdentity() {
//      return new string[] { };
//    }

//    //2. aufruf aus ushell - boot logik
//    public string[] GetPortfolioDescription(string portfolioName) {
//      return new string[] { };
//      //ist so konfguriert, dass die darin gelieferte applicationsciope steht mandantename
//    }

//    //3. bei allen ushell requests muss der applicationscope-tenant immer mit gesendet werden
//    //    "_" property, also ambient

//    //4. bei eingehenden requests prüft der service, ob der token auch einen zum aktuelle angefordrrtent tenant
//    //passenden scoep enthält

//    //5. vom service aus wird mit der tenent information die constring geladen
//    public string[] GetConnectionStringsForTenant(string tanant) {
//      return new string[] { };
//    }

//  }

//  //public class GenericTypeControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature> {

//  //  //services.
//  //  //  AddMvc(o => o.Conventions.Add(new GenericControllerRouteConvention(true))).
//  //  //  ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new GenericTypeControllerFeatureProvider())
//  //  //);

//  //  public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
//  //    var currentAssembly = typeof(GenericTypeControllerFeatureProvider).Assembly;
//  //    var candidates = currentAssembly.GetExportedTypes().Where(x => x.GetCustomAttributes<GeneratedControllerAttribute>().Any());

//  //    foreach (var candidate in candidates) {

//  //      feature.Controllers.Add(typeof(BaseController<>).MakeGenericType(typeof(AEntity)).GetTypeInfo());
//  //      feature.Controllers.Add(typeof(BaseController<>).MakeGenericType(typeof(BEntity)).GetTypeInfo());

//  //    }
//  //  }
//  //}

//  public class GenericControllerRouteConvention : IControllerModelConvention {

//    private bool _LowerCase;

//    public GenericControllerRouteConvention(bool lowerCase) {
//      _LowerCase = lowerCase;
//    }

//    public void Apply(ControllerModel controller) {
//      if (controller.ControllerType.IsGenericType) {
//        string controllerTypeName = controller.ControllerType.GetGenericTypeDefinition().Name;
//        controllerTypeName = controllerTypeName.Substring(0, controllerTypeName.IndexOf("`"));
//        controller.ControllerName = controllerTypeName + "s";
//        for (int i = 0; i <= (controller.Selectors.Count - 1); i++) {
//          if (controller.Selectors[0].AttributeRouteModel.Template == "api/[controller]") {
//            string genTypeName = controller.ControllerType.GenericTypeArguments[0].Name;
//            string newRoute = "api/" + controller.ControllerName + "/" + genTypeName;
//            if (_LowerCase) {
//              newRoute = newRoute.ToLower();
//            }
//            controller.Selectors[0].AttributeRouteModel.Template = newRoute;
//          }
//        }

//        //controller.ApiExplorer.IsVisible = false;

//        //var genericType = controller.ControllerType.GenericTypeArguments[0];
//        //var customNameAttribute = genericType.GetCustomAttribute<GeneratedControllerAttribute>();

//        //if (customNameAttribute?.Route != null) {

//        //controller.Selectors.Add(new SelectorModel {
//        //  AttributeRouteModel = new AttributeRouteModel(
//        //    new RouteAttribute(genericType.Name.ToLower())
//        //  //new RouteAttribute(customNameAttribute.Route)
//        //  ),
//        //});

//        //}
//      }
//    }
//  }

//  //public class ControllerHidingConvention : IControllerModelConvention {
//  //  public void Apply(ControllerModel controller) {
//  //    if (controller.ControllerName.Contains("`")) {
//  //      controller.ControllerName = "(GENERIC)";
//  //      if (controller.Selectors[0].AttributeRouteModel.Template  == "api/[controller]") {
//  //        string entityName = controller.ControllerType.GenericTypeArguments[0].Name;
//  //        controller.Selectors[0].AttributeRouteModel.Template = "api/" + entityName;
//  //      }
//  //      //controller.ApiExplorer.IsVisible = false;
//  //    }
//  //  }
//  //}

//  public class AEntity {
//    public string NameA { get; set; }
//    public string DescriptionA { get; set; }
//  }

//  public class BEntity {
//    public string NameB { get; set; }
//    public string DescriptionB { get; set; }
//  }

//  [Route("api/[controller]")]
//  public class BaseController<T> : Controller where T : class {
//    private static Dictionary<Guid, T> _storage = new Dictionary<Guid, T>();

//    public BaseController() {
//    }

//    [HttpGet]
//    public IEnumerable<T> Get() {
//      return _storage.Values;
//    }

//    [HttpGet("{id}")]
//    public T Get(Guid id) {
//      return _storage[id];
//    }

//    [HttpPost("{id}")]
//    public void Post(Guid id, [FromBody] T value) {
//      _storage[id] = value;
//    }
//  }

//}