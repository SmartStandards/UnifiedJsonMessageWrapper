using CodeGeneration.Languages;
using System;

namespace CodeGeneration {

  public class RootCfg : CodeWritingSettings {

    //INPUT-BASICS

    public string inputFile = null;
    public string interfaceTypeNamePattern = null;

    public string[] requireXmlDocForNamespaces = new string[] { };

    //OUTPUT-BASICS

    public string template = null;
    public string outputLanguage = "C#";
    public string outputNamespace = "";
    public String[] customImports = new String[] {};

    //DEBUGGING
    public int waitForDebuggerSec = 0;

  }

}
