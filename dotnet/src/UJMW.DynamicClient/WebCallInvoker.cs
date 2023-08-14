// COPIED FROM https://github.com/KornSW/DynamicProxy
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static System.Web.UJMW.DynamicClientFactory;

namespace System.Web.UJMW {

  internal class WebCallInvoker: IDynamicProxyInvoker {

    private Type _ApplicableType;
    private HttpPostMethod _HttpPostMethod;
    private Func<string> _UrlGetter;
    private RequestSidechannelCaptureMethod _RequestSidechannelCaptureMethod;
    private ResponseSidechannelRestoreMethod _ResponseSidechannelRestoreMethod;

    public WebCallInvoker(
      Type applicableType,
      HttpPostMethod httpPostMethod,
      Func<string> urlGetter,
      RequestSidechannelCaptureMethod requestSidechannelCaptureMethod,
      ResponseSidechannelRestoreMethod responseSidechannelRestoreMethod
    ) {
      _ApplicableType = applicableType;
      _HttpPostMethod = httpPostMethod;
      _UrlGetter = urlGetter;
      _RequestSidechannelCaptureMethod = requestSidechannelCaptureMethod;
      _ResponseSidechannelRestoreMethod = responseSidechannelRestoreMethod;
    } 

    private Dictionary<string, Func<object[], object>> _MethodsPerSignature = new Dictionary<string, Func<object[], object>>();

    public void DefineMethod(string methodName, Action method) {
      var signature = method.Method.ToString().Replace(method.Method.Name + "(", methodName + "(");
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          method.Invoke();
          return null;
        }
      );

    }

    public void DefineMethod<TArg1>(string methodName, Action<TArg1> method) {
      var signature = method.Method.ToString().Replace(method.Method.Name + "(", methodName + "(");
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          method.Invoke((TArg1)args[0]);
          return null;
        }
      );
    }

    public void DefineMethod<TArg1, TArg2>(string methodName, Action<TArg1, TArg2> method) {
      var signature = method.Method.ToString().Replace(method.Method.Name + "(", methodName + "(");
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          method.Invoke((TArg1)args[0], (TArg2)args[1]);
          return null;
        }
      );

    }

    public void DefineMethod<TResult>(string methodName, Func<TResult> method) {
      var signature = method.Method.ToString().Replace(method.Method.Name + "(", methodName + "(");
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          return method.Invoke();
        }
      );
    }

    public void DefineMethod<TArg1, TResult>(string methodName, Func<TArg1, TResult> method) {
      var signature = method.Method.ToString().Replace(method.Method.Name + "(", methodName + "(");
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          return method.Invoke((TArg1)args[0]);
        }
      );
    }

    public void DefineMethod<TArg1, TArg2, TResult>(string methodName, Func<TArg1, TArg2, TResult> method) {
      var signature = method.Method.ToString().Replace(method.Method.Name + "(", methodName + "(");
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          return method.Invoke((TArg1)args[0], (TArg2)args[1]);
        }
      );
    }

    public void DefineMethod(string methodName, Type[] paramTypes, Action<object[]> method) {
      var signature = "Void " + methodName + "(" + String.Join(", ", paramTypes.Select((p) => p.ToString()).ToArray()) + ")";
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          method.Invoke(args);
          return null;
        }
      );
    }

    public void DefineMethod(string methodName, ParameterInfo[] paramTypes, Action<object[]> method) {
      var signature = "Void " + methodName + "(" + String.Join(", ", paramTypes.Select((p) => p.ParameterType.ToString()).ToArray()) + ")";
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          method.Invoke(args);
          return null;
        }
      );
    }

    public void DefineMethod(MethodInfo methodInfo, Action<object[]> method) {
      var signature = methodInfo.ToString();
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          method.Invoke(args);
          return null;
        }
      );
    }


    public void DefineMethod(string methodName, Type[] paramTypes, Type returnType, Func<object[], object> method) {
      var signature = returnType.ToString() + " " + methodName + "(" + String.Join(", ", paramTypes.Select((p) => p.ToString()).ToArray()) + ")";
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          var result = method.Invoke(args);
          return result;
        }
      );
    }

    public void DefineMethod(string methodName, ParameterInfo[] paramTypes, Type returnType, Func<object[], object> method) {
      var signature = returnType.ToString() + " " + methodName + "(" + String.Join(", ", paramTypes.Select((p) => p.ParameterType.ToString()).ToArray()) + ")";
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          var result = method.Invoke(args);
          return result;
        }
      );
    }

    public void DefineMethod(MethodInfo methodInfo, Func<object[], object> method) {
      var signature = methodInfo.ToString();
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          var result = method.Invoke(args);
          return result;
        }
      );
    }

    public void DefineMethod(MethodInfo methodInfo, string overrideMethodName, Func<object[], object> method) {
      var signature = methodInfo.ToString().Replace(methodInfo.Name + "(", overrideMethodName + "(");
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          var result = method.Invoke(args);
          return result;
        }
      );
    }

    public void DefineMethod(MethodInfo methodInfo, object targetObject) {
      var signature = methodInfo.ToString();
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          var result = methodInfo.Invoke(targetObject,args);
          return result;
        }
      );
    }

    public void DefineMethod(MethodInfo methodInfo, string overrideMethodName, object targetObject) {
      var signature = methodInfo.ToString().Replace(methodInfo.Name + "(", overrideMethodName + "(");
      _MethodsPerSignature.Add(
        signature,
        (object[] args) => {
          var result = methodInfo.Invoke(targetObject, args);
          return result;
        }
      );
    }

    public object InvokeMethod(string methodName, object[] arguments, string[] argumentNames, string methodSignatureString) {

      Func<object[], object> handle = null;
      lock (_MethodsPerSignature){
        _MethodsPerSignature.TryGetValue(methodSignatureString, out handle);
      }

      if(handle != null) {
        return handle.Invoke(arguments);
      }
      else {
        return this.FallbackInvokeMethod.Invoke(methodName, arguments, argumentNames, methodSignatureString);
      }

    }

    public delegate object FallbackInvokeMethodDelegate(string methodName, object[] arguments, string[] argumentNames, string methodSignatureString);

    public FallbackInvokeMethodDelegate FallbackInvokeMethod { get; set; } =
      (m, a, n, sig) => throw new NotImplementedException($"There is no Implementation for Method {sig}!");

  }

}