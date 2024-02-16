using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;

namespace System.Web.UJMW {

  public delegate void RequestSidechannelCaptureMethod(IDictionary<string, string> requestSidechannelContainer);
  public delegate void ResponseSidechannelProcessingMethod(IEnumerable<KeyValuePair<string, string>> responseSidechannelContainer);

  //developed on base of https://github.com/KornSW/DynamicProxy
  public abstract class DynamicClientFactory {

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private IAbstractWebcallInvoker _Invoker;

    #region " CreateInstance - Convenience overloads " 

    public static TApplicable CreateInstance<TApplicable>(string url){
      return CreateInstance<TApplicable>(() => url);
    }

    public static TApplicable CreateInstance<TApplicable>(string url, string httpAuthHeader) {
      return CreateInstance<TApplicable>(url, ()=> httpAuthHeader);
    }

    public static TApplicable CreateInstance<TApplicable>(string url, Func<string> httpAuthHeaderGetter) {
      var httpClient = new HttpClient();
      var httpPostExecutor = new WebClientBasedHttpPostExecutor(httpClient, httpAuthHeaderGetter);
      UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(typeof(TApplicable), httpPostExecutor, () => url, httpClient.Dispose);
      return CreateInstance<TApplicable>(invoker);
    }

    public static TApplicable CreateInstance<TApplicable>(Func<string> urlGetter, Func<string> httpAuthHeaderGetter = null) {
      var httpClient = new HttpClient();
      if(httpAuthHeaderGetter == null && UjmwClientConfiguration.DefaultAuthHeaderGetter != null) {
        httpAuthHeaderGetter = ()=>UjmwClientConfiguration.DefaultAuthHeaderGetter.Invoke(typeof(TApplicable));
      }
      var httpPostExecutor = new WebClientBasedHttpPostExecutor(httpClient, httpAuthHeaderGetter);
      UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(typeof(TApplicable), httpPostExecutor, urlGetter, httpClient.Dispose);
      return CreateInstance<TApplicable>(invoker);
    }

    public static object CreateInstance(Type applicableType,Func<string> urlGetter, Func<string> httpAuthHeaderGetter) {
      var httpClient = new HttpClient();
      var httpPostExecutor = new WebClientBasedHttpPostExecutor(httpClient, httpAuthHeaderGetter);
      UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(applicableType, httpPostExecutor, urlGetter, httpClient.Dispose);
      return CreateInstance(applicableType,invoker);
    }

    //public static TApplicable CreateInstance<TApplicable>(HttpClient httpClient, Func<string> urlGetter) {
    //  var httpPostExecutor = new WebClientBasedHttpPostExecutor(httpClient);
    //  UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(typeof(TApplicable), httpPostExecutor, urlGetter);
    //  return CreateInstance<TApplicable>(invoker);
    //}

    //public static object CreateInstance(Type applicableType, HttpClient httpClient, Func<string> urlGetter) {
    //  var httpPostExecutor = new WebClientBasedHttpPostExecutor(httpClient);
    //  UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(applicableType, httpPostExecutor, urlGetter);
    //  return CreateInstance(applicableType, invoker);
    //}

    public static TApplicable CreateInstance<TApplicable>(HttpPostExecutor httpPostExecutor, Func<string> urlGetter) {
      UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(typeof(TApplicable), httpPostExecutor, urlGetter);
      return CreateInstance<TApplicable>(invoker);
    }

    public static object CreateInstance(Type applicableType, HttpPostExecutor httpPostExecutor, Func<string> urlGetter) {
      UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(applicableType, httpPostExecutor, urlGetter);
      return CreateInstance(applicableType, invoker);
    }

    #endregion

    private static TApplicable CreateInstance<TApplicable>(IAbstractWebcallInvoker invoker, params object[] constructorArgs) {
      return (TApplicable)CreateInstance(typeof(TApplicable), invoker, constructorArgs);
    }

    private static object CreateInstance(Type applicableType, IAbstractWebcallInvoker invoker, params object[] constructorArgs) {
      Type dynamicType = BuildDynamicType(applicableType);
      var extendedConstructorArgs = constructorArgs.ToList();
      extendedConstructorArgs.Add(invoker);
      var instance = Activator.CreateInstance(dynamicType, extendedConstructorArgs.ToArray());
      return instance;
    }

