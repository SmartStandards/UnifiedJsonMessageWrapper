using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace System.Web.UJMW {

  /// <summary>
  /// Reads XML documentation comments for service contracts exposed through MCP.
  /// </summary>
  public sealed class DynamicMcpXmlDocumentationProvider {

    private readonly Dictionary<string, XDocument> _Documents = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the summary text for the specified method.
    /// </summary>
    /// <param name="methodInfo">The method to inspect.</param>
    /// <returns>The normalized XML summary or null.</returns>
    public string GetMethodSummary(MethodInfo methodInfo) {
      XElement memberElement = this.GetMemberElement(methodInfo);
      if (memberElement == null) {
        return null;
      }

      XElement summaryElement = memberElement.Element("summary");
      return this.NormalizeText(summaryElement);
    }

    /// <summary>
    /// Gets XML documentation comments for parameters of the specified method.
    /// </summary>
    /// <param name="methodInfo">The method to inspect.</param>
    /// <returns>A dictionary containing normalized parameter descriptions by parameter name.</returns>
    public Dictionary<string, string> GetParameterSummaries(MethodInfo methodInfo) {
      Dictionary<string, string> parameterSummaries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      XElement memberElement = this.GetMemberElement(methodInfo);
      if (memberElement == null) {
        return parameterSummaries;
      }

      IEnumerable<XElement> parameterElements = memberElement.Elements("param");
      foreach (XElement parameterElement in parameterElements) {
        XAttribute nameAttribute = parameterElement.Attribute("name");
        if (nameAttribute == null) {
          continue;
        }

        string parameterName = nameAttribute.Value;
        string parameterSummary = this.NormalizeText(parameterElement);
        if (string.IsNullOrWhiteSpace(parameterName) || string.IsNullOrWhiteSpace(parameterSummary)) {
          continue;
        }

        parameterSummaries[parameterName] = parameterSummary;
      }

      return parameterSummaries;
    }

    /// <summary>
    /// Gets the XML documentation member element for the specified method.
    /// </summary>
    /// <param name="methodInfo">The method to inspect.</param>
    /// <returns>The matching XML member element or null.</returns>
    private XElement GetMemberElement(MethodInfo methodInfo) {
      if (methodInfo == null) {
        return null;
      }
      if (methodInfo.DeclaringType == null) {
        return null;
      }

      XDocument document = this.GetXmlDocument(methodInfo.DeclaringType.Assembly);
      if (document == null) {
        return null;
      }

      string memberNameWithParameters = this.BuildMemberName(methodInfo, true);
      XElement memberElement = this.FindMemberElement(document, memberNameWithParameters);
      if (memberElement != null) {
        return memberElement;
      }

      string memberNameWithoutParameters = this.BuildMemberName(methodInfo, false);
      return this.FindMemberElement(document, memberNameWithoutParameters);
    }

    /// <summary>
    /// Loads the XML documentation document for an assembly.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The loaded XML documentation document or null.</returns>
    private XDocument GetXmlDocument(Assembly assembly) {
      if (assembly == null) {
        return null;
      }
      if (assembly.IsDynamic) {
        return null;
      }
      if (string.IsNullOrWhiteSpace(assembly.Location)) {
        return null;
      }

      string xmlDocumentationFile = Path.ChangeExtension(assembly.Location, ".xml");
      if (!File.Exists(xmlDocumentationFile)) {
        return null;
      }

      if (_Documents.ContainsKey(xmlDocumentationFile)) {
        return _Documents[xmlDocumentationFile];
      }

      XDocument document = XDocument.Load(xmlDocumentationFile);
      _Documents[xmlDocumentationFile] = document;
      return document;
    }

    /// <summary>
    /// Finds a member element by XML documentation member name.
    /// </summary>
    /// <param name="document">The XML documentation document.</param>
    /// <param name="memberName">The XML documentation member name.</param>
    /// <returns>The matching member element or null.</returns>
    private XElement FindMemberElement(XDocument document, string memberName) {
      if (document == null || string.IsNullOrWhiteSpace(memberName)) {
        return null;
      }

      IEnumerable<XElement> memberElements = document.Descendants("member");
      foreach (XElement memberElement in memberElements) {
        XAttribute nameAttribute = memberElement.Attribute("name");
        if (nameAttribute == null) {
          continue;
        }
        if (string.Equals(nameAttribute.Value, memberName, StringComparison.Ordinal)) {
          return memberElement;
        }
      }

      return null;
    }

    /// <summary>
    /// Builds an XML documentation member name for a method.
    /// </summary>
    /// <param name="methodInfo">The method to inspect.</param>
    /// <param name="includeParameters">True to include parameter type names.</param>
    /// <returns>The XML documentation member name.</returns>
    private string BuildMemberName(MethodInfo methodInfo, bool includeParameters) {
      string typeName = methodInfo.DeclaringType.FullName.Replace('+', '.');
      string memberName = "M:" + typeName + "." + methodInfo.Name;
      if (!includeParameters) {
        return memberName;
      }

      ParameterInfo[] parameters = methodInfo.GetParameters();
      if (parameters.Length == 0) {
        return memberName;
      }

      string[] parameterTypeNames = parameters.Select((parameter) => this.GetXmlTypeName(parameter.ParameterType)).ToArray();
      return memberName + "(" + string.Join(",", parameterTypeNames) + ")";
    }

    /// <summary>
    /// Gets the XML documentation type name for a CLR type.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>The XML documentation type name.</returns>
    private string GetXmlTypeName(Type type) {
      if (type.IsByRef) {
        type = type.GetElementType();
      }
      if (type.IsArray) {
        return this.GetXmlTypeName(type.GetElementType()) + "[]";
      }
      if (type.IsGenericType) {
        string genericTypeName = type.GetGenericTypeDefinition().FullName;
        int genericMarkerIndex = genericTypeName.IndexOf('`');
        if (genericMarkerIndex >= 0) {
          genericTypeName = genericTypeName.Substring(0, genericMarkerIndex);
        }

        string[] argumentTypeNames = type.GetGenericArguments().Select((argument) => this.GetXmlTypeName(argument)).ToArray();
        return genericTypeName.Replace('+', '.') + "{" + string.Join(",", argumentTypeNames) + "}";
      }

      return type.FullName.Replace('+', '.');
    }

    /// <summary>
    /// Normalizes XML documentation text for single-line MCP descriptions.
    /// </summary>
    /// <param name="element">The XML element to read.</param>
    /// <returns>The normalized text or null.</returns>
    private string NormalizeText(XElement element) {
      if (element == null) {
        return null;
      }

      string value = element.Value;
      if (string.IsNullOrWhiteSpace(value)) {
        return null;
      }

      string[] parts = value.Split(new char[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
      return string.Join(" ", parts);
    }

  }

}
