using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeGeneration.Languages {

  public class WriterForCS : CodeWriterBase {

    public WriterForCS(TextWriter targetWriter, CodeWritingSettings cfg) : base(targetWriter, cfg) {
    }
    public override void WriteImport(string @namespace) {
      this.WriteLine($"using {@namespace};");
    }

    public override void WriteBeginNamespace(string name) {
      name = this.Escape(name);
      this.WriteLineAndPush($"namespace {name} {{");
    }

    public override void WriteEndNamespace() {
      this.PopAndWriteLine("}");
    }

    private string Escape(string input) {
      //TODO: implement escaping
      return input;
    }

  }

}