    internal static Type BuildDynamicType<TApplicable>() {
      return BuildDynamicType(typeof(TApplicable));
    }

    internal static Type BuildDynamicType(Type applicableType) {

      Type iDynamicProxyInvokerType = typeof(IAbstractWebcallInvoker);
      MethodInfo iDynamicProxyInvokerTypeInvokeMethod = iDynamicProxyInvokerType.GetMethod(nameof(IAbstractWebcallInvoker.InvokeWebCall));

      AssemblyBuilder assemblyBuilder = null;
      Type baseType = null;
      if ((applicableType.IsClass)) {
        baseType = applicableType;
      }

      //##### ASSEMBLY & MODULE DEFINITION #####

      var assemblyName = new AssemblyName(applicableType.Name + "_DynamicUjmwClient_Assembly");

#if NET46
      assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndSave);
#endif
#if NET5
      assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
#endif

      var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name);

      // ##### CLASS DEFINITION #####

      TypeBuilder typeBuilder;
      if (baseType is object) {
        typeBuilder = moduleBuilder.DefineType(applicableType.Name + "_DynamicUjmwClient", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.AutoLayout, baseType);
      }
      // CODE: Public Class <MyApplicableType>_DyamicProxyClass
      // Inherits <MyApplicableType>
      else {
        typeBuilder = moduleBuilder.DefineType(applicableType.Name + "_DynamicUjmwClient", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.AutoLayout);
        typeBuilder.AddInterfaceImplementation(applicableType);
        // CODE: Public Class <MyApplicableType>_DyamicProxyClass
        // Implements <MyApplicableType>
      }

      // ##### FIELD DEFINITIONs #####

      var fieldBuilderDynamicProxyInvoker = typeBuilder.DefineField("_DynamicProxyInvoker", iDynamicProxyInvokerType, FieldAttributes.Private);

      // ##### CONSTRUCTOR DEFINITIONs #####

