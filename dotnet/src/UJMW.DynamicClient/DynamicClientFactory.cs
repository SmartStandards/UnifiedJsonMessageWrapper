using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
    private IAbstractCallInvoker _Invoker;

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
      return CreateInstance(applicableType, invoker, string.Empty);
    }

    public static TApplicable CreateInstance<TApplicable>(IHttpPostExecutor httpPostExecutor, Func<string> urlGetter) {
      UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(typeof(TApplicable), httpPostExecutor, urlGetter);
      return CreateInstance<TApplicable>(invoker);
    }

    public static object CreateInstance(Type applicableType, IHttpPostExecutor httpPostExecutor, Func<string> urlGetter) {
      UjmwWebCallInvoker invoker = new UjmwWebCallInvoker(applicableType, httpPostExecutor, urlGetter);
      return CreateInstance(applicableType, invoker, string.Empty);
    }

    #endregion

    public static TApplicable CreateInstance<TApplicable>(IAbstractCallInvoker invoker, params object[] constructorArgs) {
      return (TApplicable)CreateInstance(typeof(TApplicable), invoker, string.Empty, constructorArgs);
    }

    private static ModuleBuilder _CombinedBuilder = null;
    private static object CreateInstance(Type applicableType, IAbstractCallInvoker invoker, string subClientPath, params object[] constructorArgs) {
     
      if (subClientPath == null || subClientPath == "/") {
        subClientPath = string.Empty;
      }

      if (subClientPath.EndsWith ("/") || subClientPath.StartsWith("/")) {
        throw new ArgumentException("'subClientPath' must not start ort end with '/'");
      }
      
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
      extendedConstructorArgs.Add(subClientPath);
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

        Type iDynamicProxyInvokerType = typeof(IAbstractCallInvoker);
        MethodInfo iDynamicProxyInvokerTypeInvokeMethod = iDynamicProxyInvokerType.GetMethod(nameof(IAbstractCallInvoker.InvokeCall));

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
        typeBuilder.AddInterfaceImplementation(typeof(IUjmwClient));

        // ##### FIELD DEFINITIONs #####

        var fieldBuilderDynamicProxyInvoker = typeBuilder.DefineField("_DynamicProxyInvoker", iDynamicProxyInvokerType, FieldAttributes.Private);
        var fieldSubClientPath = typeBuilder.DefineField("_SubClientPath", typeof(string), FieldAttributes.Private);

        // ##### CONSTRUCTOR DEFINITIONs #####

        if (baseType is object) {

          // create a proxy for each constructor in the base class
          foreach (var constructorOnBase in baseType.GetConstructors()) {

            var constructorArgs = new List<Type>();
            foreach (var p in constructorOnBase.GetParameters())
              constructorArgs.Add(p.ParameterType);

            //add our own additional constructors
            constructorArgs.Add(typeof(IAbstractCallInvoker));
            constructorArgs.Add(typeof(string));

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
              byte lastArgIndex = (byte)constructorArgs.Count;
              // TODO: prüfen ob valutype!!!!! <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
              // .Emit(OpCodes.Ldarg, argIndex) 'load the last Argument (Constructor-Param: IDynamicProxyInvoker)
              withBlock.Emit(OpCodes.Ldarg, 0); // load Argument(0) (which is a pointer to the instance of our class)  
              withBlock.Emit(OpCodes.Ldarg_S, lastArgIndex - 1); // load the last Argument (Constructor-Param: IDynamicProxyInvoker)
              withBlock.Emit(OpCodes.Stfld, fieldBuilderDynamicProxyInvoker); // CODE: _DynamicProxyInvoker = dynamicProxyInvoker
              withBlock.Emit(OpCodes.Nop); // ------------------
              withBlock.Emit(OpCodes.Ldarg, 0); // load Argument(0) (which is a pointer to the instance of our class)
              withBlock.Emit(OpCodes.Ldarg, lastArgIndex); // load the Argument (Constructor-Param: subClientPath)
              withBlock.Emit(OpCodes.Stfld, fieldSubClientPath); // CODE: _SubClientPath = subClientPath
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
            new[] { typeof(IAbstractCallInvoker), typeof(string) }
          );

          // CODE: Public Sub New(dynamicProxyInvoker As IDynamicProxyInvoker)

          {
            var constructorIlGen = constructorBuilder.GetILGenerator();
            constructorIlGen.Emit(OpCodes.Nop); // ------------------
            constructorIlGen.Emit(OpCodes.Ldarg, 0); // load Argument(0) (which is a pointer to the instance of our class)
            constructorIlGen.Emit(OpCodes.Ldarg, 1); // load the Argument (Constructor-Param: IDynamicProxyInvoker)
            constructorIlGen.Emit(OpCodes.Stfld, fieldBuilderDynamicProxyInvoker); // CODE: _DynamicProxyInvoker = dynamicProxyInvoker
            constructorIlGen.Emit(OpCodes.Nop); // ------------------
            constructorIlGen.Emit(OpCodes.Ldarg, 0); // load Argument(0) (which is a pointer to the instance of our class)
            constructorIlGen.Emit(OpCodes.Ldarg, 2); // load the Argument (Constructor-Param: subClientPath)
            constructorIlGen.Emit(OpCodes.Stfld, fieldSubClientPath); // CODE: _SubClientPath = subClientPath
            constructorIlGen.Emit(OpCodes.Ret); // ------------------
          }
        }

        // ##### METHOD DEFINITIONs #####

        #region " Impleml. of IUjmwClient "

        //Function GetInvoker() As IDynamicProxyInvoker -> IL implementierung, die einfach nur das feld returnt:

        MethodInfo getInvokerMethod = typeof(IUjmwClient).GetMethod(nameof(IUjmwClient.GetInvoker));

        MethodBuilder getInvokerMethodBuilder = typeBuilder.DefineMethod(
          getInvokerMethod.Name, MethodAttributes.Public | MethodAttributes.ReuseSlot | MethodAttributes.HideBySig | MethodAttributes.Virtual,
          getInvokerMethod.ReturnType, Array.Empty<Type>()
        );
        ILGenerator getInvokerMethodIlGen = getInvokerMethodBuilder.GetILGenerator();

        getInvokerMethodIlGen.Emit(OpCodes.Ldarg_0);
        getInvokerMethodIlGen.Emit(OpCodes.Ldfld, fieldBuilderDynamicProxyInvoker);
        getInvokerMethodIlGen.Emit(OpCodes.Ret);

        typeBuilder.DefineMethodOverride(getInvokerMethodBuilder, getInvokerMethod);

        //Function GetSubClientPath() As string -> IL implementierung, die einfach nur das feld returnt:

        MethodInfo getSubClientPathMethod = typeof(IUjmwClient).GetMethod(nameof(IUjmwClient.GetSubClientPath));

        MethodBuilder getSubClientPathMethodBuilder = typeBuilder.DefineMethod(
          getSubClientPathMethod.Name, MethodAttributes.Public | MethodAttributes.ReuseSlot | MethodAttributes.HideBySig | MethodAttributes.Virtual,
          getSubClientPathMethod.ReturnType, Array.Empty<Type>()
        );
        ILGenerator getSubClientPathIlGen = getSubClientPathMethodBuilder.GetILGenerator();

        getSubClientPathIlGen.Emit(OpCodes.Ldarg_0);
        getSubClientPathIlGen.Emit(OpCodes.Ldfld, fieldSubClientPath);
        getSubClientPathIlGen.Emit(OpCodes.Ret);

        typeBuilder.DefineMethodOverride(getSubClientPathMethodBuilder, getSubClientPathMethod);


        //Function GetContract() As Type -> IL implementierung, die einfach nur unseren applicableType in statischer form returnt:

        MethodInfo getContractMethod = typeof(IUjmwClient).GetMethod(nameof(IUjmwClient.GetContract));

        MethodBuilder getContractMethodBuilder = typeBuilder.DefineMethod(
          getContractMethod.Name, MethodAttributes.Public | MethodAttributes.ReuseSlot | MethodAttributes.HideBySig | MethodAttributes.Virtual,
          getContractMethod.ReturnType, Array.Empty<Type>()
        );
        ILGenerator getContractMethodIlGen = getContractMethodBuilder.GetILGenerator();

        getContractMethodIlGen.Emit(OpCodes.Ldtoken, applicableType);
        getContractMethodIlGen.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle"));
        getContractMethodIlGen.Emit(OpCodes.Ret);

        typeBuilder.DefineMethodOverride(getContractMethodBuilder, getContractMethod);

        #endregion

        #region " METHODS "

        List<MethodInfo> allMethods = new List<MethodInfo>();
        CollectAllMethodsForType(applicableType, allMethods);

        foreach (MethodInfo mi in allMethods) {
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

                methodIlGen.Emit(OpCodes.Ldarg_0);
                methodIlGen.Emit(OpCodes.Ldfld, fieldSubClientPath);
                string uniqueMethodNameOnTransportLayer = mi.GetNameOrOverride(false);
                methodIlGen.Emit(OpCodes.Ldstr, uniqueMethodNameOnTransportLayer); // < methodenname als string auf den stack holen
                MethodInfo stringConcatMethod = typeof(string).GetMethod( nameof(string.Concat), new Type[] { typeof(string), typeof(string) });
                methodIlGen.Emit(OpCodes.Call, stringConcatMethod);
                //jetzt liegt direkt nach dem _DynamicProxyInvoker von oben nurnoch ein weitrer zusammengesetzer string
                //_SubClientPath + uniqueMethodNameOnTransportLayer auf dem stack!

                #region " Riesen Aufstand um die Methodinfo hier sauber als 2. argument übergeben zu können... "

                //erstmal brauchen wir 'MethodBase.GetMethodFromHandle' als hilfmethode 
                MethodInfo getMethodFromHandleMethod = typeof(MethodBase).GetMethod(
                  nameof(MethodBase.GetMethodFromHandle),
                  new Type[] { typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle) }
                );

                //dann müssen wir über den typ gehen (da wri bei der ausführung des emitteten codes da erstmal ran müssen)
                Type declaringType = mi.DeclaringType;

                //AUFRUF VON: MethodBase.GetMethodFromHandle(handle,type)
                methodIlGen.Emit(OpCodes.Ldtoken, mi);
                methodIlGen.Emit(OpCodes.Ldtoken, declaringType);
                methodIlGen.Emit(OpCodes.Call, getMethodFromHandleMethod);

                //ergebnis (jetzt akkut auf dem stack liegend) casten
                methodIlGen.Emit(OpCodes.Castclass, typeof(MethodInfo));

                //-> danach liegt auf dem stack (hoffentlich am richtigen ort - nämlich arg2) die MethodInfo

                #endregion

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
                  //bool paramIsOut = paramEvalIsOut[paramIndex];
                  bool paramIsRefOrOut = paramEvalIsByRef[paramIndex];
                  Type realParamType = realParamTypes[paramIndex];
                  if (paramIsRefOrOut) {

                    methodIlGen.Emit(OpCodes.Ldarg, paramIndex + 1); //argument-handle holen (als zuweisungs-ziel)

                    methodIlGen.Emit(OpCodes.Ldloc, argumentRedirectionArray); // transport-array laden
                    methodIlGen.Emit(OpCodes.Ldc_I4_S, (byte)paramIndex); // arrayindex als integer (zwecks feld-addressierung) erzeugen
                  
                    methodIlGen.Emit(OpCodes.Ldelem_Ref); //array-inhalt (object-handle) auf den stack holen

                    //VOR FIX <<<<<<<<<<<<<<<<

                    //if (paramIsValueType) {
                    //  methodIlGen.Emit(OpCodes.Unbox_Any, realParamType); //array-inhalt auf den stack holen
                    //}

                    //methodIlGen.Emit(OpCodes.Stind_Ref); //wert in die adresse des arguments schreiben

                    //NACH FIX:

                    if (paramIsValueType) {

                      // Convert object -> actual struct value
                      methodIlGen.Emit(OpCodes.Unbox_Any, realParamType);

                      // Store the value into the target address (T&)
                      methodIlGen.Emit(OpCodes.Stobj, realParamType);
                    }
                    else {

                      // Ensure reference type compatibility
                      methodIlGen.Emit(OpCodes.Castclass, realParamType);

                      // Store object reference into the target address (T&)
                      methodIlGen.Emit(OpCodes.Stind_Ref);
                    }

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

        #endregion

        #region " PROPERTIES "

        List<PropertyInfo> allProperties = new List<PropertyInfo>();
        CollectAllPropertiesForType(applicableType, allProperties);

        foreach (PropertyInfo propertyInfo in allProperties) {

          MethodInfo getMethod = propertyInfo.GetGetMethod();
          MethodInfo setMethod = propertyInfo.GetSetMethod();

          bool propertyIsReadOnly = getMethod != null && setMethod == null;
          bool propertyTypeIsPrimitive = (
            propertyInfo.PropertyType.IsPrimitive || propertyInfo.PropertyType.IsEnum ||
            propertyInfo.PropertyType == typeof(string) || propertyInfo.PropertyType == typeof(decimal) ||
            propertyInfo.PropertyType == typeof(DateTime) || propertyInfo.PropertyType == typeof(Guid)
          );

          if (!propertyIsReadOnly || propertyTypeIsPrimitive) {
            ImplementPropertyWithNotImplementedException(typeBuilder, propertyInfo, getMethod, setMethod);
            continue;
          }

          FieldBuilder propertySingletonField = typeBuilder.DefineField(
            $"_{propertyInfo.Name}Instance",
            typeof(object),
            FieldAttributes.Private
          );

          MethodBuilder getterBuilder = typeBuilder.DefineMethod(
            getMethod.Name,
            MethodAttributes.Public |
            MethodAttributes.ReuseSlot |
            MethodAttributes.HideBySig |
            MethodAttributes.SpecialName |
            MethodAttributes.Virtual,
            propertyInfo.PropertyType,
            Array.Empty<Type>()
          );

          ILGenerator getterIlGen = getterBuilder.GetILGenerator();

          LocalBuilder singletonLocal = getterIlGen.DeclareLocal(typeof(object));
          Label singletonAlreadyCreatedLabel = getterIlGen.DefineLabel();

          getterIlGen.Emit(OpCodes.Ldarg_0);
          getterIlGen.Emit(OpCodes.Ldfld, propertySingletonField);
          getterIlGen.Emit(OpCodes.Dup);
          getterIlGen.Emit(OpCodes.Brtrue_S, singletonAlreadyCreatedLabel);
          getterIlGen.Emit(OpCodes.Pop);

          getterIlGen.Emit(OpCodes.Ldtoken, propertyInfo.PropertyType);
          getterIlGen.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle)));
          getterIlGen.Emit(OpCodes.Ldstr, propertyInfo.Name);
          getterIlGen.Emit(OpCodes.Ldarg_0);

          MethodInfo createReadOnlyPropertySingletonMethod = typeof(DynamicClientFactory).GetMethod(
            nameof(DynamicClientFactory.CreateOnDemandInstanceForVirtuallyImplementedProperty),
            new Type[] { typeof(Type), typeof(string), typeof(IUjmwClient) }
          );

          getterIlGen.Emit(OpCodes.Call, createReadOnlyPropertySingletonMethod);
          getterIlGen.Emit(OpCodes.Stloc, singletonLocal);

          getterIlGen.Emit(OpCodes.Ldarg_0);
          getterIlGen.Emit(OpCodes.Ldloc, singletonLocal);
          getterIlGen.Emit(OpCodes.Stfld, propertySingletonField);

          getterIlGen.Emit(OpCodes.Ldloc, singletonLocal);

          getterIlGen.MarkLabel(singletonAlreadyCreatedLabel);

          if (propertyInfo.PropertyType.IsValueType) {
            getterIlGen.Emit(OpCodes.Unbox_Any, propertyInfo.PropertyType);
          }
          else {
            getterIlGen.Emit(OpCodes.Castclass, propertyInfo.PropertyType);
          }

          getterIlGen.Emit(OpCodes.Ret);

          typeBuilder.DefineMethodOverride(getterBuilder, getMethod);
        }
        
        #endregion

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

    private static void CollectAllPropertiesForType(Type t, List<PropertyInfo> target) {
      foreach (PropertyInfo pi in t.GetProperties()) {
        if (target.Contains(pi)) continue;
        target.Add(pi);
      }
      if (t.BaseType != null) {
        CollectAllPropertiesForType(t.BaseType, target);
      }
      foreach (Type intf in t.GetInterfaces()) {
        CollectAllPropertiesForType(intf, target);
      }
    }

    /// <summary>
    /// Implements unsupported property accessors with a hard NotImplementedException.
    /// </summary>
    private static void ImplementPropertyWithNotImplementedException(
      TypeBuilder typeBuilder,
      PropertyInfo propertyInfo,
      MethodInfo getMethod,
      MethodInfo setMethod
    ) {
      if (typeBuilder == null) {
        throw new ArgumentNullException(nameof(typeBuilder));
      }

      if (propertyInfo == null) {
        throw new ArgumentNullException(nameof(propertyInfo));
      }

      ConstructorInfo notImplementedExceptionConstructor = typeof(NotImplementedException).GetConstructor(Type.EmptyTypes);

      if (notImplementedExceptionConstructor == null) {
        throw new InvalidOperationException("The default constructor of NotImplementedException was not found.");
      }

      if (getMethod != null) {
        MethodBuilder getterBuilder = typeBuilder.DefineMethod(
          getMethod.Name,
          MethodAttributes.Public |
          MethodAttributes.ReuseSlot |
          MethodAttributes.HideBySig |
          MethodAttributes.SpecialName |
          MethodAttributes.Virtual,
          propertyInfo.PropertyType,
          Array.Empty<Type>()
        );

        ILGenerator getterIlGen = getterBuilder.GetILGenerator();

        getterIlGen.Emit(OpCodes.Newobj, notImplementedExceptionConstructor);
        getterIlGen.Emit(OpCodes.Throw);

        typeBuilder.DefineMethodOverride(getterBuilder, getMethod);
      }

      if (setMethod != null) {
        MethodBuilder setterBuilder = typeBuilder.DefineMethod(
          setMethod.Name,
          MethodAttributes.Public |
          MethodAttributes.ReuseSlot |
          MethodAttributes.HideBySig |
          MethodAttributes.SpecialName |
          MethodAttributes.Virtual,
          typeof(void),
          new Type[] { propertyInfo.PropertyType }
        );

        setterBuilder.DefineParameter(1, ParameterAttributes.In, "value");

        ILGenerator setterIlGen = setterBuilder.GetILGenerator();

        setterIlGen.Emit(OpCodes.Newobj, notImplementedExceptionConstructor);
        setterIlGen.Emit(OpCodes.Throw);

        typeBuilder.DefineMethodOverride(setterBuilder, setMethod);
      }
    }

    //ACHTUNG WEAK REFERENCE -> wird aus emit aufgerufen
    public static object CreateOnDemandInstanceForVirtuallyImplementedProperty(
      Type propertyType,
      string propertyName,
      IUjmwClient dynamicClientInstance
    ) {

      IAbstractCallInvoker parentInvoker = dynamicClientInstance.GetInvoker();

      string subClientPath = dynamicClientInstance.GetSubClientPath();
      if (string.IsNullOrWhiteSpace(subClientPath)){
        subClientPath = propertyName;
      }
      else {
        subClientPath = subClientPath + "/" + propertyName;
      }
      
      return CreateInstance(propertyType, parentInvoker, subClientPath);
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

    #region " Version-Checks and Info-Endpoint "

    public static string BuildEndpointQualifyingName(Type contractType) {
      return contractType.BuildUjmwEndpointQualifyingName();
    }

    public static string BuildEndpointQualifyingName<TContract>() {
      return BuildEndpointQualifyingName(typeof(TContract));
    }

    public static string GetEndpointQualifyingNameRequriedByClient(object dynmicClientInstance) {
      IUjmwClient ujmwClient = (dynmicClientInstance as IUjmwClient);
      if(ujmwClient == null) return null;   
      Type contractType = ujmwClient.GetContract();
      return BuildEndpointQualifyingName(contractType);
    }

    public static Version ExtractVersionFromEndpointQualifyingName(string endpointQualifyingName) {
      if (!endpointQualifyingName.StartsWith("UJMW")) {
        throw new ArgumentException($"'{endpointQualifyingName}' is not an UJMW-Endpoint!");
      }
      int idx = endpointQualifyingName.IndexOf("/");
      if(idx < 0 ) {
        return new Version(0,0,0);
      }
      return Version.Parse(endpointQualifyingName.Substring(idx+1));
    }

    public static string GetEndpointQualifyingNameWithoutVersion(string endpointQualifyingName) {
      int idxL = endpointQualifyingName.IndexOf("/");
      if (idxL < 0) idxL = endpointQualifyingName.Length;
      return endpointQualifyingName.Substring(0, idxL); 
    }

    public static bool MatchEndpointQualifyingNamesWithoutVersion(string leftEndpointQualifyingName, string rightEndpointQualifyingName) {

      string leftPartToCompare = GetEndpointQualifyingNameWithoutVersion(leftEndpointQualifyingName);
      string rightPartToCompare = GetEndpointQualifyingNameWithoutVersion(rightEndpointQualifyingName);

      return (leftPartToCompare == rightPartToCompare);
    }

    public static bool CheckVersionCompatibilityOnServerSide(object dynamicClientInstance, Version minReqiredVersion = null, bool allowHigherMajor = false ) {      
     
      if(TryResolveContractVersionOnServerSide(dynamicClientInstance, out Version versionOnServerSide, out string[] knownMethodNames)) {

        if(minReqiredVersion == null) {
          string eqn = GetEndpointQualifyingNameRequriedByClient(dynamicClientInstance);
          minReqiredVersion = ExtractVersionFromEndpointQualifyingName(eqn);
        }

        if (versionOnServerSide.Major < minReqiredVersion.Major) {
          return false;
        }
        else if (versionOnServerSide.Major > minReqiredVersion.Major) {
          return allowHigherMajor;
        }

        if (versionOnServerSide.Minor < minReqiredVersion.Minor) {
          return false;
        }
        else if (versionOnServerSide.Minor > minReqiredVersion.Minor) {
          return true;
        }

        if (versionOnServerSide.Build < minReqiredVersion.Build) {
          return false;
        }
        else if (versionOnServerSide.Build > minReqiredVersion.Build) {
          return true;
        }

        return true;
      }
      return false;
    }

    public static bool CheckVersionCompatibilityOnServerSide(Type contractType, string url, Version minReqiredVersion = null, bool allowHigherMajor = false) {

      if (TryResolveContractVersionOnServerSide(contractType, url, out Version versionOnServerSide, out string[] knownMethodNames)) {

        if (minReqiredVersion == null) {
          string eqn = BuildEndpointQualifyingName(contractType);
          minReqiredVersion = ExtractVersionFromEndpointQualifyingName(eqn);
        }

        if (versionOnServerSide.Major < minReqiredVersion.Major) {
          return false;
        }
        else if (versionOnServerSide.Major > minReqiredVersion.Major) {
          return allowHigherMajor;
        }

        if (versionOnServerSide.Minor < minReqiredVersion.Minor) {
          return false;
        }
        else if (versionOnServerSide.Minor > minReqiredVersion.Minor) {
          return true;
        }

        if (versionOnServerSide.Build < minReqiredVersion.Build) {
          return false;
        }
        else if (versionOnServerSide.Build > minReqiredVersion.Build) {
          return true;
        }

        return true;
      }
      return false;

    }

    public static bool CheckVersionCompatibilityOnServerSide<TContract>(string url, Version minReqiredVersion = null, bool allowHigherMajor = false) {
      return CheckVersionCompatibilityOnServerSide(typeof(TContract), url, minReqiredVersion, allowHigherMajor);
    }

    public static bool TryResolveContractVersionOnServerSide(object dynamicClientInstance, out Version versionOnServerSide, out string[] knownMethodNames) {

      IUjmwClient ujmwClient = (dynamicClientInstance as IUjmwClient);

      if (ujmwClient == null) {
        versionOnServerSide = null;
        knownMethodNames = null;
        return false;
      }

      try {

        IAbstractCallInvoker invoker = ujmwClient.GetInvoker();
        Type contractType = ujmwClient.GetContract();

        string fullEqnToSearch = BuildEndpointQualifyingName(contractType);
        string nameOnlyToSearch = GetEndpointQualifyingNameWithoutVersion(fullEqnToSearch);

        string rawInfoResponse = invoker.InvokeCall(null, null, new object[0], new string[0], null)?.ToString();

        if (!string.IsNullOrWhiteSpace(rawInfoResponse)) {
          JObject json = JObject.Parse(rawInfoResponse);
          JArray serviceEndpoints = json["ServiceEndpoints"] as JArray;
          if (serviceEndpoints != null) {
            foreach (JObject endpoint in serviceEndpoints) {
              string eqn = endpoint["EndpointQualifyingName"]?.ToString();
              if( GetEndpointQualifyingNameWithoutVersion(eqn) == nameOnlyToSearch) {
                versionOnServerSide = ExtractVersionFromEndpointQualifyingName(eqn);
                JArray knownMethods = endpoint["UJMW.KnownMethods"] as JArray;
                knownMethodNames = knownMethods?.Select(m => m.ToString()).ToArray();
                return true;
              }
            }
          }
        }

      }
      catch {
      }

      versionOnServerSide = null;
      knownMethodNames = null;
      return false;
    }

    public static bool TryResolveContractVersionOnServerSide(Type contractType, string url, out Version versionOnServerSide, out string[] knownMethodNames) {
      try {
        using (HttpClient httpClient = UjmwClientConfiguration.HttpClientFactory.Invoke()) {

          IAbstractCallInvoker invoker = new UjmwWebCallInvoker(
            contractType,
            new WebClientBasedHttpPostExecutor(httpClient, null),
            () => url
          );

          string fullEqnToSearch = BuildEndpointQualifyingName(contractType);
          string nameOnlyToSearch = GetEndpointQualifyingNameWithoutVersion(fullEqnToSearch);

          string rawInfoResponse = invoker.InvokeCall(null, null, new object[0], new string[0], null)?.ToString();

          if (!string.IsNullOrWhiteSpace(rawInfoResponse)) {
            JObject json = JObject.Parse(rawInfoResponse);
            JArray serviceEndpoints = json["ServiceEndpoints"] as JArray;
            if (serviceEndpoints != null) {
              foreach (JObject endpoint in serviceEndpoints) {
                string eqn = endpoint["EndpointQualifyingName"]?.ToString();
                if (GetEndpointQualifyingNameWithoutVersion(eqn) == nameOnlyToSearch) {
                  versionOnServerSide = ExtractVersionFromEndpointQualifyingName(eqn);
                  JArray knownMethods = endpoint["UJMW.KnownMethods"] as JArray;
                  knownMethodNames = knownMethods?.Select(m => m.ToString()).ToArray();
                  return true;
                }
              }
            }
          }

        }
      }
      catch {
      }

      versionOnServerSide = null;
      knownMethodNames = null;
      return false;
    }

    public static bool TryResolveContractVersionOnServerSide<TContract>(string url, out Version versionOnServerSide, out string[] knownMethodNames) {
      return TryResolveContractVersionOnServerSide(typeof(TContract), url, out versionOnServerSide, out knownMethodNames);
    }

    #endregion

  }

}
