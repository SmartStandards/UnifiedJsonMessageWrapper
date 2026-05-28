using Logging.SmartStandards.CopyForUJMW;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;

namespace System.Web.UJMW {

  internal static partial class TypeAliasBuilder {

    internal static string BuildTypeAliasRecursive(this Type t, bool simplified, int level, string namespaceToSkip) {

      if (t.IsGenericType) {

        string[] paramNames = t.GenericTypeArguments.Select(
          (Type ta) => {
            if (ta.IsValueType && !ta.IsGenericType) {
              return ta.Name; //hier ohne NS
            }
            else {
              //namespaceToSkip will only be applied from level 2 on, so the root type's namespace will always be included...
              return BuildTypeAliasRecursive(ta, simplified, level + 1, namespaceToSkip ?? t.Namespace);
            }
          }
        ).ToArray();

        string separator = new string('_', level);

        string nameWithoutGpPrefix = t.Name.Substring(0, t.Name.IndexOf('`'));

        string nsToUse = t.Namespace;
        if (!string.IsNullOrWhiteSpace(namespaceToSkip) && nsToUse.StartsWith(namespaceToSkip)) {
          nsToUse = nsToUse.Substring(namespaceToSkip.Length);
        }
        if (nsToUse.StartsWith(".")) {
          nsToUse = nsToUse.Substring(1);
        }

        if (string.IsNullOrWhiteSpace(nsToUse) || nsToUse == "System" || simplified) {
          return $"{nameWithoutGpPrefix}{separator}{String.Join(separator, paramNames)}";
        }
        else {
          return $"{nsToUse}.{nameWithoutGpPrefix}{separator}{String.Join(separator, paramNames)}";
        }

      }
      else {

        string nsToUse = t.Namespace;
        if (!string.IsNullOrWhiteSpace(namespaceToSkip) && nsToUse.StartsWith(namespaceToSkip)) {
          nsToUse = nsToUse.Substring(namespaceToSkip.Length);
        }
        if (nsToUse.StartsWith(".")) {
          nsToUse = nsToUse.Substring(1);
        }
     
        if (string.IsNullOrWhiteSpace(nsToUse) || nsToUse == "System" || simplified) {
          return t.Name;
        }
        else {
          return $"{nsToUse}.{t.Name}";
        }

      }
    }

  }

}