      if (baseType is object) {

        // create a proxy for each constructor in the base class
        foreach (var constructorOnBase in baseType.GetConstructors()) {
          var constructorArgs = new List<Type>();
          foreach (var p in constructorOnBase.GetParameters())
            constructorArgs.Add(p.ParameterType);
          constructorArgs.Add(typeof(IAbstractWebcallInvoker));
          var constructorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, CallingConventions.Standard, constructorArgs.ToArray());
          // CODE: Public Sub New([...],dynamicProxyInvoker As IDynamicProxyInvoker)

          // Dim dynamicProxyInvokerCParam = constructorBuilder.DefineParameter(constructorArgs.Count, ParameterAttributes.In, "dynmaicProxyInvoker")

          {
            var withBlock = constructorBuilder.GetILGenerator();
            withBlock.Emit(OpCodes.Nop); // ------------------
            withBlock.Emit(OpCodes.Ldarg, 0); // load Argument(0) (which is a pointer to the instance of our class)
            for (int i = 1, loopTo = constructorArgs.Count - 1; i <= loopTo; i++)
              withBlock.Emit(OpCodes.Ldarg, (byte)i); // load the other Arguments (Constructor-Params) excluding the last one
            withBlock.Emit(OpCodes.Call, constructorOnBase); // CODE: MyBase.New([...])
            withBlock.Emit(OpCodes.Nop); // ------------------
            withBlock.Emit(OpCodes.Ldarg, 0); // load Argument(0) (which is a pointer to the instance of our class)
            byte argIndex = (byte)constructorArgs.Count;
            // TODO: prüfen ob valutype!!!!! <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
            // .Emit(OpCodes.Ldarg, argIndex) 'load the last Argument (Constructor-Param: IDynamicProxyInvoker)
            withBlock.Emit(OpCodes.Ldarg_S, argIndex); // load the last Argument (Constructor-Param: IDynamicProxyInvoker)
            withBlock.Emit(OpCodes.Stfld, fieldBuilderDynamicProxyInvoker); // CODE: _DynamicProxyInvoker = dynamicProxyInvoker
            withBlock.Emit(OpCodes.Nop);
            withBlock.Emit(OpCodes.Ret); // ------------------
          }
        }
      }
      else // THIS IS WHEN WERE IMPLEMENTING AN INTERFACE INSTEAD OF INHERITING A CLASS
      {
        var constructorBuilder = typeBuilder.DefineConstructor(
          MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
          CallingConventions.HasThis,
          new[] { typeof(IAbstractWebcallInvoker) }
        );

        // CODE: Public Sub New(dynamicProxyInvoker As IDynamicProxyInvoker)

        {
          var constructorIlGen = constructorBuilder.GetILGenerator();
          constructorIlGen.Emit(OpCodes.Nop); // ------------------
          constructorIlGen.Emit(OpCodes.Ldarg, 0); // load Argument(0) (which is a pointer to the instance of our class)
          constructorIlGen.Emit(OpCodes.Ldarg, 1); // load the Argument (Constructor-Param: IDynamicProxyInvoker)
          constructorIlGen.Emit(OpCodes.Stfld, fieldBuilderDynamicProxyInvoker); // CODE: _DynamicProxyInvoker = dynamicProxyInvoker
          constructorIlGen.Emit(OpCodes.Ret); // ------------------
        }
      }

      // ##### METHOD DEFINITIONs #####

      var allMethods = new List<MethodInfo>();
      CollectAllMethodsForType(applicableType, allMethods);

      foreach (var mi in allMethods) {
        var methodSignatureString = mi.ToString();
        var methodNameBlacklist = new[] { "ToString", "GetHashCode", "GetType", "Equals" };
        if (!mi.IsSpecialName && !methodNameBlacklist.Contains(mi.Name)) {
          bool isOverridable = !mi.Attributes.HasFlag(MethodAttributes.Final);
          if (mi.IsPublic && (baseType is null || isOverridable)) {

            var realParamTypes = new List<Type>();
            var paramTypesOrRefTypes = new List<Type>();
            var paramNames = new List<String>();
            var paramEvalIsValueType = new List<bool>();
            var paramEvalIsByRef = new List<bool>();
            var paramEvalIsOut = new List<bool>();

            foreach (ParameterInfo pi in mi.GetParameters()) {
              Type realType;

              if (pi.ParameterType.IsByRef) {
                realType = pi.ParameterType.GetElementType();
                paramEvalIsByRef.Add(true);
              }
              else {
                realType = pi.ParameterType;
                paramEvalIsByRef.Add(false);
              }
              paramTypesOrRefTypes.Add(pi.ParameterType);
              realParamTypes.Add(realType);
              paramNames.Add(pi.Name);
              paramEvalIsValueType.Add(realType.IsValueType);
              paramEvalIsOut.Add(pi.IsOut);
            }

            var methodBuilder = typeBuilder.DefineMethod(mi.Name, MethodAttributes.Public | MethodAttributes.ReuseSlot | MethodAttributes.HideBySig | MethodAttributes.Virtual, mi.ReturnType, paramTypesOrRefTypes.ToArray());
            var paramBuilders = new ParameterBuilder[paramNames.Count];
            for (int paramIndex = 0, loopTo1 = paramNames.Count - 1; paramIndex <= loopTo1; paramIndex++) {
              if (paramEvalIsOut[paramIndex]) {
                paramBuilders[paramIndex] = methodBuilder.DefineParameter(paramIndex + 1, ParameterAttributes.Out, paramNames[paramIndex]);
              }
              else if (paramEvalIsByRef[paramIndex]) {
                paramBuilders[paramIndex] = methodBuilder.DefineParameter(paramIndex + 1, ParameterAttributes.In | ParameterAttributes.Out, paramNames[paramIndex]);
              }
              else {
                paramBuilders[paramIndex] = methodBuilder.DefineParameter(paramIndex + 1, ParameterAttributes.In, paramNames[paramIndex]);
              }

              // TODO: optionale parameter

            }

            {
              var methodIlGen = methodBuilder.GetILGenerator();

              // ##### LOCAL VARIABLE DEFINITIONs #####

              LocalBuilder localReturnValue = null;
              if (mi.ReturnType is object && !(mi.ReturnType.Name == "Void")) {
                localReturnValue = methodIlGen.DeclareLocal(mi.ReturnType);
              }

              var argumentRedirectionArray = methodIlGen.DeclareLocal(typeof(object[]));
              var argumentNameArray = methodIlGen.DeclareLocal(typeof(string[]));
              methodIlGen.Emit(OpCodes.Nop); // ------------------------------------------------------------------------

              // ARRAY-INSTANZIIEREN
              methodIlGen.Emit(OpCodes.Ldc_I4_S, (byte)paramNames.Count); // CODE: Zahl x als (int32) wobei x die anzhalt der parameter unseerer methode ist
              methodIlGen.Emit(OpCodes.Newarr, typeof(object)); // CODE: Dim args(x) As Object
              methodIlGen.Emit(OpCodes.Stloc, argumentRedirectionArray);
              methodIlGen.Emit(OpCodes.Nop); // ------------------------------------------------------------------------

              // ARRAY-INSTANZIIEREN
              methodIlGen.Emit(OpCodes.Ldc_I4_S, (byte)paramNames.Count); // CODE: Zahl x als (int32) wobei x die anzhalt der parameter unseerer methode ist
              methodIlGen.Emit(OpCodes.Newarr, typeof(string)); // CODE: Dim args(x) As Object
              methodIlGen.Emit(OpCodes.Stloc, argumentNameArray);

              // ------------------------------------------------------------------------

              // parameter in transport-array übertragen
              for (int paramIndex = 0, loopTo2 = paramNames.Count - 1; paramIndex <= loopTo2; paramIndex++) {
                bool paramIsValueType = paramEvalIsValueType[paramIndex];
                bool paramIsOut = paramEvalIsOut[paramIndex];
                bool paramIsRef = paramEvalIsByRef[paramIndex];
                var paramType = realParamTypes[paramIndex];

                if (!paramIsOut) {

                  methodIlGen.Emit(OpCodes.Ldloc, argumentRedirectionArray); // transport-array laden
                  methodIlGen.Emit(OpCodes.Ldc_I4_S, (byte)paramIndex); // arrayindex als integer (zwecks feld-addressierung) erzeugen
                
                  if (paramIsRef) {

                    // resolve incomming byref handle into a new object address
                    if (paramIsValueType) {
                      // methodIlGen.Emit(OpCodes.Ldarga_S, paramIndex + 1); // zuzuweisendes methoden-argument (bzw. desse nadresse) auf den stack holen
                      methodIlGen.Emit(OpCodes.Ldarg, paramIndex + 1);
                      methodIlGen.Emit(OpCodes.Ldobj, paramType);
                    }
                    else {

                      //methodIlGen.Emit(OpCodes.Ldarga_S, paramIndex + 1); // zuzuweisendes methoden-argument (bzw. desse nadresse) auf den stack holen

                      methodIlGen.Emit(OpCodes.Ldarg, paramIndex + 1);// zuzuweisendes methoden-argument auf den stack holen
                      methodIlGen.Emit(OpCodes.Ldind_Ref);
                    }

                  }
                  else {
                    methodIlGen.Emit(OpCodes.Ldarg, paramIndex + 1);// zuzuweisendes methoden-argument auf den stack holen
                  }

                  if (paramIsValueType) {
                    methodIlGen.Emit(OpCodes.Box, paramType); // value-types müssen geboxed werden, weil die array-felder vom typ "object" sind
                  }

                  methodIlGen.Emit(OpCodes.Stelem_Ref); // ins transport-array hineinschreiben
                }

                // ------------------------------------------------------------------------

                methodIlGen.Emit(OpCodes.Ldloc, argumentNameArray); // transport-array laden
                methodIlGen.Emit(OpCodes.Ldc_I4_S, (byte)paramIndex); // arrayindex als integer (zwecks feld-addressierung) erzeugen
                methodIlGen.Emit(OpCodes.Ldstr, paramNames[paramIndex]); // name als string bereitlegen (als array inhalt)
                methodIlGen.Emit(OpCodes.Stelem_Ref); // ins transport-array hineinschreiben
              }

              methodIlGen.Emit(OpCodes.Ldarg_0); // < unsere klasseninstanz auf den stack
              methodIlGen.Emit(OpCodes.Ldfld, fieldBuilderDynamicProxyInvoker); // feld '_DynamicProxyInvoker' laden auf den stack)
              methodIlGen.Emit(OpCodes.Ldstr, mi.Name); // < methodenname als string auf den stack holen
              methodIlGen.Emit(OpCodes.Ldloc, argumentRedirectionArray); // pufferarray auf den stack holen
              methodIlGen.Emit(OpCodes.Ldloc, argumentNameArray); // pufferarray auf den stack holen
              methodIlGen.Emit(OpCodes.Ldstr, methodSignatureString); // < methoden-signatur als string auf den stack holen

              // aufruf auf umgeleitete funktion absetzen
              methodIlGen.Emit(OpCodes.Callvirt, iDynamicProxyInvokerTypeInvokeMethod); // _DynamicProxyInvoker.InvokeMethod("Foo", args)
                                                                                       // jetzt liegt ein result auf dem stack...
              if (localReturnValue is null) {
                methodIlGen.Emit(OpCodes.Pop); // result (void) vom stack löschen (weil wir nix zurückgeben)
              }
              else if (mi.ReturnType.IsValueType) {
                methodIlGen.Emit(OpCodes.Unbox_Any, mi.ReturnType); // value-types müssen unboxed werden, weil der retval in "object" ist
                methodIlGen.Emit(OpCodes.Stloc, localReturnValue); // < speichere es in 'returnValueBuffer'
              }
              else {
                methodIlGen.Emit(OpCodes.Castclass, mi.ReturnType); // reference-types müssen gecastet werden, weil der retval in "object" ist
                methodIlGen.Emit(OpCodes.Stloc, localReturnValue); // < speichere es in 'returnValueBuffer'
              }

              //ByRef-/Out-Parameter aus transport-array "auspacken" und zurückschreiben!!!
              for (int paramIndex = 0, loopTo2 = paramNames.Count - 1; paramIndex <= loopTo2; paramIndex++) {
                bool paramIsValueType = paramEvalIsValueType[paramIndex];
                bool paramIsOut = paramEvalIsOut[paramIndex];
                bool paramIsRef = paramEvalIsByRef[paramIndex];
                var realParamType = realParamTypes[paramIndex];
                if (paramIsRef) {

                  //methodIlGen.Emit(OpCodes.Ldarga_S, paramIndex + 1); // zuzuweisendes methoden-argument auf den stack holen
                  methodIlGen.Emit(OpCodes.Ldarg, paramIndex + 1); //argument-handle holen (als zuweisungs-ziel)

                  methodIlGen.Emit(OpCodes.Ldloc, argumentRedirectionArray); // transport-array laden
                  methodIlGen.Emit(OpCodes.Ldc_I4_S, (byte)paramIndex); // arrayindex als integer (zwecks feld-addressierung) erzeugen
                  
                  methodIlGen.Emit(OpCodes.Ldelem_Ref); //array-inhalt (object-handle) auf den stack holen

                  if (paramIsValueType) {
                    methodIlGen.Emit(OpCodes.Unbox_Any, realParamType); //array-inhalt auf den stack holen
                  }
   
                  methodIlGen.Emit(OpCodes.Stind_Ref); //wert in die adresse des arguments schreiben
                }
              }

              if (localReturnValue is object) {
                methodIlGen.Emit(OpCodes.Ldloc, localReturnValue);
              }

              methodIlGen.Emit(OpCodes.Ret);
            }

            // note: 'DefineMethodOverride' is also used for implementing interface-methods
            typeBuilder.DefineMethodOverride(methodBuilder, mi);
          }
        }
      }

      var dynamicType = typeBuilder.CreateType();
      // assemblyBuilder.Save("Dynassembly.dll")
      return dynamicType;
    }

    private static void CollectAllMethodsForType(Type t, List<MethodInfo> target) {
      foreach (MethodInfo mi in t.GetMethods()) {
        target.Add(mi);
      }
      if(t.BaseType != null) {
        CollectAllMethodsForType(t.BaseType, target);
      }
      foreach (Type intf in t.GetInterfaces()) {
        CollectAllMethodsForType(intf, target);
      }
    }

  }
}