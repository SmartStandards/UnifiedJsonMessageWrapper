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

namespace CodeGeneration.Models {

  public class Generator {

    public void Generate(CodeWriterBase writer, Cfg cfg) {

      if (writer.GetType() != typeof(WriterForCS)) {
        throw new Exception("For the selected template is currenty only language 'C#' supported!");
      }

      var nsImports = new List<string>();
      nsImports.Add("System");
      nsImports.Add("System.Collections.Generic");

      if (cfg.generateNavigationAnnotationsForLocalModels) {
        nsImports.Add("System.ComponentModel.DataAnnotations"); //+ extended by the "EntityAnnoations" Nuget Package!
      }
      else if (cfg.generateDataAnnotationsForLocalModels) {
        nsImports.Add("System.ComponentModel.DataAnnotations");
      }

      var modelContent = new StringBuilder(10000);

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
      for (int i = 0; i < cfg.modelTypeNameIncludePatterns.Length; i++) {
        cfg.modelTypeNameIncludePatterns[i] = "^(" + Regex.Escape(cfg.modelTypeNameIncludePatterns[i]).Replace("\\*", ".*?") + ")$";
      }

      svcInterfaces = svcInterfaces.Where((Type i) => Regex.IsMatch(i.FullName, cfg.interfaceTypeNamePattern)).ToArray();

      //collect models
      var directlyUsedModelTypes = new List<Type>();
      //var wrappers = new Dictionary<String, StringBuilder>();
      foreach (Type svcInt in svcInterfaces) {

        //if(!nsImports.Contains(svcInt.Namespace)){
        //  nsImports.Add(svcInt.Namespace);
        //}
        string svcIntDoc = XmlCommentAccessExtensions.GetDocumentation(svcInt);

        foreach (MethodInfo svcMth in svcInt.GetMethods()) {
          string svcMthDoc = XmlCommentAccessExtensions.GetDocumentation(svcMth, false);

          if (svcMth.ReturnType != null && svcMth.ReturnType != typeof(void)) {
            directlyUsedModelTypes.Add(svcMth.ReturnType);
            if (!nsImports.Contains(svcMth.ReturnType.Namespace)) {
              nsImports.Add(svcMth.ReturnType.Namespace);
            }
          }

          foreach (ParameterInfo svcMthPrm in svcMth.GetParameters()) {
            string svcMthPrmDoc = XmlCommentAccessExtensions.GetDocumentation(svcMthPrm);
            if (String.IsNullOrWhiteSpace(svcMthPrmDoc)) {
              svcMthPrmDoc = XmlCommentAccessExtensions.GetDocumentation(svcMthPrm.ParameterType);
            }
            directlyUsedModelTypes.Add(svcMthPrm.ParameterType);

            if (!nsImports.Contains(svcMthPrm.ParameterType.Namespace)) {
              nsImports.Add(svcMthPrm.ParameterType.Namespace);
            }

          }//foreach Param

        }//foreach Method

      }//foreach Interface

      Action<List<Type>, Type> addRecursiveMethod = null;
      addRecursiveMethod = (collector, candidate) => {
        if (candidate.IsArray) {
          candidate = candidate.GetElementType();
        }
        else if (candidate.IsGenericType) {
          var genBase = candidate.GetGenericTypeDefinition();
          var genArg1 = candidate.GetGenericArguments()[0];
          if (typeof(List<>).MakeGenericType(genArg1).IsAssignableFrom(candidate)) {
            candidate = genArg1;
          }
          else if (typeof(Collection<>).MakeGenericType(genArg1).IsAssignableFrom(candidate)) {
            candidate = genArg1;
          }
          if (genBase == typeof(Nullable<>)) {
            candidate = genArg1;
          }
        }

        if (!collector.Contains(candidate)) {
          bool match = false;
          for (int i = 0; i < cfg.modelTypeNameIncludePatterns.Length; i++) {
            if (Regex.IsMatch(candidate.FullName, cfg.modelTypeNameIncludePatterns[i])) {
              match = true;
              break;
            }
          }
          if (match) {
            collector.Add(candidate);
            if (candidate.BaseType != null) {
              addRecursiveMethod.Invoke(collector, candidate.BaseType);
            }
            foreach (PropertyInfo p in candidate.GetProperties()) {
              addRecursiveMethod.Invoke(collector, p.PropertyType);
            }
          }
        }
      };

      var modelTypesToGenerate = new List<Type>();
      foreach (Type canidate in directlyUsedModelTypes.Distinct()) {
        addRecursiveMethod.Invoke(modelTypesToGenerate, canidate);
      }

      foreach (Type modelTypeToGenerate in modelTypesToGenerate.OrderBy((m) => m.Name)) {
        string modelDoc = XmlCommentAccessExtensions.GetDocumentation(modelTypeToGenerate, true);

        modelContent.AppendLine();

        if (!String.IsNullOrWhiteSpace(modelDoc)) {
          modelContent.AppendLine($"/// <summary>");
          modelContent.AppendLine($"/// " + modelDoc.Replace("\n", "\n/// "));
          modelContent.AppendLine($"/// </summary>");
        }

        //TODO: Obsolete-Attributes on Class-Level (including comments)

        if (modelTypeToGenerate.IsClass) {
          modelContent.Append("public class " + modelTypeToGenerate.Name);
        }
        else {
          modelContent.Append("public interface " + modelTypeToGenerate.Name);
        }

        if (modelTypeToGenerate.BaseType != null) {
          modelContent.AppendLine(" {");
        }
        else {
          modelContent.AppendLine(" : " + modelTypeToGenerate.BaseType.Name + " {");
        }

        foreach (PropertyInfo prop in modelTypeToGenerate.GetProperties()) {
          string propDoc = XmlCommentAccessExtensions.GetDocumentation(prop, true);

          modelContent.AppendLine();

          if (!String.IsNullOrWhiteSpace(propDoc)) {
            modelContent.AppendLine($"  /// <summary>");
            modelContent.AppendLine($"  /// " + propDoc.Replace("\n", "\n  /// "));
            modelContent.AppendLine($"  /// </summary>");
          }

          var attribs = prop.GetCustomAttributes();

          if (cfg.generateDataAnnotationsForLocalModels) {
            if (prop.GetCustomAttributes<RequiredAttribute>().Any()) {
              modelContent.AppendLine("  [Required]");
            }
            ObsoleteAttribute oa = prop.GetCustomAttributes<ObsoleteAttribute>().FirstOrDefault();
            if (oa != null) {
              modelContent.AppendLine("  [Obsolete(\"" + oa.Message + "\")]");
            }
            MaxLengthAttribute mla = prop.GetCustomAttributes<MaxLengthAttribute>().FirstOrDefault();
            if (mla != null) {
              modelContent.AppendLine("  [MaxLength(" + mla.Length.ToString() + ")]");
            }
          }

          if (cfg.generateNavigationAnnotationsForLocalModels) {
            //here we use strings, because we dont want to have a reference to a nuget-pkg within this template
            if (attribs.Where((a) => a.GetType().Name == "FixedAfterCreationAttribute").Any()) {
              modelContent.AppendLine("  [FixedAfterCreation]");
            }
            if (attribs.Where((a) => a.GetType().Name == "SystemInternalAttribute").Any()) {
              modelContent.AppendLine("  [SystemInternal]");
            }
            if (attribs.Where((a) => a.GetType().Name == "LookupAttribute").Any()) {
              modelContent.AppendLine("  [Lookup]");
            }
            if (attribs.Where((a) => a.GetType().Name == "RefererAttribute").Any()) {
              modelContent.AppendLine("  [Referer]");
            }
            if (attribs.Where((a) => a.GetType().Name == "PrincipalAttribute").Any()) {
              modelContent.AppendLine("  [Principal]");
            }
            if (attribs.Where((a) => a.GetType().Name == "DependentAttribute").Any()) {
              modelContent.AppendLine("  [Dependent]");
            }
          }

          if (modelTypeToGenerate.IsClass) {
            modelContent.AppendLine("  public " + prop.PropertyType.Name + " " + prop.Name + " { get; set; }");
          }
          else {//interface
            modelContent.AppendLine("  " + prop.PropertyType.Name + " " + prop.Name + " { get; set; }");
          }

        }

        modelContent.AppendLine();
        modelContent.AppendLine("}");
        modelContent.AppendLine();

      }

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

      using (var sr = new StringReader(modelContent.ToString())) {
        string line = sr.ReadLine();
        while (line != null) {
          writer.WriteLine(line);
          line = sr.ReadLine();
        }
      }
      
      if (!String.IsNullOrWhiteSpace(cfg.outputNamespace)) {
        writer.WriteEndNamespace();
      }

    }
  }
}
