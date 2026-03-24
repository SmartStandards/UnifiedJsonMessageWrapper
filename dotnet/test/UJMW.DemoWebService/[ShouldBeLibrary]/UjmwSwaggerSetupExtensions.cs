using Jose;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using SmartStandards;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.Web.UJMW;
using System.Web.UJMW.SelfAnnouncement;

namespace Microsoft.AspNetCore.Builder {

  public static partial class UjmwSwaggerSetupExtensions {

    public static void AddSwaggerGenSmartStandardsFlavored(
      this IServiceCollection services, Func<string, string> oAuthUrlResolver = null
    ) {

      services.AddSwaggerGen();

      services.AddTransient<IConfigureOptions<SwaggerGenOptions>, SmartStandardsSwaggerGenOptionsConfigurator>(
        (p) => new SmartStandardsSwaggerGenOptionsConfigurator(
          p.GetRequiredService<IApiDescriptionGroupCollectionProvider>(),
          p.GetRequiredService<IConfiguration>(),
          oAuthUrlResolver
         )
      );

      services.AddTransient<IStartupFilter, SmartStandardsSwaggerStartupFilter>(
        (p) => new SmartStandardsSwaggerStartupFilter(
          p.GetRequiredService<IApiDescriptionGroupCollectionProvider>(),
          p.GetRequiredService<IConfiguration>()
        )
       );

    }

  }
}

namespace SmartStandards {

  public sealed class SmartStandardsSwaggerGenOptionsConfigurator : IConfigureOptions<SwaggerGenOptions> {

    private readonly IApiDescriptionGroupCollectionProvider _Provider;

    private readonly IConfiguration _Configuration;

    private readonly Func<string, string> _OAuthUrlResolver;

    /// <summary>
    /// Creates a new instance of the UjmwStandardSwaggerGenOptionsConfigurator class.
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="configuration"></param>
    /// <param name="oAuthUrlResolver"></param>
    public SmartStandardsSwaggerGenOptionsConfigurator(
      IApiDescriptionGroupCollectionProvider provider, IConfiguration configuration, Func<string, string> oAuthUrlResolver
    ) {
      _Provider = provider;
      _Configuration = configuration;
      _OAuthUrlResolver = oAuthUrlResolver;
    }

    /// <summary>
    /// Configures the SwaggerGenOptions for the API documentation generation.
    /// </summary>
    /// <param name="options"></param>
    public void Configure(SwaggerGenOptions options) {

      string baseUrl = _Configuration.GetValue<string>("BaseUrl");

      ApiDescriptionGroup[] groups = _Provider.ApiDescriptionGroups.Items.Where(
        g => !string.IsNullOrWhiteSpace(g.GroupName)
      ).Distinct().ToArray();

      options.ResolveConflictingActions(apiDescriptions => {
        return apiDescriptions.First();
      });

      options.EnableAnnotations(true, true);

      #region " Bearer "

      options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "API-TOKEN"
      });

      options.AddSecurityRequirement(
        new OpenApiSecurityRequirement { {
            new OpenApiSecurityScheme {
              Reference = new OpenApiReference {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
              }
            },
            new string[] {}
          }}
      );

      #endregion

      #region " OAuth " 

      //if (_Configuration.GetValue<bool>("EnableSwaggerUi")) 
      string oAuthClientId = _Configuration.GetValue<string>("OAuthClientIdForSwaggerUi");
      string oAuthUrl = _Configuration.GetValue<string>("OAuthAuthorizeUrlForSwaggerUi");

      //TODO: evtl wenn wir selbst eine oatuh-route haben!!!!!!!!

      if (_OAuthUrlResolver != null) {
        oAuthUrl = _OAuthUrlResolver(oAuthUrl);
      }

      if (!string.IsNullOrWhiteSpace(oAuthClientId) && !string.IsNullOrWhiteSpace(oAuthUrl)) {


        string oAuthScopeExpression = _Configuration.GetValue<string>("OAuthScopeExpressionForSwaggerUi", "");
        string[] splittedOAuthScopes = oAuthScopeExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();

        Dictionary<string, string> scopesWithLabels = splittedOAuthScopes.Select(
          scope => new KeyValuePair<string, string>(scope, $"Scope: {scope}")
        ).ToDictionary(kv => kv.Key, kv => kv.Value);

        options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme {
          Type = SecuritySchemeType.OAuth2,
          Flows = new OpenApiOAuthFlows {
            Implicit = new OpenApiOAuthFlow {
              AuthorizationUrl = new Uri(oAuthUrl), // deine Auth-URL
              Scopes = scopesWithLabels
            }
          }
        });

