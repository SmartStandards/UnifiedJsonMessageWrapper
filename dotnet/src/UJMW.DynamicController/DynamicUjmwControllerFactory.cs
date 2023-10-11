using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace System.Web.UJMW {

  //TODO: es fehlen noch ein paar Features zum MVP:
  // - dynamisch via emit generiertes RouteAttribute am controller damit pfad reingegeben werden kann
  // - mapping von exceptions in eine "fault"-property am response dto
  // - konfiguriertbarkeit, ob in der fault-property richtige exceptiondetails drin stehen
  // - logger injecten lassen und nutzen

  public sealed partial class DynamicUjmwControllerFactory {

    private DynamicUjmwControllerFactory() { 
    }

    private const string UjmwReturnPropertyName = "return";
    private const string UjmwFaultPropertyName = "fault";
    private const string UjmwSideChannelPropertyName = "_";
    private const string UjmwResponseDtoNamespaceSuffix = "Dtos.";
    private const string UjmwResponseDtoSuffix = "Response";
    private const string UjmwRequestDtoSuffix = "Request";

    private static ConstructorInfo _HttpPostAttributeConstructor = typeof(HttpPostAttribute).GetConstructors().Where((c) => c.GetParameters().Count() == 1).Single();
    private static ConstructorInfo _ProducesAttributeConstructor = typeof(ProducesAttribute).GetConstructors().Where((c) => c.GetParameters().Count() == 1).Single();
    private static ConstructorInfo _ConsumesAttributeConstructor = typeof(ConsumesAttribute).GetConstructors().Where((c) => c.GetParameters().First().ParameterType == typeof(string)).Single();
    private static ConstructorInfo _FromBodyAttributeConstructor = typeof(FromBodyAttribute).GetConstructors().Where((c) => c.GetParameters().Count() == 0).Single();
    private static ConstructorInfo _RouteAttributeConstructor = typeof(RouteAttribute).GetConstructors().Where((c) => c.GetParameters().First().ParameterType == typeof(string)).Single();

    public static Type BuildDynamicControllerType(Type serviceType, DynamicUjmwControllerOptions options = null) {
      if(options == null) {
        options = new DynamicUjmwControllerOptions();
      }

      ConstructorInfo authAttributeConstructor = null;
      if (options.AuthAttribute != null) {
        authAttributeConstructor = options.AuthAttribute.GetConstructors().Where(
          (c) => c.GetParameters().Length == options.AuthAttributeConstructorParams.Length
        ).Single();
      }

      AssemblyBuilder assemblyBuilder = null;
      Type baseType = typeof(DynamicControllerBase<>).MakeGenericType(serviceType);

      MethodInfo invokeMethod = baseType.GetMethod("InvokeMethod", BindingFlags.Instance | BindingFlags.NonPublic);

      //##### ASSEMBLY & MODULE DEFINITION #####

      var assemblyName = new AssemblyName(serviceType.Name + ".DyamicControllers");

#if NET46
      assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndSave);
#else
      assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
#endif

      var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name);

      // ##### CLASS DEFINITION #####

      string svcName = serviceType.Name;
      if (serviceType.IsInterface && svcName.StartsWith("I") && char.IsUpper(svcName[1])) {
        svcName = svcName.Substring(1);
      }

      TypeBuilder typeBuilder = moduleBuilder.DefineType(
          svcName + "Controller",
          TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.AutoLayout,
          baseType
        );

      CustomAttributeBuilder RouteAttribBuilder = new CustomAttributeBuilder(
       _RouteAttributeConstructor, new object[] { options.ControllerRoute }
      );
      typeBuilder.SetCustomAttribute(RouteAttribBuilder);

      // ##### FIELD DEFINITIONs #####

      var fieldBuilderDynamicProxyInvoker = baseType.GetField("_Invoker", BindingFlags.Instance | BindingFlags.NonPublic);

      // ##### CONSTRUCTOR DEFINITIONs #####

      //HACK: no loop needed - can be simplified...
      foreach (var constructorOnBase in baseType.GetConstructors()) {

        var constructorBuilder = typeBuilder.DefineConstructor(
          MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
          CallingConventions.Standard,
          new[] { serviceType }
        );

        var constructorIL = constructorBuilder.GetILGenerator();
        //withBlock.Emit(OpCodes.Nop); // ------------------
        constructorIL.Emit(OpCodes.Ldarg, 0); // load Argument(0) (which is a pointer to the instance of our class)   

        //ACHTUNG: 'Ldarg_S', weil es kein valuetype ist, sonst 'Ldarg'
        constructorIL.Emit(OpCodes.Ldarg_S, (byte)1); // load the custonctor argument, which is the service type

        constructorIL.Emit(OpCodes.Call, constructorOnBase); // CODE: MyBase.New([...])
        constructorIL.Emit(OpCodes.Nop); // ------------------

        constructorIL.Emit(OpCodes.Nop);
        constructorIL.Emit(OpCodes.Ret); // ------------------
          
      }

      // ##### METHOD DEFINITIONs #####

      foreach (var serviceMethod in serviceType.GetMethods()) {
        var methodSignatureString = serviceMethod.ToString();
        var methodNameBlacklist = new[] { "ToString", "GetHashCode", "GetType", "Equals"};
        if (!serviceMethod.IsSpecialName && !methodNameBlacklist.Contains(serviceMethod.Name) && !serviceMethod.Name.EndsWith("Async")) {
          if (serviceMethod.IsPublic) {

            Type requestType = DynamicUjmwControllerFactory.GetOrCreateDto(
              serviceType, serviceMethod, moduleBuilder, false, options
            );

            Type responseType = DynamicUjmwControllerFactory.GetOrCreateDto(
              serviceType, serviceMethod, moduleBuilder, true, options
            );

            var methodBuilder = typeBuilder.DefineMethod(
              serviceMethod.Name,
              MethodAttributes.Public | MethodAttributes.ReuseSlot | MethodAttributes.HideBySig | MethodAttributes.Virtual,
              responseType,
              new Type[] { requestType }
            );

            CustomAttributeBuilder httpPostAttribBuilder = new CustomAttributeBuilder(
              _HttpPostAttributeConstructor, new object[] { serviceMethod.Name }
            );
            methodBuilder.SetCustomAttribute(httpPostAttribBuilder);

            if(authAttributeConstructor != null) {
              CustomAttributeBuilder authAttribBuilder = new CustomAttributeBuilder(
                authAttributeConstructor, options.AuthAttributeConstructorParams
              );
              methodBuilder.SetCustomAttribute(authAttribBuilder);
            }

            CustomAttributeBuilder ConsumesAttribBuilder = new CustomAttributeBuilder(
              _ConsumesAttributeConstructor, new object[] { "application/json" ,new string[] { } }
            );
            methodBuilder.SetCustomAttribute(ConsumesAttribBuilder);

            var paramBuilders = new ParameterBuilder[1];
            paramBuilders[0] = methodBuilder.DefineParameter(1, ParameterAttributes.In, "args");

            CustomAttributeBuilder FromBodyAttribBuilder = new CustomAttributeBuilder(
              _FromBodyAttributeConstructor, new object[] { }
            );
            paramBuilders[0].SetCustomAttribute(FromBodyAttribBuilder);

            {
              var methodIlGen = methodBuilder.GetILGenerator();

              // ##### LOCAL VARIABLE DEFINITIONs #####

              LocalBuilder responseDtoReturnField = methodIlGen.DeclareLocal(responseType);

              methodIlGen.Emit(OpCodes.Nop);

              methodIlGen.Emit(OpCodes.Ldarg_0); // < unsere klasseninstanz auf den stack
                    
              methodIlGen.Emit(OpCodes.Ldstr, serviceMethod.Name); // < methodenname als string auf den stack holen

              methodIlGen.Emit(OpCodes.Ldarg, 1);// zuzuweisendes methoden-argument auf den stack holen
                 
              // aufruf auf umgeleitete funktion absetzen
              methodIlGen.Emit(OpCodes.Callvirt, invokeMethod); // _DynamicProxyInvoker.InvokeMethod("Foo", args)
                                                                // jetzt liegt ein result auf dem stack...
       
              methodIlGen.Emit(OpCodes.Castclass, responseType); // reference-types müssen gecastet werden, weil der retval in "object" ist
              methodIlGen.Emit(OpCodes.Stloc, responseDtoReturnField); // < speichere es in 'returnValueBuffer'
            
              if (responseDtoReturnField is object) {
                methodIlGen.Emit(OpCodes.Ldloc, responseDtoReturnField);
              }

              methodIlGen.Emit(OpCodes.Ret);
            }

          }
        }

      }

      var dynamicType = typeBuilder.CreateType();
      return dynamicType;
    }

    private static Dictionary<string, Type> _DtoTypeCache = new Dictionary<string, Type>();

    private static Type GetOrCreateDto(Type serviceType, MethodInfo methodInfo, ModuleBuilder moduleBuilder, bool response, DynamicUjmwControllerOptions options) {
      string dtoTypeName = serviceType.FullName + UjmwResponseDtoNamespaceSuffix + methodInfo.Name ;
      if (response) {
        dtoTypeName = dtoTypeName + UjmwResponseDtoSuffix;
      }
      else {
        dtoTypeName = dtoTypeName + UjmwRequestDtoSuffix;
      }
      lock (_DtoTypeCache ) {
        if (_DtoTypeCache.ContainsKey(dtoTypeName)) {
          return _DtoTypeCache[dtoTypeName];
        }
        else {

          TypeBuilder typeBuilder = moduleBuilder.DefineType(
            dtoTypeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.AutoLayout
          );

          if ((!response && options.EnableRequestSidechannel) || (response && options.EnableResponseSidechannel)) {
            DynamicUjmwControllerFactory.BuildProp(typeBuilder, UjmwSideChannelPropertyName, typeof(Dictionary<string, string>));
          }

          var parameters = methodInfo.GetParameters();
          foreach (var param in parameters) {
            if ((response && param.IsOut) || (!response && !param.IsOut) || (param.ParameterType.IsByRef && !param.IsOut)) {
              if (param.ParameterType.IsByRef) {
                DynamicUjmwControllerFactory.BuildProp(typeBuilder, param.Name, param.ParameterType.GetElementType());
              }
              else {
                DynamicUjmwControllerFactory.BuildProp(typeBuilder, param.Name, param.ParameterType);
              }         
            }
          }

          if (response) {
            if (methodInfo.ReturnType != null && methodInfo.ReturnType != typeof(void)) {
              DynamicUjmwControllerFactory.BuildProp(typeBuilder, UjmwReturnPropertyName, methodInfo.ReturnType);
            }
            DynamicUjmwControllerFactory.BuildProp(typeBuilder, UjmwFaultPropertyName, typeof(string));
          }

          Type dtoType = typeBuilder.CreateType();
          _DtoTypeCache[dtoTypeName] = dtoType;
          return dtoType;
        }
      }
    }

    private static void BuildProp(TypeBuilder typeBuilder, string propName, Type propType) {

      FieldBuilder fieldBdr = typeBuilder.DefineField("_" + propName, propType, FieldAttributes.Private);

      var propBdr = typeBuilder.DefineProperty(propName, PropertyAttributes.HasDefault, propType, null);

      MethodAttributes getSetAttr = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

      MethodBuilder getterPropMthdBldr = typeBuilder.DefineMethod("get_" + propName, getSetAttr, propType, Type.EmptyTypes);
      ILGenerator custNameGetIL = getterPropMthdBldr.GetILGenerator();
      custNameGetIL.Emit(OpCodes.Ldarg_0);
      custNameGetIL.Emit(OpCodes.Ldfld, fieldBdr);
      custNameGetIL.Emit(OpCodes.Ret);

      MethodBuilder setterPropMthdBldr = typeBuilder.DefineMethod("set_" + propName, getSetAttr, null, new Type[] { propType });
      ILGenerator custNameSetIL = setterPropMthdBldr.GetILGenerator();
      custNameSetIL.Emit(OpCodes.Ldarg_0);
      custNameSetIL.Emit(OpCodes.Ldarg_1);
      custNameSetIL.Emit(OpCodes.Stfld, fieldBdr);
      custNameSetIL.Emit(OpCodes.Ret);

      propBdr.SetGetMethod(getterPropMthdBldr);
      propBdr.SetSetMethod(setterPropMthdBldr);

    }

  }

}