using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeGeneration.Clients {

  public class Cfg: RootCfg {

    public string connectorClassName = "Connector";
    public string authHeaderName = "Authorization";
    public bool throwClientExecptionsFromFaultProperty = false;

  }

}
