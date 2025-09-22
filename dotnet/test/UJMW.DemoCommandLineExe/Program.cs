using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UJMW.CommandLineFacade;

namespace UJMW.DemoCommandLineExe {
  internal class Program {
    static void Main(string[] args) {
      CommandLineWrapper.RegisterService<IDemoCliService>(() => new DemoCliService());

      CommandLineWrapper.InvokeFromCommandLine(args);

      //CommandLineWrapper.InvokeServiceMethod("Run", null);
      //CommandLineWrapper.InvokeServiceMethod("Run1", "{\"count\": 5}");
      //CommandLineWrapper.InvokeServiceMethod("Run2", "{\"message\": \"Hello, World!\", \"uppercase\": true}");
      //object result = CommandLineWrapper.InvokeServiceMethod("Run3", "{\"value\": 3.14}");
      //var sum = CommandLineWrapper.InvokeServiceMethod("Add", "{\"a\": 10, \"b\": 20}");

      //Console.ReadKey();
    }
  }
}
