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

    /// <summary>
    /// IMPORTANT: when using this overload, the url will be retrieved automatically,
    /// so the 'UjmwClientConfiguration.DefaultUrlGetter' needs to be initialized first!
    /// Otherwise this will cause to an exception!
    /// </summary>
    /// <typeparam name="TApplicable"></typeparam>
    /// <param name="customizingFlags">
    /// You can use this to select different customizing flavors (offered by the
    /// UjmwClientConfiguration.HttpClientFactory) in order to use adjusted transport-layer
    /// configurations like special timouts or proxy-settings.
    /// This wont be evaluated by the UJMW framework in default, it is just a channel to 
    /// support extended customizing usecases.
    /// </param>
    /// <returns></returns>
    public static TApplicable CreateInstance<TApplicable>(string[] customizingFlags = null) {
      return CreateInstance<TApplicable>(
        () => UjmwClientConfiguration.DefaultUrlGetter.Invoke(typeof(TApplicable)),
        () => UjmwClientConfiguration.DefaultAuthHeaderGetter.Invoke(typeof(TApplicable)),
        customizingFlags
      );
    }

    /// <summary>
    /// IMPORTANT: when using this overload, the url will be retrieved automatically,
    /// so the 'UjmwClientConfiguration.DefaultUrlGetter' needs to be initialized first!
    /// Otherwise this will cause to an exception!
    /// </summary>
    /// <param name="applicableType"></param>
    /// <param name="customizingFlags">
    /// You can use this to select different customizing flavors (offered by the
    /// UjmwClientConfiguration.HttpClientFactory) in order to use adjusted transport-layer
    /// configurations like special timouts or proxy-settings.
    /// This wont be evaluated by the UJMW framework in default, it is just a channel to 
    /// support extended customizing usecases.
    /// </param>
    /// <returns></returns>
    public static object CreateInstance(Type applicableType, string[] customizingFlags = null) {
      return CreateInstance(
        applicableType, 
        () => UjmwClientConfiguration.DefaultUrlGetter.Invoke(applicableType),
        () => UjmwClientConfiguration.DefaultAuthHeaderGetter.Invoke(applicableType),
        customizingFlags
      );
    }

    #region " Convenience Overloads with STRINGS instead of callbacks "

    public static TApplicable CreateInstance<TApplicable>(string url){
      return CreateInstance<TApplicable>(    
        () => url,
        () => UjmwClientConfiguration.DefaultAuthHeaderGetter.Invoke(typeof(TApplicable))
      ) ;
    }
    public static object CreateInstance(Type applicableType, string url) {
      return CreateInstance(
        applicableType,
        () => url,
        () => UjmwClientConfiguration.DefaultAuthHeaderGetter.Invoke(applicableType)
      );
    }

    public static TApplicable CreateInstance<TApplicable>(string url, string httpAuthHeader) {
      return CreateInstance<TApplicable>(
        () => url,
        () => httpAuthHeader
      );
    }
    public static object CreateInstance(Type applicableType, string url, string httpAuthHeader) {
      return CreateInstance(
        applicableType,
        () => url,
        () => httpAuthHeader
      );
    }

    #endregion

    /// <summary>
    /// </summary>
    /// <typeparam name="TApplicable"></typeparam>
    /// <param name="urlGetter"></param>
    /// <param name="httpAuthHeaderGetter"></param>
    /// <param name="customizingFlags">
    /// You can use this to select different customizing flavors (offered by the
    /// UjmwClientConfiguration.HttpClientFactory) in order to use adjusted transport-layer
    /// configurations like special timouts or proxy-settings.
    /// This wont be evaluated by the UJMW framework in default, it is just a channel to 
    /// support extended customizing usecases.
    /// </param>
    /// <returns></returns>
    public static TApplicable CreateInstance<TApplicable>(Func<string> urlGetter, Func<string> httpAuthHeaderGetter = null, string[] customizingFlags = null) {
      HttpClient httpClient = GetHttpClient(customizingFlags);

      if (httpAuthHeaderGetter == null && UjmwClientConfiguration.DefaultAuthHeaderGetter != null) {
        httpAuthHeaderGetter = ()=>UjmwClientConfiguration.DefaultAuthHeaderGetter.Invoke(typeof(TApplicable));
      }

      var httpPostExecutor = new WebClientBasedHttpPostExecutor(httpClient, httpAuthHeaderGetter);
      UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(typeof(TApplicable), httpPostExecutor, urlGetter);
      return CreateInstance<TApplicable>(invoker);
    }

    /// <summary>
    /// </summary>
    /// <param name="applicableType"></param>
    /// <param name="urlGetter"></param>
    /// <param name="httpAuthHeaderGetter"></param>
    /// <param name="customizingFlags">
    /// You can use this to select different customizing flavors (offered by the
    /// UjmwClientConfiguration.HttpClientFactory) in order to use adjusted transport-layer
    /// configurations like special timouts or proxy-settings.
    /// This wont be evaluated by the UJMW framework in default, it is just a channel to 
    /// support extended customizing usecases.
    /// </param>
    /// <returns></returns>
    public static object CreateInstance(Type applicableType, Func<string> urlGetter, Func<string> httpAuthHeaderGetter, string[] customizingFlags = null) {
      HttpClient httpClient = GetHttpClient(customizingFlags);

      if (httpAuthHeaderGetter == null && UjmwClientConfiguration.DefaultAuthHeaderGetter != null) {
        httpAuthHeaderGetter = ()=>UjmwClientConfiguration.DefaultAuthHeaderGetter.Invoke(applicableType);
      }

      var httpPostExecutor = new WebClientBasedHttpPostExecutor(httpClient, httpAuthHeaderGetter);
      UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(applicableType, httpPostExecutor, urlGetter);
      return CreateInstance(applicableType, invoker);
    }

    public static TApplicable CreateInstance<TApplicable>(IHttpPostExecutor httpPostExecutor, Func<string> urlGetter) {
      UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(typeof(TApplicable), httpPostExecutor, urlGetter);
      return CreateInstance<TApplicable>(invoker);
    }

    public static object CreateInstance(Type applicableType, IHttpPostExecutor httpPostExecutor, Func<string> urlGetter) {
      UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(applicableType, httpPostExecutor, urlGetter);
      return CreateInstance(applicableType, invoker);
    }

    #endregion

    private static TApplicable CreateInstance<TApplicable>(IAbstractWebcallInvoker invoker, params object[] constructorArgs) {
      return (TApplicable)CreateInstance(typeof(TApplicable), invoker, constructorArgs);
    }

    private static ModuleBuilder _CombinedBuilder = null;
    private static object CreateInstance(Type applicableType, IAbstractWebcallInvoker invoker, params object[] constructorArgs) {
      Type dynamicType;
      if (UjmwClientConfiguration.UseCombinedDynamicAssembly) {
        if(_CombinedBuilder == null) {
          _CombinedBuilder = CreateAssemblyModuleBuilder("UJMW.InMemoryClients");
        }
        dynamicType = BuildDynamicType(applicableType, _CombinedBuilder);
      }
      else {
        dynamicType = BuildDynamicType(applicableType);
      }
      var extendedConstructorArgs = constructorArgs.ToList();
      extendedConstructorArgs.Add(invoker);
      var instance = Activator.CreateInstance(dynamicType, extendedConstructorArgs.ToArray());
      return instance;
    }

    internal static Type BuildDynamicType<TApplicable>() {
      return BuildDynamicType(typeof(TApplicable));
    }
    internal static Type BuildDynamicType<TApplicable>(ModuleBuilder moduleBuilder) {
      return BuildDynamicType(typeof(TApplicable), moduleBuilder);
    }

    private static Dictionary<Type,Type> _ProxyTypesPerContract = new Dictionary<Type, Type>();


    internal static Type BuildDynamicType(Type applicableType) {

      lock (_ProxyTypesPerContract) {
        if (_ProxyTypesPerContract.TryGetValue(applicableType, out Type generatdProxyType)) {
          return generatdProxyType;
        }
      }

      ModuleBuilder moduleBuilder = CreateAssemblyModuleBuilder("UJMW.InMemoryClients." + applicableType.Name);

      return BuildDynamicType(applicableType, moduleBuilder);
    }

    internal static ModuleBuilder CreateAssemblyModuleBuilder(string assemblyName) {
      var an = new AssemblyName(assemblyName);
#if NET46
      AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndSave);
