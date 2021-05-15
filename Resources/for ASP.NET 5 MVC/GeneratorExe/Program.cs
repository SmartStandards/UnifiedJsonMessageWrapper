using CodeGeneration.Languages;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace CodeGeneration {

  public class Program {

    static int Main(string[] args) {
      String cfgRawJson = null;
      RootCfg rootCfg = null;

      try {

        try {
          if (args.Length > 0) {
            args[0] = Path.GetFullPath(args[0]);
            cfgRawJson = File.ReadAllText(args[0], Encoding.UTF8);
            rootCfg = JsonConvert.DeserializeObject<RootCfg>(cfgRawJson);
          }
        }
        catch (Exception ex) {
          Console.WriteLine("/* ERROR reading '" + args[0] + "': " + ex.Message);
          Console.WriteLine("   please specify a filename via commandline-arg which has the following content:");
          Console.WriteLine(JsonConvert.SerializeObject(new RootCfg(), Formatting.Indented));
          Console.WriteLine("*/");
          System.Threading.Thread.Sleep(200);
          throw new Exception("ERROR reading '" + args[0] + "': " + ex.Message);
        }

        if(rootCfg == null) {
          Console.WriteLine("/* ERROR: wrong input!");
          Console.WriteLine("   please specify a filename via commandline-arg which has the following content:");
          Console.WriteLine(JsonConvert.SerializeObject(new RootCfg(), Formatting.Indented));
          Console.WriteLine("*/");
          System.Threading.Thread.Sleep(200);
          throw new Exception("ERROR: wrong input: invalid configuration content!");
        }

        XmlCommentAccessExtensions.RequireXmlDocForNamespaces = rootCfg.requireXmlDocForNamespaces;

        CodeWriterBase langSpecificWriter;
        if(rootCfg.outputLanguage == "C#" || rootCfg.outputLanguage == "CS") {
          langSpecificWriter = new WriterForCS(Console.Out, rootCfg);
        }
        else if (rootCfg.outputLanguage == "TS") {
          langSpecificWriter = new WriterForTS(Console.Out, rootCfg);
        }
        else if (rootCfg.outputLanguage == "VB") {
          langSpecificWriter = new WriterForVB(Console.Out, rootCfg);
        }
        else {
          throw new Exception($"Unknown Language '{rootCfg.outputLanguage}' ");
        }

        if (String.IsNullOrWhiteSpace(rootCfg.template)) {
          throw new Exception($"No Template was selected!");
        }
        else if (String.Equals(rootCfg.template, "Wrappers", StringComparison.CurrentCultureIgnoreCase)) {
          var templateSpecificCfg  = JsonConvert.DeserializeObject<Wrappers.Cfg>(cfgRawJson);
          var gen = new Wrappers.Generator();
          gen.Generate(langSpecificWriter, templateSpecificCfg);
        }
        else if (String.Equals(rootCfg.template, "Models", StringComparison.CurrentCultureIgnoreCase)) {
          var templateSpecificCfg = JsonConvert.DeserializeObject<Models.Cfg>(cfgRawJson);
          var gen = new Models.Generator();
          gen.Generate(langSpecificWriter, templateSpecificCfg);
        }
        else if (String.Equals(rootCfg.template, "MvcControllers", StringComparison.CurrentCultureIgnoreCase)) {
          var templateSpecificCfg = JsonConvert.DeserializeObject<MvcControllers.Cfg>(cfgRawJson);
          var gen = new MvcControllers.Generator();
          gen.Generate(langSpecificWriter, templateSpecificCfg);
        }
        else if (String.Equals(rootCfg.template, "Clients", StringComparison.CurrentCultureIgnoreCase)) {
          var templateSpecificCfg = JsonConvert.DeserializeObject<Clients.Cfg>(cfgRawJson);
          var gen = new Clients.Generator();
          gen.Generate(langSpecificWriter, templateSpecificCfg);
        }
        //else if (String.Equals(rootCfg.template, "Interfaces", StringComparison.CurrentCultureIgnoreCase)) {
        //  var templateSpecificCfg = JsonConvert.DeserializeObject<Interfaces.Cfg>(cfgRawJson);
        //  var gen = new Interfaces.Generator();
        //  gen.Generate(langSpecificWriter, templateSpecificCfg);
        //}
        else {
          throw new Exception($"Unknown Template '{rootCfg.template}'");
        }

      }
      catch (Exception ex) {
        Console.WriteLine("/* ERROR: " + ex.Message);
        Console.WriteLine(ex.StackTrace);
        Console.WriteLine("*/");
        File.WriteAllText(args[0] + ".Error.txt", ex.Message + Environment.NewLine + ex.StackTrace, Encoding.Default);
        Thread.Sleep(200);
        return 100;
      }

      Thread.Sleep(1000);
      return 0;

    }

  }

}
