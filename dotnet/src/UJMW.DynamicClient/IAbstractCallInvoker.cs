using System;
using System.Collections.Generic;

namespace System.Web.UJMW {

  public interface IAbstractCallInvoker {

		object InvokeCall(string methodName, object[] arguments, string[] argumentNames, string methodSignatureString);

	}

}