using Microsoft.AspNetCore.Hosting.Server.Features;
using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

namespace Microsoft.AspNetCore.SmartStandards {

  internal static class AspNetCoreHelpers {

    public static string[] GetValidHttpAddresses(this IServerAddressesFeature serverAddressesFeature) {

      string[] validHttpAddresses = serverAddressesFeature.Addresses.Where( //filter out non-http(s) bindings like 'net.tcp://...' or 'net.pipe://...'
         (uglyBaseUrlBindingPattern) => uglyBaseUrlBindingPattern.StartsWith("http", StringComparison.CurrentCultureIgnoreCase)
      ).Select(
         (uglyBaseUrlBindingPattern) => {

           string baseUrl;

           if (uglyBaseUrlBindingPattern.Contains("//*")) {
             string thisHostName = Environment.MachineName;
             try {
               IPHostEntry entry = Dns.GetHostEntry(Dns.GetHostName());
               if (!string.IsNullOrWhiteSpace(entry?.HostName)) {
                 thisHostName = entry.HostName;
               }
             }
             catch {
             }
             baseUrl = uglyBaseUrlBindingPattern.Replace("//*", "//" + thisHostName).Replace(":*", "");
           }
           else {
             baseUrl = uglyBaseUrlBindingPattern.Replace(":*", "");
           }

           if (baseUrl.StartsWith("http:", StringComparison.CurrentCultureIgnoreCase)) {

             //crazy sh** (we had thad case!)
             if (!baseUrl.StartsWith("http://", StringComparison.CurrentCultureIgnoreCase)) {
               //build the scheme completely new, becaus it seems to be corrupted...
               baseUrl = "https://" + baseUrl.Substring(5).TrimStart('/');
             }

             baseUrl = baseUrl.Replace(":80", ""); //remove unnecessary port specification for http
           }
           else if (baseUrl.StartsWith("https:", StringComparison.CurrentCultureIgnoreCase)) {

             //crazy sh** (we had thad case!)
             if (!baseUrl.StartsWith("https://", StringComparison.CurrentCultureIgnoreCase)) {
               //build the scheme completely new, becaus it seems to be corrupted...
               baseUrl = "https://" + baseUrl.Substring(6).TrimStart('/');
             }

             baseUrl = baseUrl.Replace(":443", ""); //remove unnecessary port specification for https
           }

           return baseUrl;
         }
       ).ToArray();

      return validHttpAddresses;
    }

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
