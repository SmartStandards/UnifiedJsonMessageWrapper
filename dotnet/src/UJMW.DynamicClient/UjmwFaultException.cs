using System.Reflection;

namespace System.Web.UJMW {

  public class UjmwFaultException : Exception {

    public UjmwFaultException(string fullUrl, MethodInfo method, string faultMessage) : base(faultMessage) {
      _FullUrl = fullUrl;
      _Method = method;
    }

    private string _FullUrl;
    private MethodInfo _Method;

    public string FullUrl { 
      get {
        return _FullUrl;
      } 
    }

    public MethodInfo Method {
      get {
        return _Method;
      }
    }

  }

}
