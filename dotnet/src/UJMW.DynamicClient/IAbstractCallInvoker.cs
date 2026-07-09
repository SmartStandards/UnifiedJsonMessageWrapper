using System;
using System.Collections.Generic;
using System.Reflection;

namespace System.Web.UJMW {

  public interface IAbstractCallInvoker {

    //WARNING: WILL BE INVOKED VIA EMIT - WEAK REFERENCE!!!
		object InvokeCall(string uniqueMethodNameOnTransportLayer, MethodInfo method, object[] arguments, string[] argumentNames, string methodSignatureString);

	}

  public interface IUjmwClient {

    IAbstractCallInvoker GetInvoker();

    Type GetContract();

  }

}