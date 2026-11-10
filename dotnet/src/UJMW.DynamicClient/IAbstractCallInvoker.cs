using System;
using System.Collections.Generic;
using System.Reflection;

namespace System.Web.UJMW {

  public interface IAbstractCallInvoker {


    /// <summary> </summary>
    /// <param name="uniqueMethodNameOnTransportLayer">can be 'mySubroute/myReMappedMerhodName'</param>
    /// <param name="method"></param>
    /// <param name="arguments"></param>
    /// <param name="argumentNames"></param>
    /// <param name="methodSignatureString"></param>
    /// <returns></returns>
		object InvokeCall(
      string uniqueMethodNameOnTransportLayer, MethodInfo method, object[] arguments, string[] argumentNames, string methodSignatureString
    );
    //WARNING WHEN REFACTORING: WILL ALSO BE INVOKED VIA EMIT - WEAK REFERENCE!!! 

	}

  public interface IUjmwClient {

    IAbstractCallInvoker GetInvoker();
    string GetSubClientPath();
    Type GetContract();

    /// <summary>
    /// Overrides the polling interval of long-running operations: the 'pollNbf' provided by the server
    /// is ignored and the client polls every 'fixedSeconds' instead (0 becomes 500 ms internally).
    /// A negative value restores the default behavior (respecting the 'pollNbf').
    /// NOTE: the setting applies to the underlying invoker (and is therefore shared with all sub-clients).
    /// </summary>
    void OverrideNbfPolling(int fixedSeconds);

  }

}