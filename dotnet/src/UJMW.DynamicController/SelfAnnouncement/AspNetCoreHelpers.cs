using Microsoft.AspNetCore.Hosting.Server.Features;
using System;
using System.Linq;
using System.Net;

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

  }

}
