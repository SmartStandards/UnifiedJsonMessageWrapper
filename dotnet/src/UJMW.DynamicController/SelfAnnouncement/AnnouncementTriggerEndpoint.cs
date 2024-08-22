using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace System.Web.UJMW.SelfAnnouncement {

  [Route(Route)]
  internal class AnnouncementTriggerEndpointController : Controller {

    public const string Route = "Announcement.svc";

    [HttpGet(), Produces("application/json")]
    public IActionResult ShowCurrentState() {
      if (string.IsNullOrWhiteSpace(SelfAnnouncementHelper.LastFault)) {
        return Ok(new {
          lastActionTime = SelfAnnouncementHelper.LastActionTime,
          lastAction = SelfAnnouncementHelper.LastAction,
          baseUrls = SelfAnnouncementHelper.BaseUrls,
          endpoints = GetReducedEndpointInfo(),
          lastAddInfo = SelfAnnouncementHelper.LastAddInfo,
          lastFault = SelfAnnouncementHelper.LastFault
        });
      }
      else {
        return Ok(new {
          lastActionTime = SelfAnnouncementHelper.LastActionTime,
          lastAction = SelfAnnouncementHelper.LastAction,
          lastFault = SelfAnnouncementHelper.LastFault,
          lastAddInfo = SelfAnnouncementHelper.LastAddInfo,
        });
      }
    }

    [HttpGet(nameof(SelfAnnounceToRegistry)), Produces("application/json")]
    public IActionResult SelfAnnounceToRegistry() {
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

    [HttpGet(nameof(UnAnnounceToRegistry)), Produces("application/json")]
    public IActionResult UnAnnounceToRegistry() {
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
