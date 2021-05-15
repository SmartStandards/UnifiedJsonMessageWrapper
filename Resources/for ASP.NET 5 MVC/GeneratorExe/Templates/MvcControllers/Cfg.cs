using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeGeneration.MvcControllers {

  public class Cfg: RootCfg {

    public bool generateSwashbuckleAttributesForControllers = true;
    public string customAttributesPerControllerMethod = null;

  }

}
