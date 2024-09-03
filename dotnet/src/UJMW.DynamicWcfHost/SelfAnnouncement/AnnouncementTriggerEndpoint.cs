using Logging.SmartStandards;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;

namespace System.Web.UJMW.SelfAnnouncement {

  [ServiceContract]
  public interface IAnnouncementTriggerEndpoint {

    [OperationContract]
    [WebInvoke(Method = "GET")]
    object ShowState();

    [OperationContract]
    [WebInvoke(Method = "GET")]
    object Announce();

    [OperationContract]
    [WebInvoke(Method = "GET")]
    object Unannounce();

  }

  public class AnnouncementTriggerEndpoint : IAnnouncementTriggerEndpoint {

    public AnnouncementTriggerEndpoint() {
      SelfAnnouncementHelper.OnApplicationStarted();
    }

    public object ShowState() {

      if (string.IsNullOrWhiteSpace(SelfAnnouncementHelper.LastFault)) {
        return new {
          lastActionTime = SelfAnnouncementHelper.LastActionTime,
          lastAction = SelfAnnouncementHelper.LastAction,
          baseUrls = SelfAnnouncementHelper.BaseUrls,
          endpoints = GetReducedEndpointInfo(),
          lastAddInfo = SelfAnnouncementHelper.LastAddInfo,
          lastFault = SelfAnnouncementHelper.LastFault
        };
      }
      else {
        return new {
          lastActionTime = SelfAnnouncementHelper.LastActionTime,
          lastAction = SelfAnnouncementHelper.LastAction,
          lastFault = SelfAnnouncementHelper.LastFault,
          lastAddInfo = SelfAnnouncementHelper.LastAddInfo,
        };
      }
    }

    public object Announce() {
      string addInfo = null;
      try {
        if (!SelfAnnouncementHelper.TriggerSelfAnnouncement(out addInfo, false)) {
          return new {
            action = "announce",
            fault = "Failed",
            addInfo = addInfo,
          };
        }
      }
      catch (Exception ex) {
        return new {
          action = "announce",
          fault = ex.Message,
          addInfo = addInfo,
        };
      }
      return new {
        action = "announce",
        baseUrls = SelfAnnouncementHelper.BaseUrls,
        endpoints = GetReducedEndpointInfo(),
        addInfo = addInfo,
        fault = (string)null,
      };
    }

    public object Unannounce() {
      string addInfo = null;
      try {
        if (!SelfAnnouncementHelper.TriggerSelfAnnouncement(out addInfo, false)) {
          return new {
            action = "unannounce",
            fault = "Failed",
            addInfo = addInfo,
          };
        }
      }
      catch (Exception ex) {
        return new {
          action = "unannounce",
          fault = ex.Message,
          addInfo = addInfo,
        };
      }
      return new {
        action = "unannounce",
        baseUrls = SelfAnnouncementHelper.BaseUrls,
        endpoints = GetReducedEndpointInfo(),
        addInfo = addInfo,
        fault = (string)null,
      };
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
