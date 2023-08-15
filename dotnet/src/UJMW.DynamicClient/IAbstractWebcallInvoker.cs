using System;
using System.Collections.Generic;

namespace System.Web.UJMW {

  public interface IAbstractWebcallInvoker {

		object InvokeWebCall(string methodName, object[] arguments, string[] argumentNames, string methodSignatureString);

	}

}