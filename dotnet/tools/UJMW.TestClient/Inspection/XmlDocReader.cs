using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace UJMW.TestClient.Inspection {

  /// <summary>
  /// Reads the XML documentation file ('&lt;assembly&gt;.xml') which is located next to an assembly
  /// and returns plain-text descriptions for types, methods, parameters and return values.
  /// </summary>
  internal sealed class XmlDocReader {

    private readonly Dictionary<string, XElement> _MembersByName = new Dictionary<string, XElement>(StringComparer.Ordinal);

    private XmlDocReader() {
    }

    /// <summary>
    /// Cache of readers per documentation file (invalidated when the file changes).
    /// </summary>
    private static readonly Dictionary<string, KeyValuePair<DateTime, XmlDocReader>> _Cache = new Dictionary<string, KeyValuePair<DateTime, XmlDocReader>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the reader for the documentation file of the assembly (an empty reader if there is none).
    /// </summary>
    public static XmlDocReader ForAssembly(Assembly assembly) {
      string assemblyPath = null;
      try {
        assemblyPath = assembly.Location;
      } catch (NotSupportedException) {
        assemblyPath = null;
      }
      if (string.IsNullOrEmpty(assemblyPath)) {
        return new XmlDocReader();
      }
      return ForAssemblyPath(assemblyPath);
    }

    /// <summary>
    /// Returns the reader for the documentation file of the assembly path (an empty reader if there is none).
    /// </summary>
    public static XmlDocReader ForAssemblyPath(string assemblyPath) {
      string xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
      if (!File.Exists(xmlPath)) {
        return new XmlDocReader();
      }
      DateTime lastWrite = File.GetLastWriteTimeUtc(xmlPath);
      lock (_Cache) {
        KeyValuePair<DateTime, XmlDocReader> cached;
        if (_Cache.TryGetValue(xmlPath, out cached) && cached.Key == lastWrite) {
          return cached.Value;
        }
        XmlDocReader reader = new XmlDocReader();
        try {
          XDocument document = XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace);
          foreach (XElement member in document.Descendants("member")) {
            string name = (string)member.Attribute("name");
            if (!string.IsNullOrEmpty(name)) {
              reader._MembersByName[name] = member;
            }
          }
        } catch (XmlException) {
          //an invalid documentation file is treated like a missing one
        }
        _Cache[xmlPath] = new KeyValuePair<DateTime, XmlDocReader>(lastWrite, reader);
        return reader;
      }
    }

    /// <summary>
    /// Returns the summary of a type (or an empty string).
    /// </summary>
    public string GetTypeSummary(Type type) {
      XElement member = this.FindMember("T:" + GetTypeId(type, false));
      return GetElementText(member, "summary");
    }

    /// <summary>
    /// Returns the summary of a method (or an empty string).
    /// </summary>
    public string GetMethodSummary(MethodInfo method) {
      return GetElementText(this.FindMethodMember(method), "summary");
    }

    /// <summary>
    /// Returns the 'returns' description of a method (or an empty string).
    /// </summary>
    public string GetMethodReturns(MethodInfo method) {
      return GetElementText(this.FindMethodMember(method), "returns");
    }

    /// <summary>
    /// Returns the description of a method parameter (or an empty string).
    /// </summary>
    public string GetParameterDescription(MethodInfo method, string parameterName) {
      XElement member = this.FindMethodMember(method);
      if (member == null) {
        return string.Empty;
      }
      XElement parameter = member.Elements("param").FirstOrDefault((p) => (string)p.Attribute("name") == parameterName);
      return ToPlainText(parameter);
    }

    /// <summary>
    /// Looks up a member by its documentation id.
    /// </summary>
    private XElement FindMember(string memberId) {
      XElement member;
      if (_MembersByName.TryGetValue(memberId, out member)) {
        return member;
      }
      return null;
    }

    /// <summary>
    /// Looks up the documentation of a method.
    /// </summary>
    private XElement FindMethodMember(MethodInfo method) {
      try {
        return this.FindMember(GetMethodId(method));
      } catch (FileNotFoundException) {
        //a parameter type lives in an assembly which is not available
        return null;
      }
    }

    /// <summary>
    /// Builds the documentation id of a method, e.g. 'M:Ns.Type.Method(System.String,System.Int32@)'.
    /// </summary>
    private static string GetMethodId(MethodInfo method) {
      StringBuilder id = new StringBuilder();
      id.Append("M:");
      id.Append(GetTypeId(method.DeclaringType, false));
      id.Append('.');
      id.Append(method.Name);
      if (method.IsGenericMethod) {
        id.Append("``");
        id.Append(method.GetGenericArguments().Length);
      }
      ParameterInfo[] parameters = method.GetParameters();
      if (parameters.Length > 0) {
        id.Append('(');
        for (int i = 0; i < parameters.Length; i++) {
          if (i > 0) {
            id.Append(',');
          }
          id.Append(GetParameterTypeId(parameters[i].ParameterType));
        }
        id.Append(')');
      }
      return id.ToString();
    }

    /// <summary>
    /// Builds the documentation id of a parameter type (generic arguments in braces, '@' for by-ref).
    /// </summary>
    private static string GetParameterTypeId(Type type) {
      if (type.IsByRef) {
        return GetParameterTypeId(type.GetElementType()) + "@";
      }
      if (type.IsArray) {
        int rank = type.GetArrayRank();
        if (rank == 1) {
          return GetParameterTypeId(type.GetElementType()) + "[]";
        }
        return GetParameterTypeId(type.GetElementType()) + "[" + string.Join(",", Enumerable.Repeat("0:", rank)) + "]";
      }
      if (type.IsPointer) {
        return GetParameterTypeId(type.GetElementType()) + "*";
      }
      if (type.IsGenericParameter) {
        if (type.DeclaringMethod != null) {
          return "``" + type.GenericParameterPosition;
        }
        return "`" + type.GenericParameterPosition;
      }
      return GetTypeId(type, true);
    }

    /// <summary>
    /// Builds the documentation id of a type (without 'T:' prefix).
    /// </summary>
    private static string GetTypeId(Type type, bool expandGenericArguments) {
      if (type.IsGenericType && expandGenericArguments && !type.IsGenericTypeDefinition) {
        Type definition = type.GetGenericTypeDefinition();
        string definitionId = GetTypeId(definition, false);
        int tickIndex = definitionId.LastIndexOf('`');
        if (tickIndex >= 0) {
          definitionId = definitionId.Substring(0, tickIndex);
        }
        string arguments = string.Join(",", type.GetGenericArguments().Select((a) => GetParameterTypeId(a)));
        return definitionId + "{" + arguments + "}";
      }
      string fullName = type.FullName;
      if (fullName == null) {
        fullName = type.Namespace + "." + type.Name;
      }
      int argumentsIndex = fullName.IndexOf('[');
      if (argumentsIndex >= 0) {
        fullName = fullName.Substring(0, argumentsIndex);
      }
      return fullName.Replace('+', '.');
    }

    /// <summary>
    /// Returns the plain text of a child element of a member.
    /// </summary>
    private static string GetElementText(XElement member, string elementName) {
      if (member == null) {
        return string.Empty;
      }
      return ToPlainText(member.Element(elementName));
    }

    /// <summary>
    /// Converts documentation markup into plain text (cref/paramref references become their names).
    /// </summary>
    private static string ToPlainText(XElement element) {
      if (element == null) {
        return string.Empty;
      }
      StringBuilder text = new StringBuilder();
      AppendPlainText(element, text);
      string[] lines = text.ToString().Replace("\r\n", "\n").Split('\n');
      List<string> normalizedLines = new List<string>();
      foreach (string line in lines) {
        normalizedLines.Add(Regex.Replace(line, @"[ \t]+", " ").Trim());
      }
      string result = string.Join("\n", normalizedLines);
      result = Regex.Replace(result, @"\n{3,}", "\n\n");
      return result.Trim();
    }

    /// <summary>
    /// Recursively appends the text of documentation markup.
    /// </summary>
    private static void AppendPlainText(XElement element, StringBuilder text) {
      foreach (XNode node in element.Nodes()) {
        XText textNode = node as XText;
        if (textNode != null) {
          text.Append(Regex.Replace(textNode.Value, @"\s+", " "));
          continue;
        }
        XElement child = node as XElement;
        if (child == null) {
          continue;
        }
        string name = child.Name.LocalName;
        if (name == "see" || name == "seealso") {
          string reference = (string)child.Attribute("cref");
          if (reference == null) {
            reference = (string)child.Attribute("langword");
          }
          if (reference == null) {
            reference = (string)child.Attribute("href");
          }
          if (!child.IsEmpty) {
            AppendPlainText(child, text);
          } else if (reference != null) {
            text.Append(ShortenReference(reference));
          }
        } else if (name == "paramref" || name == "typeparamref") {
          text.Append((string)child.Attribute("name"));
        } else if (name == "para" || name == "br") {
          text.Append('\n');
          AppendPlainText(child, text);
          text.Append('\n');
        } else if (name == "item") {
          text.Append("\n- ");
          AppendPlainText(child, text);
        } else {
          AppendPlainText(child, text);
        }
      }
    }

    /// <summary>
    /// Converts 'T:Ns.Type' or 'M:Ns.Type.Method(System.String)' into 'Type' or 'Type.Method'.
    /// </summary>
    private static string ShortenReference(string reference) {
      string value = reference;
      if (value.Length > 2 && value[1] == ':') {
        value = value.Substring(2);
      }
      int parenthesisIndex = value.IndexOf('(');
      if (parenthesisIndex >= 0) {
        value = value.Substring(0, parenthesisIndex);
      }
      string[] segments = value.Split('.');
      if (reference.StartsWith("T:", StringComparison.Ordinal) || segments.Length < 2) {
        return segments[segments.Length - 1];
      }
      return segments[segments.Length - 2] + "." + segments[segments.Length - 1];
    }

  }

}
