using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeGeneration.Languages {

  public abstract class CodeWriterBase {

    private TextWriter _Wtr;
    private CodeWritingSettings _Cfg;
    private int _CurrentIndentLevel = 0;

    public CodeWriterBase(TextWriter targetWriter, CodeWritingSettings cfg) {
      _Wtr = targetWriter;
      _Cfg = cfg;
    }

    public abstract void WriteImport(string @namespace);
    public abstract void WriteBeginNamespace(string name);
    public abstract void WriteEndNamespace();

    #region convenience

    public void PopAndWrite(string output) {
      _CurrentIndentLevel -= 1;
      this.WriteCore(output);
    }
    public void Write(string output) {
      this.WriteCore(output);
    }
    public void WriteIndented(string output) {
      _CurrentIndentLevel += 1;
      this.WriteCore(output);
      _CurrentIndentLevel -= 1;
    }
    public void WriteAndPush(string output) {
      this.WriteCore(output);
      _CurrentIndentLevel += 1;
    }
    public void PopAndWriteLine(string output) {
      _CurrentIndentLevel -= 1;
      this.WriteCore(output + Environment.NewLine);
    }
    public void WriteLine(string output = "") {
      this.WriteCore(output + Environment.NewLine);
    }
    public void WriteLineIndented(string output) {
      _CurrentIndentLevel += 1;
      this.WriteCore(output + Environment.NewLine);
      _CurrentIndentLevel -= 1;
    }
    public void WriteLineAndPush(string output) {
      this.WriteCore(output + Environment.NewLine);
      _CurrentIndentLevel += 1;
    }

    #endregion

    private void WriteCore(string output,bool suppressIndentOnFirstLine = false) {
      if(_CurrentIndentLevel < 0) {
        throw new Exception("Indent-Level < 0");
      }

      if (output.Contains(Environment.NewLine)) {
        bool endBreak = output.EndsWith(Environment.NewLine);
        using (StringReader rdr = new StringReader(output)) {
          string line = rdr.ReadLine();
          bool isFirst = true;
          while (line != null) {
            if (isFirst) {
              isFirst = false;
              if (!suppressIndentOnFirstLine) {
                _Wtr.Write(new String(' ', _Cfg.indentDepthPerLevel * _CurrentIndentLevel));
              }
            }
            else {
              _Wtr.WriteLine();
              _Wtr.Write(new String(' ', _Cfg.indentDepthPerLevel * _CurrentIndentLevel));
            }
            if (line.Length > 0) {
              _Wtr.Write(line);
            }
            line = rdr.ReadLine();
          }
          if (endBreak) {
            _Wtr.WriteLine();
          }
        }
      }
      else {
        if (!suppressIndentOnFirstLine) {
          _Wtr.Write(new String(' ', _Cfg.indentDepthPerLevel * _CurrentIndentLevel));
        }
        _Wtr.Write(output);
      }
    }

    /// <summary>
    /// FIRST TO LOWER
    /// </summary>
    public string Ftl(string input) {
      if (Char.IsUpper(input[0])) {
        input = Char.ToLower(input[0]) + input.Substring(1);
      }
      return input;
    }

    /// <summary>
    /// FIRST TO UPPER
    /// </summary>
    public string Ftu(string input) {
      if (Char.IsLower(input[0])) {
        input = Char.ToUpper(input[0]) + input.Substring(1);
      }
      return input;
    }

  }

}