#endif
#if NET5
     AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
#endif
      return  assemblyBuilder.DefineDynamicModule(an.Name);
    }

    internal static Type BuildDynamicType(Type applicableType, ModuleBuilder moduleBuilder) {

      lock (_ProxyTypesPerContract) {
        if(_ProxyTypesPerContract.TryGetValue(applicableType, out Type generatdProxyType)) {
          return generatdProxyType;
        }
      }

      lock (moduleBuilder) {

        Type iDynamicProxyInvokerType = typeof(IAbstractWebcallInvoker);
        MethodInfo iDynamicProxyInvokerTypeInvokeMethod = iDynamicProxyInvokerType.GetMethod(nameof(IAbstractWebcallInvoker.InvokeWebCall));

        Type baseType = null;
        if ((applicableType.IsClass)) {
          baseType = applicableType;
        }

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

        lock (_ProxyTypesPerContract) {
          _ProxyTypesPerContract[applicableType] = dynamicType;
        }

        return dynamicType;
      }
    }

    private static void CollectAllMethodsForType(Type t, List<MethodInfo> target) {
      foreach (MethodInfo mi in t.GetMethods()) {
        if (target.Contains(mi)) continue;
        target.Add(mi);
      }
      if(t.BaseType != null) {
        CollectAllMethodsForType(t.BaseType, target);
      }
      foreach (Type intf in t.GetInterfaces()) {
        CollectAllMethodsForType(intf, target);
      }
    }

    //https://www.aspnetmonsters.com/2016/08/2016-08-27-httpclientwrong/
    private static Dictionary<string, HttpClient> _HttpClientsPerCustomizing = new Dictionary<string, HttpClient>();


    private static HttpClient GetHttpClient(string[] customizingFlags) {
      if(customizingFlags == null) {
        customizingFlags = new string[0];
      }
      lock (_HttpClientsPerCustomizing) {
        HttpClient client;
        string flagsDiscriminator = string.Join("|", customizingFlags);
        if (_HttpClientsPerCustomizing.TryGetValue(flagsDiscriminator, out client)) {
          return client;
        }
        if (UjmwClientConfiguration.HttpClientFactory != null) {
          client = UjmwClientConfiguration.HttpClientFactory.Invoke(customizingFlags);
        }
        else {
          client = new HttpClient();
          client.Timeout = TimeSpan.FromMinutes(10);
        }
        _HttpClientsPerCustomizing.Add(flagsDiscriminator, client);
        return client;
      }
    }

    public static void DisposeHttpClient() {
      lock (_HttpClientsPerCustomizing) {
        foreach (HttpClient client in _HttpClientsPerCustomizing.Values) {
          client.Dispose();
        }
        _HttpClientsPerCustomizing.Clear();
      }
    }

  }

}
