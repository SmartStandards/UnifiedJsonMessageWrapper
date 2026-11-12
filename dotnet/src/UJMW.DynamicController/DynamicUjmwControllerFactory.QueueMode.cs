using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace System.Web.UJMW {

  partial class DynamicUjmwControllerFactory {

    private static ConstructorInfo _QueueControllerBaseConstructor = typeof(UjmwQueueControllerBase).GetConstructors(
      BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
    ).Single();

    /// <summary>
    /// Emits the tiny '&lt;route&gt;/_queue' controller subclass of 'UjmwQueueControllerBase' for a queue-mode contract
    /// (carrying only the route, api-group and auth attributes, see 'doc/epics/epic-async-queue-mode.md', 3.4).
    /// </summary>
    internal static Type BuildQueueControllerType(
      Type serviceType, DynamicUjmwControllerOptions options, string resolvedControllerRoute, string resolvedControllerName,
      ModuleBuilder moduleBuilder, string apiGroupName
    ) {

      string queueRoute = resolvedControllerRoute.TrimEnd('/') + "/" + UjmwQueueProtocol.QueueRouteSegment;
      string controllerNamespace = GetNamespaceFromApiGroupName(apiGroupName);

      TypeBuilder typeBuilder = moduleBuilder.DefineType(
        controllerNamespace + "." + resolvedControllerName + "QueueController",
        TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.AutoLayout,
        typeof(UjmwQueueControllerBase)
      );

      typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(_RouteAttributeConstructor, new object[] { queueRoute }));

      if (!string.IsNullOrWhiteSpace(apiGroupName)) {
        typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(
          _ApiExplorerSettingsAttributeConstructor, new object[0],
          new[] { _ApiExplorerSettingsGroupNameProp }, new object[] { apiGroupName }
        ));
      }

      if (_TagsAttributeContructor != null) {
        typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(
          _TagsAttributeContructor, new object[] { new string[] { resolvedControllerName + " (Queue)" } }
        ));
      }

      if (options.EnableAuthHeaderEvaluatorHook) {

        typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(_AuthHeaderInterceptorAttributeConstructor, new object[] { }));

        if (options.AllowAnonymousForAuthHeaderEvaluatorHook) {
          typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(_AllowAnonymousAttributeConstructor, new object[] { }));
        }

      }

      if (options.AuthAttribute != null) {

        ConstructorInfo authAttributeConstructor = options.AuthAttribute.GetConstructors().Where(
          (c) => c.GetParameters().Length == options.AuthAttributeConstructorParams.Length
        ).Single();

        typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(authAttributeConstructor, options.AuthAttributeConstructorParams));

      }

      ConstructorBuilder constructorBuilder = typeBuilder.DefineConstructor(
        MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
        CallingConventions.Standard,
        new[] { typeof(IUjmwQueuingService) }
      );

      ILGenerator constructorIL = constructorBuilder.GetILGenerator();
      constructorIL.Emit(OpCodes.Ldarg_0);
      constructorIL.Emit(OpCodes.Ldarg_1);
      constructorIL.Emit(OpCodes.Call, _QueueControllerBaseConstructor);
      constructorIL.Emit(OpCodes.Ret);

      Type queueControllerType = typeBuilder.CreateType();

      UjmwQueueRegistry.RegisterController(queueControllerType, serviceType, resolvedControllerRoute);

      return queueControllerType;
    }

  }

}
