using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Security.AccessTokenHandling.OAuthServer {

  [ApiController]
  [ApiExplorerSettings(GroupName = "Foo")]
  [Route("Foo")]
  public partial class FooServiceController : ControllerBase {

    private readonly ILogger<FooServiceController> _Logger;

    public FooServiceController( ILogger<FooServiceController> logger, object authService ) { 
      
     





    }





  }

}
