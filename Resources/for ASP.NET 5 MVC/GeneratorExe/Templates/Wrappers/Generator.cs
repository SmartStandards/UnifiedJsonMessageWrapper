using CodeGeneration.Languages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CodeGeneration.Wrappers {

  public class Generator {

    public void Generate(CodeWriterBase writer, Cfg cfg) {

      if(writer.GetType() != typeof(WriterForCS)) {
        throw new Exception("For the selected template is currenty only language 'CS' supported!");
      }

      var nsImports = new List<string>();
      nsImports.Add("System");
      nsImports.Add("System.Collections.Generic");
      nsImports.Add("System.ComponentModel.DataAnnotations");

      var wrapperContent = new StringBuilder(10000);

      var inputFileFullPath = Path.GetFullPath(cfg.inputFile);
      Assembly ass = Assembly.LoadFile(inputFileFullPath);

      Type[] svcInterfaces;
      try {
        svcInterfaces = ass.GetTypes();
      }
      catch (ReflectionTypeLoadException ex) {
        svcInterfaces = ex.Types.Where((t) => t != null).ToArray();
      }

      //transform patterns to regex
      cfg.interfaceTypeNamePattern = "^(" + Regex.Escape(cfg.interfaceTypeNamePattern).Replace("\\*", ".*?") + ")$";

      svcInterfaces = svcInterfaces.Where((Type i) => Regex.IsMatch(i.FullName, cfg.interfaceTypeNamePattern)).ToArray();

      //collect models
      //var directlyUsedModelTypes = new List<Type>();
      //var wrappers = new Dictionary<String, StringBuilder>();
      foreach (Type svcInt in svcInterfaces) {

        //if(!nsImports.Contains(svcInt.Namespace)){
        //  nsImports.Add(svcInt.Namespace);
        //}
        string svcIntDoc = XmlCommentAccessExtensions.GetDocumentation(svcInt);

        foreach (MethodInfo svcMth in svcInt.GetMethods()) {
          string svcMthDoc = XmlCommentAccessExtensions.GetDocumentation(svcMth, false);

          if (svcMth.ReturnType != null && svcMth.ReturnType != typeof(void)) {
            //directlyUsedModelTypes.Add(svcMth.ReturnType);
            if (!nsImports.Contains(svcMth.ReturnType.Namespace)) {
              nsImports.Add(svcMth.ReturnType.Namespace);
            }
          }

          string requestWrapperName = svcMth.Name + "Request";
          string responseWrapperName = svcMth.Name + "Response";
          StringBuilder requestWrapperContent = new StringBuilder(500);
          StringBuilder responseWrapperContent = new StringBuilder(500);

          requestWrapperContent.AppendLine();
          requestWrapperContent.AppendLine("/// <summary>");
          requestWrapperContent.AppendLine($"/// Contains arguments for calling '{svcMth.Name}'.");
          if (!String.IsNullOrWhiteSpace(svcMthDoc)) {
            requestWrapperContent.AppendLine($"/// Method: " + svcMthDoc.Replace("\n", "\n///   "));
          }
          requestWrapperContent.AppendLine("/// </summary>");
          requestWrapperContent.AppendLine("public class " + requestWrapperName + " {");

          responseWrapperContent.AppendLine();
          responseWrapperContent.AppendLine("/// <summary>");
          responseWrapperContent.AppendLine($"/// Contains results from calling '{svcMth.Name}'.");
          if (!String.IsNullOrWhiteSpace(svcMthDoc)) {
            responseWrapperContent.AppendLine($"/// Method: " + svcMthDoc.Replace("\n", "\n///   "));
          }
          responseWrapperContent.AppendLine("/// </summary>");
          responseWrapperContent.AppendLine("public class " + responseWrapperName + " {");

          foreach (ParameterInfo svcMthPrm in svcMth.GetParameters()) {
            string svcMthPrmDoc = XmlCommentAccessExtensions.GetDocumentation(svcMthPrm);
            if (String.IsNullOrWhiteSpace(svcMthPrmDoc)) {
              svcMthPrmDoc = XmlCommentAccessExtensions.GetDocumentation(svcMthPrm.ParameterType);
            }
            //directlyUsedModelTypes.Add(svcMthPrm.ParameterType);

            if (!nsImports.Contains(svcMthPrm.ParameterType.Namespace)) {
              nsImports.Add(svcMthPrm.ParameterType.Namespace);
            }

            string reqStr = "Required";
            if (svcMthPrm.IsOptional) {
              reqStr = "Optional";
            }


            //if (svcMthPrm.IsOptional) {
            //  //paramSignature.Add($"{pt} {svcMthPrm.Name} = default({pt.Name})");
            //  if (pt.IsValueType) {
            //    paramSignature.Add($"{pfx}{pt.Name}? {svcMthPrm.Name} = null");
            //  }
            //  else {
            //    paramSignature.Add($"{pfx}{pt.Name} {svcMthPrm.Name} = null");
            //  }
            //}
            //else {
            //  paramSignature.Add($"{pfx}{pt.Name} {svcMthPrm.Name}");
            //}

            //string accessModifier = "";//interfaces have no a.m.
            //if (modelTypeToGenerate.IsClass) {
            //  accessModifier = "public ";
            //}
            //string pType = prop.PropertyType.Name;
            //if ()

            String pType;
            String initializer = "";
            if (svcMthPrm.IsOut) {
              pType = svcMthPrm.ParameterType.GetElementType().Name;
            }
            else {
              pType = svcMthPrm.ParameterType.Name;
            }
            if (svcMthPrm.IsOptional && svcMthPrm.ParameterType.IsValueType) {
              pType = pType + "?";
              initializer = " = null;";
            }
            if (!svcMthPrm.IsOut) {
              requestWrapperContent.AppendLine();
              if (!String.IsNullOrWhiteSpace(svcMthPrmDoc)) {
                requestWrapperContent.AppendLine($"  /// <summary> {reqStr} Argument for '{svcMth.Name}' ({pType}): {svcMthPrmDoc} </summary>");
              }
              else {
                requestWrapperContent.AppendLine($"  /// <summary> {reqStr} Argument for '{svcMth.Name}' ({pType}) </summary>");
              }
              if (!svcMthPrm.IsOptional) {
                requestWrapperContent.AppendLine("  [Required]");
              }
              requestWrapperContent.AppendLine("  public " + pType + " " + svcMthPrm.Name + " { get; set; }" + initializer);
            }
            if (svcMthPrm.IsOut) {
              responseWrapperContent.AppendLine();
              if (!String.IsNullOrWhiteSpace(svcMthPrmDoc)) {
                responseWrapperContent.AppendLine($"  /// <summary> Out-Argument of '{svcMth.Name}' ({pType}): {svcMthPrmDoc} </summary>");
              }
              else {
                responseWrapperContent.AppendLine($"  /// <summary> Out-Argument of '{svcMth.Name}' ({pType}) </summary>");
              }
              if (!svcMthPrm.IsOptional) {
                responseWrapperContent.AppendLine("  [Required]");
              }
              responseWrapperContent.AppendLine("  public " + pType + " " + svcMthPrm.Name + " { get; set; }" + initializer);
            }

          }//foreach Param

          requestWrapperContent.AppendLine();
          requestWrapperContent.AppendLine("}");

          if (svcMth.ReturnType != null && svcMth.ReturnType != typeof(void)) {
            responseWrapperContent.AppendLine();
            string retTypeDoc = XmlCommentAccessExtensions.GetDocumentation(svcMth.ReturnType);
            if (!String.IsNullOrWhiteSpace(retTypeDoc)) {
              responseWrapperContent.AppendLine($"  /// <summary> Return-Value of '{svcMth.Name}' ({svcMth.ReturnType.Name}): {retTypeDoc} </summary>");
            }
            else {
              responseWrapperContent.AppendLine($"  /// <summary> Return-Value of '{svcMth.Name}' ({svcMth.ReturnType.Name}) </summary>");
            }
            responseWrapperContent.AppendLine("  [Required]");
            responseWrapperContent.AppendLine("  public " + svcMth.ReturnType.Name + " @return { get; set; }");
          }
          responseWrapperContent.AppendLine();
          responseWrapperContent.AppendLine("}");

          wrapperContent.Append(requestWrapperContent);
          wrapperContent.Append(responseWrapperContent);

        }//foreach Method

      }//foreach Interface

      //this can be done only here, because the nsImports will be extended during main-logic
      if (!String.IsNullOrWhiteSpace(cfg.outputNamespace) && cfg.customImports.Contains(cfg.outputNamespace)) {
        nsImports.Remove(cfg.outputNamespace);
      }
      foreach (string import in cfg.customImports.Union(nsImports).Distinct().OrderBy((s) => s)) {
        writer.WriteImport(import);
      }

      if (!String.IsNullOrWhiteSpace(cfg.outputNamespace)) {
        writer.WriteLine();
        writer.WriteBeginNamespace(cfg.outputNamespace);
      }

      using (var sr = new StringReader(wrapperContent.ToString())) {
        string line = sr.ReadLine();
        while (line != null) {
          writer.WriteLine(line);
          line = sr.ReadLine();
        }
      }

      if (!String.IsNullOrWhiteSpace(cfg.outputNamespace)) {
        writer.WriteLine();
        writer.WriteEndNamespace();
      }

    }
  }
}