        // Security Requirement, damit Swagger UI die Authorisierung anfordert
        options.AddSecurityRequirement(
          new OpenApiSecurityRequirement { {
            new OpenApiSecurityScheme {
              Reference = new OpenApiReference {
                Type = ReferenceType.SecurityScheme,
                Id = "oauth2"
              },
              Scheme = "oauth2",
              Name = "Authorization", //ACHTUNG: Header-Value ist mit prefix "JWT <token>"
              In = ParameterLocation.Header
            },
            splittedOAuthScopes.ToList()
          }}
        );


      }

      #endregion

      options.UseInlineDefinitionsForEnums();

      foreach (ApiDescriptionGroup group in groups) {

        // Ressolve descriptors to real Controller-Types
        Type[] resolvableControllerTypes = TryResolveControllerTypesForApiGroup(group);

        // Include XML-Documentation for all resolvable Controller-Types
        foreach (Type controllerType in resolvableControllerTypes) {
          if (!controllerType.Assembly.IsDynamic) {
            string estimatedXmlDocumentationFile = controllerType.Assembly.Location.Replace(".dll", ".xml");
            options.IncludeXmlComments(estimatedXmlDocumentationFile, false);
          }
        }

        //Try to discover Version for an API-Group by looking for the first resolvable Controller-Type and then looking for the version of its assembly.
        string apiVersion = TryGetApiVersionFromControllerType(
          resolvableControllerTypes.FirstOrDefault(), out string contractName
        );

        options.SwaggerDoc(
          group.GroupName!,
          new OpenApiInfo {
            Title = $"{group.GroupName}",
            Version = apiVersion
          }
        );

      }
    }

    internal static Type[] TryResolveControllerTypesForApiGroup(ApiDescriptionGroup group) {

      // Enumerate Constrollers-Descriptors
      ControllerActionDescriptor[] controllerActionDescriptors = group.Items.Select(
        (item) => item.ActionDescriptor
      ).OfType<ControllerActionDescriptor>().Distinct().ToArray();

      // Ressolve descriptors to real Controller-Types
      Type[] resolvableControllerTypes = controllerActionDescriptors.Where(
        (cad) => cad.ControllerTypeInfo != null
      ).Select(
        (cad) => cad.ControllerTypeInfo.AsType()
      ).Distinct().ToArray();

      return resolvableControllerTypes;
    }

    internal static string TryGetApiVersionFromControllerType(Type controllerType, out string contractName) {

      if (controllerType == null) {
        contractName = "Unknown";
        return "0.0.0";
      }

      Type implementationType = controllerType;

      // special knowledge about UJMW Facade-Controllers:
      // If the controller is from a dynamic assembly and its name contains "UJMW",
      // then we try to look for the first constructor and take the type of its first
      // parameter as the "real" implementation type, which might be located in a different
      // assembly that has a version number.
      if (controllerType.Assembly.IsDynamic && controllerType.Assembly.FullName.Contains("UJMW")) {
        ConstructorInfo ujmwFacedeCtor = controllerType.GetConstructors().FirstOrDefault();
        if (ujmwFacedeCtor != null) {
          Type firstConstructorParamType = ujmwFacedeCtor.GetParameters().Select(p => p.ParameterType).FirstOrDefault();
          if (firstConstructorParamType != null) {
            implementationType = firstConstructorParamType;
          }
        }
      }

      contractName = implementationType.Name.Replace("Controller", "");
      return implementationType.Assembly.GetName().Version?.ToString(3);

    }

  }

  public sealed class SmartStandardsSwaggerStartupFilter : IStartupFilter {

    private readonly IApiDescriptionGroupCollectionProvider _Provider;

    private readonly IConfiguration _Configuration;

    public SmartStandardsSwaggerStartupFilter(IApiDescriptionGroupCollectionProvider provider, IConfiguration configuration) {
      _Provider = provider;
      _Configuration = configuration;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
      return (app) => {

        string oAuthClientId = _Configuration.GetValue<string>("OAuthClientIdForSwaggerUi");

        app.UseSwagger(o => {
          //warning: needs subfolder! jsons cant be within same dir as swaggerui (below)
          o.RouteTemplate = "docs/schema/{documentName}.{json|yaml}";
          //o.SerializeAsV2 = true;
        });

        ApiDescriptionGroup[] groups = _Provider.ApiDescriptionGroups.Items.Where(
          g => !string.IsNullOrWhiteSpace(g.GroupName) && g.GroupName != "hidden"
        ).Distinct().ToArray();

        app.UseSwaggerUI(c => {

          c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
          c.DefaultModelExpandDepth(2);
          c.DefaultModelsExpandDepth(2);
          //c.ConfigObject.DefaultModelExpandDepth = 2;

          c.DocumentTitle = "OpenAPI Definition(s)";

          foreach (ApiDescriptionGroup group in groups) {

            // Ressolve descriptors to real Controller-Types
            Type[] resolvableControllerTypes = SmartStandardsSwaggerGenOptionsConfigurator.TryResolveControllerTypesForApiGroup(group);

            //Try to discover Version for an API-Group by looking for the first resolvable Controller-Type and then looking for the version of its assembly.
            string apiVersion = SmartStandardsSwaggerGenOptionsConfigurator.TryGetApiVersionFromControllerType(
              resolvableControllerTypes.FirstOrDefault(), out string contractName
            );

            c.SwaggerEndpoint($"schema/{group.GroupName}.json", $"{group.GroupName}");
            //c.SwaggerEndpoint($"schema/{group.GroupName}.json", $"{contractName} ({group.GroupName})");
          }

          c.RoutePrefix = "docs";

          if (!string.IsNullOrWhiteSpace(oAuthClientId)) {
            c.OAuthClientId(oAuthClientId);
          }

          //requires MVC app.UseStaticFiles();
          //c.InjectStylesheet(baseUrl + "swagger-ui/custom.css");

        });


        next(app);
      };
    }
  }




}


