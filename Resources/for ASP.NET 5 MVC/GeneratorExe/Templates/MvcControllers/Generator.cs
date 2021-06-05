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

namespace CodeGeneration.MvcControllers {

  public class Generator {

    public void Generate(CodeWriterBase writer, Cfg cfg) {

      if(writer.GetType() != typeof(WriterForCS)) {
        throw new Exception("For the selected template is currenty only language 'CS' supported!");
      }

      var nsImports = new List<string>();
      nsImports.Add("Microsoft.AspNetCore.Mvc");
      nsImports.Add("Microsoft.Extensions.Logging");
      nsImports.Add("Security");
      if (cfg.generateSwashbuckleAttributesForControllers) {
        nsImports.Add("Swashbuckle.AspNetCore.Annotations");
      }
      nsImports.Add("System");
      nsImports.Add("System.Collections.Generic");
      nsImports.Add("System.Linq");
      nsImports.Add("System.Net");

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

      //collect models
      //var directlyUsedModelTypes = new List<Type>();
      //var wrappers = new Dictionary<String, StringBuilder>();
      foreach (Type svcInt in svcInterfaces) {

        //if(!nsImports.Contains(svcInt.Namespace)){
        //  nsImports.Add(svcInt.Namespace);
        //}
        string svcIntDoc = XmlCommentAccessExtensions.GetDocumentation(svcInt);
        string endpointName = svcInt.Name;

        if(endpointName[0] == 'I' && Char.IsUpper(endpointName[1])) {
          endpointName = endpointName.Substring(1);
        }

        writer.WriteLine();
        writer.WriteLine("[ApiController]");
        writer.WriteLine($"[Route(\"{writer.Ftl(endpointName)}\")]");
        writer.WriteLineAndPush($"public partial class {endpointName}Controller : ControllerBase {{");
        writer.WriteLine();
        writer.WriteLine($"private readonly ILogger<{endpointName}Controller> _Logger;");
        writer.WriteLine($"private readonly {svcInt.Name} _{endpointName};");
        writer.WriteLine();
        writer.WriteLineAndPush($"public {endpointName}Controller(ILogger<{endpointName}Controller> logger, {svcInt.Name} {writer.Ftl(endpointName)}) {{");
        writer.WriteLine($"_Logger = logger;");
        writer.WriteLine($"_{endpointName} = {writer.Ftl(endpointName)};");
        writer.PopAndWriteLine("}");

        foreach (MethodInfo svcMth in svcInt.GetMethods()) {
          string svcMthDoc = XmlCommentAccessExtensions.GetDocumentation(svcMth, true);

          writer.WriteLine();
          if (String.IsNullOrWhiteSpace(svcMthDoc)) {
            svcMthDoc = svcMth.Name;
          }
          writer.WriteLine($"/// <summary> {svcMthDoc} </summary>");
          writer.WriteLine($"/// <param name=\"args\"> request capsule containing the method arguments </param>");

          if (!String.IsNullOrWhiteSpace(cfg.customAttributesPerControllerMethod)) {
            writer.WriteLine("[" + cfg.customAttributesPerControllerMethod.Replace("{C}", endpointName).Replace("{O}", svcMth.Name) + "]");
          }
          writer.WriteLine($"[HttpPost(\"{writer.Ftl(svcMth.Name)}\"), Produces(\"application/json\")]");

          string swaggerBodyAttrib = "";
          if (cfg.generateSwashbuckleAttributesForControllers) {
            swaggerBodyAttrib = "[SwaggerRequestBody(Required = true)]";
            string escDesc = svcMthDoc.Replace("\\", "\\\\").Replace("\"", "\\\"");
            nsImports.Add($"[SwaggerOperation(OperationId = nameof({svcMth.Name}), Description = \"{escDesc}\")]");
          }

          writer.WriteLineAndPush($"public {svcMth.Name}Response {svcMth.Name}([FromBody]{swaggerBodyAttrib} {svcMth.Name}Request args) {{");
          writer.WriteLineAndPush("try {");
          writer.WriteLine($"var response = new {svcMth.Name}Response();");

          var @params = new List<string>();
          foreach (ParameterInfo svcMthPrm in svcMth.GetParameters()) {
            if (svcMthPrm.IsOut) {
              if (svcMthPrm.IsIn) {
                writer.WriteLine($"response.{writer.Ftl(svcMthPrm.Name)} = args.{writer.Ftl(svcMthPrm.Name)}; //shift IN-OUT value");
              }
              @params.Add($"response.{writer.Ftl(svcMthPrm.Name)}");
            }
            else {

              if (svcMthPrm.IsOptional) {

                string defaultValueString = "";
                if (svcMthPrm.DefaultValue == null) {
                  defaultValueString = "null";
                }
                else if (svcMthPrm.DefaultValue.GetType() == typeof(string)) {
                  defaultValueString = "\"" + svcMthPrm.DefaultValue.ToString() + "\"";
                }
                else {
                  defaultValueString = svcMthPrm.DefaultValue.ToString();
                }

                if (svcMthPrm.ParameterType.IsValueType) {
                  @params.Add($"(args.{writer.Ftl(svcMthPrm.Name)}.HasValue ? args.{writer.Ftl(svcMthPrm.Name)}.Value : {defaultValueString})");
                }
                else {
                  //here 'null' will be used
                  @params.Add($"args.{writer.Ftl(svcMthPrm.Name)}");

                  //@params.Add($"(args.{writer.Ftl(svcMthPrm.Name)} == null ? args.{writer.Ftl(svcMthPrm.Name)} : {defaultValueString})");
                }
              }
              else {
                @params.Add($"args.{writer.Ftl(svcMthPrm.Name)}");
              }
            }
          }

          if (svcMth.ReturnType != null && svcMth.ReturnType != typeof(void)) {
            writer.WriteLine($"response.@return = _{endpointName}.{svcMth.Name}({Environment.NewLine + String.Join("," + Environment.NewLine, @params.ToArray()) + Environment.NewLine});");
          }
          else {
            writer.WriteLine($"_{endpointName}.{svcMth.Name}({Environment.NewLine + String.Join("," + Environment.NewLine, @params.ToArray()) + Environment.NewLine});");
          }

          writer.WriteLine($"return response;");
          writer.PopAndWriteLine("}");

          writer.WriteLineAndPush("catch (Exception ex) {");
          writer.WriteLine($"_Logger.LogCritical(ex, ex.Message);");
          if (cfg.fillFaultPropertyOnException) {
            writer.WriteLine($"return new {svcMth.Name}Response {{ fault = {cfg.exceptionDisplay} }};");
          }
          else {
            writer.WriteLine($"return new {svcMth.Name}Response();");
          }
          writer.PopAndWriteLine("}");

          writer.PopAndWriteLine("}"); //method

        }//foreach Method

        writer.WriteLine();
        writer.PopAndWriteLine("}"); //controller-class

      }//foreach Interface

      if (!String.IsNullOrWhiteSpace(cfg.outputNamespace)) {
        writer.WriteLine();
        writer.WriteEndNamespace();
      }

    }
  }
}
