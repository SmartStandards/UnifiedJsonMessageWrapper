using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeGeneration.Models {

  public class Cfg: RootCfg {

    public string[] modelTypeNameIncludePatterns = new string[] {
      "Foo.*"
    };

    public bool generateDataAnnotationsForLocalModels = true; //requires also the "EntityAnnoations" Nuget Package!
    public bool generateNavigationAnnotationsForLocalModels = true;

  }

}
