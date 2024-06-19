using System.Net;

namespace System.Web.UJMW {

  internal static class InternalExtensions {

    internal static bool TryGetValue(this WebHeaderCollection headers, string headerName, out string headerValue) {
      foreach (var entryKey in headers.AllKeys) {
        if (entryKey.Equals(headerName, StringComparison.InvariantCultureIgnoreCase)) { 
          headerValue = headers[entryKey];
          return true;
        }
      }
      headerValue = null;
      return false;
    }
        
  }

}
