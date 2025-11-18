using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace System.Web.UJMW {

  //TODO: es fehlen noch ein paar Features zum MVP:
  // - konfiguriertbarkeit, ob in der fault-property richtige exceptiondetails drin stehen
  // - logger injecten lassen und nutzen
  // - name für controller angeben (getrennt von url) -> ist Titel im swagger
  // - code xml-doc für swagger

  public sealed partial class DynamicUjmwControllerFactory {

    private DynamicUjmwControllerFactory() { 
    }

    internal const string RenderInfoSiteMethodName = "RenderInfoSite";

    private const string UjmwReturnPropertyName = "return";
    private const string UjmwFaultPropertyName = "fault";
    private const string UjmwSideChannelPropertyName = "_";
    private const string UjmwResponseDtoSuffix = "Response";
    private const string UjmwRequestDtoSuffix = "Request";

    private static ConstructorInfo _HttpPostAttributeConstructor = typeof(HttpPostAttribute).GetConstructors().Where((c) => c.GetParameters().Count() == 1).Single();
    private static ConstructorInfo _HttpGetAttributeConstructor = typeof(HttpGetAttribute).GetConstructors().Where((c) => c.GetParameters().Count() == 0).Single();
    private static ConstructorInfo _ProducesAttributeConstructor = typeof(ProducesAttribute).GetConstructors().Where((c) => c.GetParameters().Count() == 2).Single();
    private static ConstructorInfo _ConsumesAttributeConstructor = typeof(ConsumesAttribute).GetConstructors().Where((c) => c.GetParameters().First().ParameterType == typeof(string)).Single();
    private static ConstructorInfo _FromBodyAttributeConstructor = typeof(FromBodyAttribute).GetConstructors().Where((c) => c.GetParameters().Count() == 0).Single();
    private static ConstructorInfo _RouteAttributeConstructor = typeof(RouteAttribute).GetConstructors().Where((c) => c.GetParameters().First().ParameterType == typeof(string)).Single();

    private static ConstructorInfo _ApiExplorerSettingsAttributeConstructor = typeof(ApiExplorerSettingsAttribute).GetConstructors().Single();
    private static PropertyInfo _ApiExplorerSettingsGroupNameProp = typeof(ApiExplorerSettingsAttribute).GetProperty(nameof(ApiExplorerSettingsAttribute.GroupName));

    private static ConstructorInfo _AuthHeaderInterceptorAttributeConstructor = typeof(AuthHeaderInterceptorAttribute).GetConstructors().Single();

    private static ConstructorInfo _TagsAttributeContructor = Type.GetType("Microsoft.AspNetCore.Http.TagsAttribute, Microsoft.AspNetCore.Http.Extensions", false)?.GetConstructors()?.FirstOrDefault();

    //optional
    private const string swashbuckle = "Swashbuckle.AspNetCore.Annotations";
    private static ConstructorInfo _SwaggerOperationAttributeConstructor = Type.GetType(swashbuckle + ".SwaggerOperationAttribute, " + swashbuckle, false)?.GetConstructors()?.FirstOrDefault();
    private static ConstructorInfo _SwaggerRequestBodyAttributeConstructor = Type.GetType(swashbuckle + ".SwaggerRequestBodyAttribute, " + swashbuckle, false)?.GetConstructors()?.FirstOrDefault();
    private static ConstructorInfo _SwaggerResponseAttributeConstructor = Type.GetType(swashbuckle + ".SwaggerResponseAttribute, " + swashbuckle, false)?.GetConstructors()?.FirstOrDefault();//Skip(1)?.
    private static ConstructorInfo _SwaggerSchemaAttributeConstructor = Type.GetType(swashbuckle + ".SwaggerSchemaAttribute, " + swashbuckle, false)?.GetConstructors()?.FirstOrDefault();
    
    public static Type BuildDynamicControllerType(Type serviceType, DynamicUjmwControllerOptions options = null, string apiGroupName = null) {
      return BuildDynamicControllerType(serviceType, options, out string dummyRoute, out string dummyTitle, apiGroupName);
    }

    public static Type BuildDynamicControllerType(Type serviceType, DynamicUjmwControllerOptions options, out string controllerRoute, out string controllerTitle, string apiGroupName = null) {
      ModuleBuilder moduleBuilder = CreateAssemblyModuleBuilder("UJMW.InMemoryControllers." + serviceType.Name);
      return BuildDynamicControllerType(serviceType, options, out controllerRoute, out controllerTitle, out string resolvedControllerName, moduleBuilder, serviceType, apiGroupName);
    }

    internal static ModuleBuilder CreateAssemblyModuleBuilder(string assemblyName) {
      var an = new AssemblyName(assemblyName);
#if NET46
      AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndSave);
#endif
#if NET5
      AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
#endif
      return assemblyBuilder.DefineDynamicModule(an.Name);
    }

    internal static Type BuildDynamicControllerType(
      Type serviceType, DynamicUjmwControllerOptions options,
      out string controllerRoute, out string controllerTitle, out string controllerName,
      ModuleBuilder moduleBuilder,
      Type rootServiceTypeRequiredByConstructor, string apiGroupName = null
    ) {

      if (options == null) {
        options = new DynamicUjmwControllerOptions();
        options.ApiGroupName = apiGroupName;
      }
      else if (string.IsNullOrEmpty(apiGroupName)) {
        apiGroupName = options.ApiGroupName;
      }

      ConstructorInfo authAttributeConstructor = null;
      if (options.AuthAttribute != null) {
        authAttributeConstructor = options.AuthAttribute.GetConstructors().Where(
          (c) => c.GetParameters().Length == options.AuthAttributeConstructorParams.Length
        ).Single();
      }

      Type baseType = typeof(DynamicControllerBase<>).MakeGenericType(serviceType);

      MethodInfo invokeMethod = baseType.GetMethod("InvokeMethod", BindingFlags.Instance | BindingFlags.NonPublic);
      MethodInfo renderInfoSite = baseType.GetMethod(RenderInfoSiteMethodName, BindingFlags.Instance | BindingFlags.NonPublic);

      ////////////// NAMING ///////////////////////////////////////////////////////////////////

      string originalTypeName = serviceType.Name;

      string[] genArgs = new string[] { };
      if (serviceType.IsGenericType) {
        originalTypeName = originalTypeName.Substring(0, originalTypeName.IndexOf('`'));
        genArgs = serviceType.GetGenericArguments().Select((t) => t.Name).ToArray();
      }
      if (serviceType.IsInterface && originalTypeName.StartsWith("I") && char.IsUpper(originalTypeName[1])) {
        originalTypeName = originalTypeName.Substring(1);
      }

      string legacyClassDiscriminator = options.ClassNameDiscriminator;

      // CONTROLLER

      string controllerNamePattern = options.ControllerNamePattern;
      if (string.IsNullOrEmpty(controllerNamePattern)) {
        controllerNamePattern = "[Controller]";
        if (!string.IsNullOrEmpty(legacyClassDiscriminator)) {
          //stange behaviour from the past:
          controllerNamePattern = $"[Controller]{legacyClassDiscriminator}";
        }
        else if(genArgs.Any()) {
          controllerNamePattern = $"[Controller]_{string.Join("_", genArgs)}";
        }
      }
      controllerName = controllerNamePattern.Replace("[Controller]", originalTypeName); 
      for (int i = 0; i < genArgs.Length; i++) {
        //new (better) syntax support, because < > is common for generic args and
        //wont collide with the asp.net core route-placehoder syntax { }
        controllerName = controllerName.Replace($"<{i}>", genArgs[i]);
        //backward compatibility
        controllerName = controllerName.Replace($"{{{i}}}", genArgs[i]);
      }

      // WRAPPERS

      string wrapperNamePattern = options.WrapperNamePattern;
      if (string.IsNullOrEmpty(wrapperNamePattern)) {
        wrapperNamePattern = "{Controller}[Method]";
        if (!string.IsNullOrEmpty(legacyClassDiscriminator)) {
          //stange behaviour from the past:
          wrapperNamePattern = wrapperNamePattern + legacyClassDiscriminator;
        }
      }
      else if (!wrapperNamePattern.Contains("[Method]")) {
        wrapperNamePattern = wrapperNamePattern + "_[Method]";
      }
      wrapperNamePattern = wrapperNamePattern.Replace("[Controller]", originalTypeName);
      wrapperNamePattern = wrapperNamePattern.Replace("{Controller}", controllerName);
      for (int i = 0; i < genArgs.Length; i++) {
        //new (better) syntax support, because < > is common for generic args and
        //wont collide with the asp.net core route-placehoder syntax { }
        wrapperNamePattern = wrapperNamePattern.Replace($"<{i}>", genArgs[i]);
        //backward compatibility
        wrapperNamePattern = wrapperNamePattern.Replace($"{{{i}}}", genArgs[i]);
      }

      // TITLE

      controllerTitle = options.ControllerTitle;  
      if (string.IsNullOrEmpty(controllerTitle)) {
        if (genArgs.Any()) {
          controllerTitle = $"{{Controller}} ({string.Join(", ", genArgs)})";
        }
        else {
          controllerTitle = "{Controller}";
        }
      }
      controllerTitle = controllerTitle.Replace("[Controller]", originalTypeName);
      controllerTitle = controllerTitle.Replace("{Controller}", controllerName);
      for (int i = 0; i < genArgs.Length; i++) {
        //new (better) syntax support, because < > is common for generic args and
        //wont collide with the asp.net core route-placehoder syntax { }
        controllerTitle = controllerTitle.Replace($"<{i}>", genArgs[i]);
        //backward compatibility
        controllerTitle = controllerTitle.Replace($"{{{i}}}", genArgs[i]);
      }

      // ROUTE

      controllerRoute = options.ControllerRoute;
      if (string.IsNullOrEmpty(controllerRoute)) {
        if (genArgs.Any()) {
          controllerRoute = $"[Controller]/{string.Join("-", genArgs)}";
        }
        else {
          controllerRoute = "[Controller]";
        }
      }
      controllerRoute = controllerRoute.Replace("[Controller]", originalTypeName);
      controllerRoute = controllerRoute.Replace("{Controller}", controllerName);
      for (int i = 0; i < genArgs.Length; i++) {
        //new (better) syntax support, because < > is common for generic args and
        //wont collide with the asp.net core route-placehoder syntax { }
        controllerRoute = controllerRoute.Replace($"<{i}>", genArgs[i]);
        //backward compatibility
        controllerRoute = controllerRoute.Replace($"{{{i}}}", genArgs[i]);
      }

      if(options.ContextualRouteSegmentArguments != null) {
        foreach(KeyValuePair<string, string> kvp in options.ContextualRouteSegmentArguments) {
          if(!controllerRoute.Contains("{" + kvp.Value + "}")) {
            throw new ArgumentException(
              $"There is a route-based ContextualArgument named '{kvp.Value}', which is registered for this controller " +
              $"but the ControllerRoute ('{controllerRoute}') does not contain a corresponding '{{...}}' placeholder!."
            );
          }
        }
      }

      ///////////////////////////////////////////////////////////////////////

      TypeBuilder typeBuilder = moduleBuilder.DefineType(
        controllerName + "Controller", //<< pattern by microsoft...
        TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.AutoLayout,
        baseType
      );

      CustomAttributeBuilder RouteAttribBuilder = new CustomAttributeBuilder(
       _RouteAttributeConstructor, new object[] { controllerRoute }
      );
      typeBuilder.SetCustomAttribute(RouteAttribBuilder);

      if (!String.IsNullOrWhiteSpace(apiGroupName)) {
        CustomAttributeBuilder apiExplorerSettingsAttribBuilder = new CustomAttributeBuilder(
         _ApiExplorerSettingsAttributeConstructor, new object[0],
         //sonderlocken-überladung zum setzen von names-arguments (=properties):
         new[] { _ApiExplorerSettingsGroupNameProp }, new object[] { apiGroupName }
        );
        typeBuilder.SetCustomAttribute(apiExplorerSettingsAttribBuilder);
      }   

      if (_TagsAttributeContructor != null) {
        CustomAttributeBuilder tagsAttribBuilder = new CustomAttributeBuilder(
         _TagsAttributeContructor, new object[] { new string[] { controllerTitle } }
        );
        typeBuilder.SetCustomAttribute(tagsAttribBuilder);
      }

      if (options.EnableAuthHeaderEvaluatorHook) {
        CustomAttributeBuilder authHeaderInterceptorAttributeBuilder = new CustomAttributeBuilder(
          _AuthHeaderInterceptorAttributeConstructor, new object[] { }
        );
        typeBuilder.SetCustomAttribute(authHeaderInterceptorAttributeBuilder);
      }

      // ##### FIELD DEFINITIONs #####

      var fieldBuilderDynamicProxyInvoker = baseType.GetField("_Invoker", BindingFlags.Instance | BindingFlags.NonPublic);

      // ##### CONSTRUCTOR DEFINITIONs #####

      //HACK: no loop needed - can be simplified...
      foreach (var constructorOnBase in baseType.GetConstructors()) {

        var constructorBuilder = typeBuilder.DefineConstructor(
          MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
          CallingConventions.Standard,
          new[] { rootServiceTypeRequiredByConstructor }
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

      var allMethods = new List<MethodInfo>();
      CollectAllMethodsForType(serviceType, allMethods);

      #region " Endpoint-Info-Site (via HTTP-GET) "
      if (options.EnableInfoSite) {

        var rootMethodBuilder = typeBuilder.DefineMethod(
            RenderInfoSiteMethodName,
            MethodAttributes.Public | MethodAttributes.ReuseSlot | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            typeof(string),
            new Type[] {}
         );

        CustomAttributeBuilder httpGetAttribBuilder = new CustomAttributeBuilder(
          _HttpGetAttributeConstructor, new object[] {}
        );
        rootMethodBuilder.SetCustomAttribute(httpGetAttribBuilder);


        CustomAttributeBuilder apiExplorerSettingsAttribBuilderForGet = new CustomAttributeBuilder(
          _ApiExplorerSettingsAttributeConstructor, new object[0],
          //sonderlocken-überladung zum setzen von names-arguments (=properties):
          new[] { _ApiExplorerSettingsGroupNameProp }, new object[] { "hidden" }
        );
        rootMethodBuilder.SetCustomAttribute(apiExplorerSettingsAttribBuilderForGet);


        CustomAttributeBuilder producesAttribBuilder = new CustomAttributeBuilder(
          _ProducesAttributeConstructor, new object[] { "text/html", Array.Empty<string>() }
        );
        rootMethodBuilder.SetCustomAttribute(producesAttribBuilder);
        {
          var rootMethodIlGen = rootMethodBuilder.GetILGenerator();
          rootMethodIlGen.Emit(OpCodes.Nop);
          rootMethodIlGen.Emit(OpCodes.Ldarg_0); // < unsere klasseninstanz auf den stack
          rootMethodIlGen.Emit(OpCodes.Callvirt, renderInfoSite); // _DynamicProxyInvoker.RenderInfoSite()
                                                                  // jetzt liegt ein result auf dem stack...
          rootMethodIlGen.Emit(OpCodes.Castclass, typeof(IActionResult)); // reference-types müssen gecastet werden, weil der retval in "object" ist
          rootMethodIlGen.Emit(OpCodes.Ret);
        }

      }
      #endregion

      foreach (var serviceMethod in allMethods) {
        var methodSignatureString = serviceMethod.ToString();
        var methodNameBlacklist = new[] { "ToString", "GetHashCode", "GetType", "Equals"};
        if (!serviceMethod.IsSpecialName && !methodNameBlacklist.Contains(serviceMethod.Name) && !serviceMethod.Name.EndsWith("Async")) {
          if (serviceMethod.IsPublic) {

            Type requestType = DynamicUjmwControllerFactory.GetOrCreateDto(
              serviceType, wrapperNamePattern, serviceMethod, moduleBuilder, false, options
            );

            Type responseType = DynamicUjmwControllerFactory.GetOrCreateDto(
              serviceType, wrapperNamePattern, serviceMethod, moduleBuilder, true, options
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

            if(_SwaggerOperationAttributeConstructor != null) {
              try {
                string sum = serviceMethod.GetDocumentation(true);
                if(string.IsNullOrWhiteSpace(sum)) { sum = ""; }
                else if (sum.Length>120) { sum = sum.Substring(0, 117) + "..."; };
                string doc = serviceMethod.GetDocumentation(false, true).Replace("Returns:", "**Returns:**\n");
                CustomAttributeBuilder swaggerOperationAttributeBuilder = new CustomAttributeBuilder(
                  _SwaggerOperationAttributeConstructor, new object[] { sum, doc }
                );
                methodBuilder.SetCustomAttribute(swaggerOperationAttributeBuilder);

                string returnInfos = "An Unified Json Message Wrapper, which contains the following fields:";
                if (options.EnableResponseSidechannel) {
                  returnInfos = returnInfos + "\n\nParam **'_'**: used to flow additional ambient data (see UJMW standard)";
                }
                returnInfos = returnInfos + serviceMethod.GetDocumentationForParams(true, false, true).Replace("Param '", "\n\nParam **").Replace("':", "**:");

                string retInfo = serviceMethod.GetDocumentationForReturn();
                if(!string.IsNullOrWhiteSpace(retInfo)) {
                  returnInfos = returnInfos + "\n\nParam **return**: " + retInfo;
                }
                
                CustomAttributeBuilder swaggerResponseAttributeBuilder = new CustomAttributeBuilder(
                  //_SwaggerResponseAttributeConstructor, new object[] { 200, returnInfos, null , new string[] {"application/json"} }
                  _SwaggerResponseAttributeConstructor, new object[] { 200, returnInfos, null }
                );
                methodBuilder.SetCustomAttribute(swaggerResponseAttributeBuilder);
              }
              catch ( Exception ex ) {
                //no problem - its just for the docu
              }
            }

            if (authAttributeConstructor != null) {
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

            if (_SwaggerRequestBodyAttributeConstructor != null) {
              try {
                string paramInfos = "An Unified Json Message Wrapper, which contains the following fields:";
                if (options.EnableRequestSidechannel) {
                  paramInfos = paramInfos + "\n\nParam **'_'**: used to flow additional ambient data (see UJMW standard)";
                }
                paramInfos = paramInfos + serviceMethod.GetDocumentationForParams().Replace("Param '", "\n\nParam **").Replace("':", "**:");
                CustomAttributeBuilder swaggerParamAttributeBuilder = new CustomAttributeBuilder(
                  _SwaggerRequestBodyAttributeConstructor, new object[] { paramInfos }
                );
                paramBuilders[0].SetCustomAttribute(swaggerParamAttributeBuilder);
              }
              catch (Exception ex) {
                //no problem - its just for the docu
              }
            }

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
      lock (_OptionsPerDynamicControllerType) {
        _OptionsPerDynamicControllerType[dynamicType] = options;
      }
      return dynamicType;
    }

    private static Dictionary<Type, DynamicUjmwControllerOptions> _OptionsPerDynamicControllerType = new Dictionary<Type, DynamicUjmwControllerOptions>();

    private static DynamicUjmwControllerOptions GetDynamicUjmwControllerOptions(Type controllerType) {
      lock (_OptionsPerDynamicControllerType) {
        return _OptionsPerDynamicControllerType[controllerType];
      }
    }

    private static Dictionary<string, Type> _DtoTypeCache = new Dictionary<string, Type>();

    /// <summary></summary>
    /// <param name="serviceType"></param>
    /// <param name="wrapperNamePattern">The only allowed placeholder at this time is '[Method]'</param>
    /// <param name="methodInfo"></param>
    /// <param name="moduleBuilder"></param>
    /// <param name="response"></param>
    /// <param name="options"></param>
    /// <returns></returns>
    private static Type GetOrCreateDto(
      Type serviceType, string wrapperNamePattern, MethodInfo methodInfo, ModuleBuilder moduleBuilder,
      bool response, DynamicUjmwControllerOptions options
    ) {
      
      string wrapperTypeFullName = serviceType.Namespace + ".MessageWrappers." + wrapperNamePattern.Replace("[Method]", methodInfo.Name);
      
      if (response) {
        wrapperTypeFullName = wrapperTypeFullName + UjmwResponseDtoSuffix;
      }
      else {
        wrapperTypeFullName = wrapperTypeFullName + UjmwRequestDtoSuffix;
      }

      lock (_DtoTypeCache ) {
        if (_DtoTypeCache.ContainsKey(wrapperTypeFullName)) {
          return _DtoTypeCache[wrapperTypeFullName];
        }
        else {

          TypeBuilder typeBuilder = moduleBuilder.DefineType(
            wrapperTypeFullName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.AutoLayout
          );

          if(_SwaggerSchemaAttributeConstructor != null) {
            CustomAttributeBuilder swaggerSchemaAttributeBuilder = new CustomAttributeBuilder(
              _SwaggerSchemaAttributeConstructor, new object[] { "Unified Json Message Wrapper" }
            );
            typeBuilder.SetCustomAttribute(swaggerSchemaAttributeBuilder);
          }

          if ((!response && options.EnableRequestSidechannel) || (response && options.EnableResponseSidechannel)) {
            DynamicUjmwControllerFactory.BuildProp(typeBuilder, UjmwSideChannelPropertyName, typeof(Dictionary<string, string>), "Used to flow additional ambient data (see UJMW standard)");
          }
        
          List<string> createdPropertyNames = new List<string>();
          var parameters = methodInfo.GetParameters();
          foreach (var param in parameters) {
            if ((response && param.IsOut) || (!response && !param.IsOut) || (param.ParameterType.IsByRef && !param.IsOut)) {
              string doc = param.GetDocumentation();
              if (param.ParameterType.IsByRef) {
                DynamicUjmwControllerFactory.BuildProp(typeBuilder, param.Name, param.ParameterType.GetElementType(), doc);  
              }
              else {
                DynamicUjmwControllerFactory.BuildProp(typeBuilder, param.Name, param.ParameterType,doc);
              }
              createdPropertyNames.Add(param.Name);
            }
          }

          if (response) {
            //only relevant for the response DTO

            if (methodInfo.ReturnType != null && methodInfo.ReturnType != typeof(void)) {
              DynamicUjmwControllerFactory.BuildProp(typeBuilder, UjmwReturnPropertyName, methodInfo.ReturnType, methodInfo.GetDocumentationForReturn(true));
            }

            DynamicUjmwControllerFactory.BuildProp(typeBuilder, UjmwFaultPropertyName, typeof(string),"Optional field, which can be used to transport an error-message.");
         
          }
          else {
            //only relevant for the request DTO

            foreach (KeyValuePair<string, Tuple<string, Type>> requiredArgument in options.ContextualDtoArguments) { 
          
              //for each required argument, check if the property is already exisits (by normal method-args)
              string dtoPropertyName = requiredArgument.Value.Item1;
              if (!createdPropertyNames.Contains(dtoPropertyName)) {

                //otherwise, create it as 'shadow' property for this DTO (which will not be accessable within the BL, but contextual as well)
                Type dtoPropertyTypeIfCreating = requiredArgument.Value.Item2;
                DynamicUjmwControllerFactory.BuildProp(typeBuilder, dtoPropertyName, dtoPropertyTypeIfCreating);
              
              }
            }

          }

          Type dtoType = typeBuilder.CreateType();

          //caching...
          _DtoTypeCache[wrapperTypeFullName] = dtoType;

          return dtoType;
        }
      }
    }

    private static void BuildProp(TypeBuilder typeBuilder, string propName, Type propType, string dscr = null) {

      FieldBuilder fieldBdr = typeBuilder.DefineField("_" + propName, propType, FieldAttributes.Private);

      var propBdr = typeBuilder.DefineProperty(propName, PropertyAttributes.HasDefault, propType, null);

      if (_SwaggerSchemaAttributeConstructor != null && !string.IsNullOrWhiteSpace(dscr)) {
        CustomAttributeBuilder swaggerSchemaAttributeBuilder = new CustomAttributeBuilder(
          _SwaggerSchemaAttributeConstructor, new object[] { dscr }
        );
        propBdr.SetCustomAttribute(swaggerSchemaAttributeBuilder);
      }

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

    internal static bool TryGetContractMethod(Type serviceContractType, string methodName, out MethodInfo method) {
      method = serviceContractType.GetMethod(methodName);
      if (method != null) {
        return true;
      }
      foreach (Type aggregatedContract in serviceContractType.GetInterfaces()) {
        if (TryGetContractMethod(aggregatedContract, methodName, out method)) {
          return true;
        }
      }
      if (serviceContractType.BaseType != null) {
        return TryGetContractMethod(serviceContractType.BaseType, methodName, out method);
      }
      else {
        return false;
      }
    }

    internal static void CollectAllMethodsForType(Type t, List<MethodInfo> target) {
      foreach (MethodInfo mi in t.GetMethods()) {
        target.Add(mi);
      }
      if (t.BaseType != null) {
        CollectAllMethodsForType(t.BaseType, target);
      }
      foreach (Type intf in t.GetInterfaces()) {
        CollectAllMethodsForType(intf, target);
      }
    }

  }

}