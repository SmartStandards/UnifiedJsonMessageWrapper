using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;

namespace UJMW.CommandLineFacade {

  internal static class HelperExtensions {

    /// <summary></summary>
    /// <param name="method"></param>
    /// <param name="ensureCompilableName">Treat '-' as new word and let every word start in upper-case (PascalCasing)</param>
    /// <returns></returns>
    internal static string GetNameOrOverride(this MethodInfo method, bool ensureCompilableName) {
      var displayNameAttr = method.GetCustomAttribute<System.ComponentModel.DisplayNameAttribute>();
      string name = method.Name;
      if (displayNameAttr != null) {
        name = displayNameAttr.DisplayName;
      }
      if (ensureCompilableName) {
        //first chasr alwasy upper + treat '-' as new word (starts also with upper):
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++) {
          if (i == 0) {
            sb.Append(char.ToUpper(name[i]));
          }
          else if (name[i] == '-') {
            if (i + 1 < name.Length) {
              sb.Append(char.ToUpper(name[i + 1]));
              i++;
            }
          }
          else {
            sb.Append(name[i]);
          }
        }
        name = sb.ToString();
      }
      return name;
    }

  }

}
