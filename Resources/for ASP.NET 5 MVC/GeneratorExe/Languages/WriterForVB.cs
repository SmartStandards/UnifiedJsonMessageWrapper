using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeGeneration.Languages {

  public class WriterForVB : CodeWriterBase {

    public WriterForVB(TextWriter targetWriter, CodeWritingSettings cfg): base (targetWriter, cfg) {
    }
    public override void WriteImport(string @namespace) {
      this.WriteLine($"Imports {@namespace}");
    }

    public override void WriteBeginNamespace(string name) {
      name = this.Escape(name);
      this.WriteLineAndPush("Namespace " + name);
    }

    public override void WriteEndNamespace() {
      this.PopAndWriteLine("End Namespace");
    }

    private string Escape(string input) {
      //TODO: implement escaping
      return input;
    }

  }

}
