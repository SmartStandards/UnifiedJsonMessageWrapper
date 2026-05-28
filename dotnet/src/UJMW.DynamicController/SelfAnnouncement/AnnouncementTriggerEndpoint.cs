using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace System.Web.UJMW.SelfAnnouncement {

  [Route(Route)]
  [ApiExplorerSettings(GroupName = AnnouncementTriggerEndpointController.ApiGroupName)]
  internal class AnnouncementTriggerEndpointController : Controller {

    private readonly IApiDescriptionGroupCollectionProvider _Provider;

    public AnnouncementTriggerEndpointController(IApiDescriptionGroupCollectionProvider provider) {
      _Provider = provider;
    }

    public const string ApiGroupName = "EndpointIndex";

    public const string Route = "_";

    [HttpGet(), Produces("application/json")]
    public IActionResult ShowState() {

      ApiDescriptionGroup[] groups = _Provider.ApiDescriptionGroups.Items.Where(
         (g) => !string.IsNullOrWhiteSpace(g.GroupName)
      ).Distinct().ToArray();

      List<Tuple<string, string>> endpointQualifiedNames = new();
      Dictionary<Type, string> nonUjmwControllersWithApiGroupName = new();


      foreach (ApiDescriptionGroup group in groups) {

        // Ressolve descriptors to real Controller-Types
        Type[] resolvableControllerTypes = TryResolveControllerTypesForApiGroup(group);

        // Include XML-Documentation for all resolvable Controller-Types
        foreach (Type controllerType in resolvableControllerTypes) {

          Type implementationType = controllerType;

          // special knowledge about UJMW Facade-Controllers:
          // If the controller is from a dynamic assembly and its name contains "UJMW",
          // then we try to look for the first constructor and take the type of its first
          // parameter as the "real" implementation type, which might be located in a different
          // assembly that has a version number.
          if (controllerType.Assembly.IsDynamic && controllerType.Assembly.FullName.Contains("UJMW")) {
            ConstructorInfo ujmwFacedeCtor = controllerType.GetConstructors().FirstOrDefault();
            if (ujmwFacedeCtor != null) {
              Type firstConstructorParamType = ujmwFacedeCtor.GetParameters().Select(p => p.ParameterType).FirstOrDefault();
              if (firstConstructorParamType != null) {
                implementationType = firstConstructorParamType;
              }
            }

            //TODO: if there is only one method, the returnes url is wrong because it contains the method name!
            string topmostRoute = FindTopmostRouteForControllerType(group, controllerType);

            endpointQualifiedNames.Add(
              new Tuple<string, string>(
                "UJMW:" + TypeAliasBuilder.BuildTypeAliasRecursive(implementationType, false, 1, null) +
                "/" + implementationType.Assembly.GetName().Version?.ToString(3),
                topmostRoute
              )
            );

          }
          else if (controllerType == typeof(AnnouncementTriggerEndpointController)) {

            endpointQualifiedNames.Add(new Tuple<string, string>("EndpointIndex/1.0.0", "./" + Route));

          }
          else {

            if (group.GroupName != "hidden") {

               string topmostRoute = FindTopmostRouteForControllerType(group, controllerType);

               endpointQualifiedNames.Add(
                new Tuple<string, string>(
                  group.GroupName + ":" + TypeAliasBuilder.BuildTypeAliasRecursive(implementationType, true, 1, null) +
                  "/" + implementationType.Assembly.GetName().Version?.ToString(3),
                  topmostRoute
                )
              );

            }



            //group

            //endpointQualifiedNames.Add("EndpointIndex", Route);

            //nonUjmwControllersWithApiGroupName[]
          }

 
             
          ////also add the group itself!
          //endpointQualifiedNames.Add(new Tuple<string,string>(group.GroupName, topmostRoute));


        }
 
      }

      return Ok(endpointQualifiedNames);

      //if (string.IsNullOrWhiteSpace(SelfAnnouncementHelper.LastFault)) {
      //  return Ok(new {
      //    lastActionTime = SelfAnnouncementHelper.LastActionTime,
      //    lastAction = SelfAnnouncementHelper.LastAction,
      //    baseUrls = SelfAnnouncementHelper.BaseUrls,
      //    endpoints = GetReducedEndpointInfo(),
      //    lastAddInfo = SelfAnnouncementHelper.LastAddInfo,
      //    lastFault = SelfAnnouncementHelper.LastFault
      //  });
      //}
      //else {
      //  return Ok(new {
      //    lastActionTime = SelfAnnouncementHelper.LastActionTime,
      //    lastAction = SelfAnnouncementHelper.LastAction,
      //    lastFault = SelfAnnouncementHelper.LastFault,
      //    lastAddInfo = SelfAnnouncementHelper.LastAddInfo,
      //  });
      //}

    }

    internal static string FindTopmostRouteForControllerType(ApiDescriptionGroup group, Type controllerType) {

      //first check if the controller has a controllerroute-attribute:
      string controllerWideRoute = controllerType.GetCustomAttributes(true).OfType<RouteAttribute>().FirstOrDefault()?.Template?.ToString();
      if (!string.IsNullOrWhiteSpace(controllerWideRoute)) {
        return "./" + controllerWideRoute;
      }

      //fallback: try to evaluate it from the concrete actions, provided by this controller...

      ControllerActionDescriptor[] controllerActions = group.Items.Select(
        (item) => item.ActionDescriptor
      ).OfType<ControllerActionDescriptor>().Where(
        (cad) => cad.ControllerTypeInfo != null && cad.ControllerTypeInfo.AsType() == controllerType
      ).ToArray();

      string[] routeTemplates = controllerActions.Select(
        (cad) => "./" + cad.AttributeRouteInfo?.Template
      ).Distinct().ToArray();

      string root = "./";
      if (routeTemplates.Length == 0) {
        return root;
      }

      //now find the topmost route to assume the entry url that is corresponding to this descriptive 'endpoint':
      RecurseWhileSameRoot(ref root, routeTemplates);
 
      return root;
    }

    private static void RecurseWhileSameRoot(ref string root, string[] routeTemplates) {

      if (routeTemplates.Length == 0) {
        return;
      }

      string potDeeperRoot = routeTemplates[0];
      int slashIdx = potDeeperRoot.Substring(root.Length).IndexOf('/');
      if(slashIdx > -1) {
        potDeeperRoot = potDeeperRoot.Substring(0, root.Length + slashIdx + 1);
      }
      if (root == potDeeperRoot) {
        return; //not possible to get deeper
      }

      foreach (string routeTemplate in routeTemplates) {
        if ((routeTemplate + "/").StartsWith(potDeeperRoot) == false) {
          return;
        }
      }

      root = potDeeperRoot;
      RecurseWhileSameRoot(ref root, routeTemplates);
    }

    internal static Type[] TryResolveControllerTypesForApiGroup(ApiDescriptionGroup group) {

      // Enumerate Constrollers-Descriptors
      ControllerActionDescriptor[] controllerActionDescriptors = group.Items.Select(
        (item) => item.ActionDescriptor
      ).OfType<ControllerActionDescriptor>().Distinct().ToArray();

      // Ressolve descriptors to real Controller-Types
      Type[] resolvableControllerTypes = controllerActionDescriptors.Where(
        (cad) => cad.ControllerTypeInfo != null
      ).Select(
        (cad) => cad.ControllerTypeInfo.AsType()
      ).Distinct().ToArray();

      return resolvableControllerTypes;
    }

    internal static string TryGetApiVersionFromControllerType(Type controllerType, out string contractName) {

      if (controllerType == null) {
        contractName = "Unknown";
        return "0.0.0";
      }

      Type implementationType = controllerType;

      // special knowledge about UJMW Facade-Controllers:
      // If the controller is from a dynamic assembly and its name contains "UJMW",
      // then we try to look for the first constructor and take the type of its first
      // parameter as the "real" implementation type, which might be located in a different
      // assembly that has a version number.
      if (controllerType.Assembly.IsDynamic && controllerType.Assembly.FullName.Contains("UJMW")) {
        ConstructorInfo ujmwFacedeCtor = controllerType.GetConstructors().FirstOrDefault();
        if (ujmwFacedeCtor != null) {
          Type firstConstructorParamType = ujmwFacedeCtor.GetParameters().Select(p => p.ParameterType).FirstOrDefault();
          if (firstConstructorParamType != null) {
            implementationType = firstConstructorParamType;
          }
        }
      }

      contractName = implementationType.Name.Replace("Controller", "");
      return implementationType.Assembly.GetName().Version?.ToString(3);

    }














    [HttpGet(nameof(Announce)), Produces("application/json")]
    public IActionResult Announce() {
      string addInfo = null;
      try {
        if(!SelfAnnouncementHelper.TriggerSelfAnnouncement(out addInfo, false)) {
          return Ok(new {
            action = "announce",
            fault = "Failed",
            addInfo = addInfo,
          });
        }   
      }
      catch (Exception ex) {
        return Ok(new {
          action = "announce",
          fault = ex.Message,
          addInfo = addInfo,
        });
      }
      return Ok(new {
        action = "announce",
        baseUrls = SelfAnnouncementHelper.BaseUrls,
        endpoints = GetReducedEndpointInfo(),
        addInfo = addInfo,
        fault = (string)null,
      });
    }

    [HttpGet(nameof(Unannounce)), Produces("application/json")]
    public IActionResult Unannounce() {
      string addInfo = null;
      try {
        if (!SelfAnnouncementHelper.TriggerSelfAnnouncement(out addInfo, false)) {
          return Ok(new {
            action = "unannounce",
            fault = "Failed",
            addInfo = addInfo,
          });
        }
      }
      catch (Exception ex) {
        return Ok(new {
          action = "unannounce",
          fault = ex.Message,
          addInfo = addInfo,
        });
      }
      return Ok(new {
        action = "unannounce",
        baseUrls = SelfAnnouncementHelper.BaseUrls,
        endpoints = GetReducedEndpointInfo(),
        addInfo = addInfo,
        fault = (string)null,
      });
    }

    private object[] GetReducedEndpointInfo() {
      return SelfAnnouncementHelper.RegisteredEndpoints.Select(
        (ep) => {
          return new {
            route = ep.RelativeRoute,
            contract = ep.ContractIdentifyingName
          };
        }
      ).ToArray();
    }

  }

}
