using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using System;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using System.IO;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi.Writers;
using Security.AccessTokenHandling;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Security.AccessTokenHandling.OAuthServer;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using System.Web.UJMW;
using Microsoft.AspNetCore.Authorization;
using System.Net.Http;
using UJMW.DemoWcfService;
using Microsoft.AspNetCore;
using DistributedDataFlow;
using Demo;

namespace Security {

  public class Startup {

    public Startup(IConfiguration configuration) {
      _Configuration = configuration;
    }

    private static IConfiguration _Configuration = null;
    public static IConfiguration Configuration { get { return _Configuration; } }

    const string _ApiTitle = "UJMW Demo";
    Version _ApiVersion = null;

    public void ConfigureServices(IServiceCollection services) {

      services.AddLogging();

      _ApiVersion = typeof(IDemoService).Assembly.GetName().Version;

      string outDir = AppDomain.CurrentDomain.BaseDirectory;
      //string masterApiClientSecret = _Configuration.GetValue<string>("MasterApiClientSecret");

      //var apiService = new ApiService(
      //  _Configuration.GetValue<string>("MasterApiClientSecret"),
      //  _Configuration.GetValue<string>("MasterApiClientId"),
      //  _Configuration.GetValue<string>("MasterJwtIssuer"),
      //  _Configuration.GetValue<string>("MasterJwtAudience")
      //);

      //services.AddSingleton<IAccessTokenValidator>(apiService);
      //services.AddSingleton<IOAuth>(apiService);
      //services.AddSingleton<IAccessTokenIntrospector>(apiService);

      //services.AddSingleton<IOAuthService>(apiService);

      /*services.AddSingleton<IEnvironmentAdministrationService>(apiService);
      services.AddSingleton<IEnvironmentSetupService>(apiService);
      services.AddSingleton<IUserAdminstrationService>(apiService);
      services.AddSingleton<IUserSelfAdministrationService>(apiService);*/

      //services.AddCors(opt => {
      //  opt.AddPolicy(
      //    "MyCustomCorsPolicy",
      //    c => c
      //      .AllowAnyOrigin()
      //      .AllowAnyHeader()
      //      .AllowAnyMethod()
      //      .DisallowCredentials()
      //  );
      //});

      //we are our own evaluator
      //DefaultAccessTokenValidator.Instance = apiService;
      //Security.AccessTokenHandling.OAuthServer.

      services.AddControllers();

      AmbienceHub.DefineFlowingContract(
        "tenant-identifiers",
        (contract) => {
          contract.IncludeExposedAmbientFieldInstances("currentTenant");
        }
      );

      UjmwHostConfiguration.ConfigureRequestSidechannel(
        (serviceType, sideChannel) => {
          if (HasDataFlowSideChannelAttribute.TryReadFrom(serviceType, out string contractName)) {

            sideChannel.AcceptHttpHeader("my-ambient-data");
            sideChannel.AcceptUjmwUnderlineProperty();

            sideChannel.ProcessDataVia(
              (incommingData) => AmbienceHub.RestoreValuesFrom(incommingData, contractName)
            );
          }
          else {
            sideChannel.AcceptNoChannelProvided();
            //sideChannel.AcceptNoChannelProvided(
            //  (ref IDictionary<string, string> defaultData) => {
            //    defaultData["currentTenant"] = "(fallback)";
            //  }
            //);
          }
        }
      );

      UjmwHostConfiguration.ConfigureResponseSidechannel(
        (serviceType, sideChannel) => {
          if (HasDataFlowBackChannelAttribute.TryReadFrom(serviceType, out string contractName)) {

            sideChannel.ProvideHttpHeader("my-ambient-data");
            sideChannel.ProvideUjmwUnderlineProperty();

            sideChannel.CaptureDataVia(
              (snapshot) => AmbienceHub.CaptureCurrentValuesTo(snapshot, contractName)
            );
          }
          else {
            sideChannel.ProvideNoChannel();
          }
        }
      );

      AccessTokenValidator.ConfigureTokenValidation(
        new LocalJwtIntrospector("TheSignKey"),
        (cfg) => {
        }
      );

      UjmwHostConfiguration.AuthHeaderEvaluator = (
        (string rawAuthHeader, Type contractType, MethodInfo targetContractMethod, string callingMachine, ref int httpReturnCode, ref string failedReason) => {
          //in this demo - any auth header is ok - but there must be one ;-)
          if (string.IsNullOrWhiteSpace(rawAuthHeader)) {
            httpReturnCode = 403;
            failedReason = "This demo requires at least ANY string as authheader!";
            return false;
          }
          return true;
        }
      );
      UjmwHostConfiguration.ArgumentPreEvaluator = (
        Type contractType,
        MethodInfo calledContractMethod,
        object[] arguments
      ) => {

        contractType.ToString();
      };

      var svc = new DemoService();
      services.AddSingleton<IDemoService>(svc);
      services.AddSingleton<IDemoFileService>(svc);

      services.AddDynamicUjmwControllers(r => {
        //NOTE: the '.svc' suffix is only to have the same url as in the WCF-Demo
        r.AddControllerFor<IDemoService>(new DynamicUjmwControllerOptions {
          ControllerRoute = "DemoService.svc",
          //EnableResponseSidechannel = false,
          //AuthAttribute = typeof(EvaluateBearerTokenAttribute),
          //AuthAttributeConstructorParams = new object[] { new string[] { } }
        });

        r.AddControllerFor<IDemoFileService>(new DynamicUjmwControllerOptions {
          ControllerRoute = "FileStore",
          EnableResponseSidechannel = false,
          EnableRequestSidechannel = false,
          //AuthAttribute = typeof(EvaluateBearerTokenAttribute),
          //AuthAttributeConstructorParams = new object[] { new string[] { } }
        });

        var repoControllerOptions = new DynamicUjmwControllerOptions {
          ControllerRoute = "Repo/{0}",
          EnableResponseSidechannel = false,
          EnableRequestSidechannel = false,
          //AuthAttribute = typeof(EvaluateBearerTokenAttribute),
          //AuthAttributeConstructorParams = new object[] { new string[] { } }
          ClassNameDiscriminator = "_{0}_",
          ControllerTitle = "Gen ({0})"

        };

        r.AddControllerFor<IGenericInterface<Foo, int>>(repoControllerOptions);
        r.AddControllerFor<IGenericInterface<Bar, string>>(repoControllerOptions);

      });

      services.AddSwaggerGen(c => {

        c.ResolveConflictingActions(apiDescriptions => {
          return apiDescriptions.First();
        });
        c.EnableAnnotations(true, true);

        //c.IncludeXmlComments(outDir + ".......Contract.xml", true);
        //c.IncludeXmlComments(outDir + "........Service.xml", true);
        //c.IncludeXmlComments(outDir + "........WebAPI.xml", true);

        #region bearer

        string getLinkMd = "";
        //if (!string.IsNullOrWhiteSpace(masterApiClientSecret)) {
        //  getLinkMd = " [get one...](../oauth?state=myState&client_id=master&login_hint=API-CLIENT&redirect_uri=/oauth/display)";
        //}

        //https://www.thecodebuzz.com/jwt-authorization-token-swagger-open-api-asp-net-core-3-0/
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme {
          Name = "Authorization",
          Type = SecuritySchemeType.ApiKey,
          Scheme = "Bearer",
          BearerFormat = "JWT",
          In = ParameterLocation.Header,
          Description = "API-TOKEN" + getLinkMd
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
          {
              {
                    new OpenApiSecurityScheme
                      {
                          Reference = new OpenApiReference
                          {
                              Type = ReferenceType.SecurityScheme,
                              Id = "Bearer"
                          }
                      },
                      new string[] {}

              }
          });

        #endregion

        c.UseInlineDefinitionsForEnums();

        c.SwaggerDoc(
          "ApiV3",
          new OpenApiInfo {
            Title = _ApiTitle + " - API",
            Version = _ApiVersion.ToString(3),
            Description = "NOTE: This is not intended be a 'RESTful' api, as it is NOT located on the persistence layer and is therefore NOT focused on doing CRUD operations! This HTTP-based API uses a 'call-based' approach to known BL operations. IN-, OUT- and return-arguments are transmitted using request-/response- wrappers (see [UJMW](https://github.com/SmartStandards/UnifiedJsonMessageWrapper)), which are very lightweight and are a compromise for broad support and adaptability in REST-inspired technologies as well as soap-inspired technologies!",
            //Contact = new OpenApiContact {
            //  Name = "",
            //  Email = "",
            //  Url = new Uri("")
            //},
          }
        );

      });

    }
    
    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerfactory) {

      var logFileFullName = _Configuration.GetValue<string>("LogFileName");
      var logDir = Path.GetFullPath(Path.GetDirectoryName(logFileFullName));
      Directory.CreateDirectory(logDir);
      loggerfactory.AddFile(logFileFullName);

      //required for the www-root
      app.UseStaticFiles();

      app.UseAmbientFieldAdapterMiddleware();

      if (!_Configuration.GetValue<bool>("ProdMode")) {
        app.UseDeveloperExceptionPage();
      }

      if (_Configuration.GetValue<bool>("EnableSwaggerUi")) {
        var baseUrl = _Configuration.GetValue<string>("BaseUrl");

        app.UseSwagger(o => {
          //warning: needs subfolder! jsons cant be within same dir as swaggerui (below)
          o.RouteTemplate = "docs/schema/{documentName}.{json|yaml}";
          //o.SerializeAsV2 = true;
        });

        app.UseSwaggerUI(c => {

          c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
          c.DefaultModelExpandDepth(2);
          c.DefaultModelsExpandDepth(2);
          //c.ConfigObject.DefaultModelExpandDepth = 2;

          c.DocumentTitle = _ApiTitle + " - OpenAPI Definition(s)";

          //represents the sorting in SwaggerUI combo-box
          c.SwaggerEndpoint("schema/ApiV3.json", _ApiTitle + " - API v" + _ApiVersion.ToString(3));

          c.RoutePrefix = "docs";

          //requires MVC app.UseStaticFiles();
          c.InjectStylesheet(baseUrl + "swagger-ui/custom.css");

        });

      }

      app.UseHttpsRedirection();

      app.UseRouting();

      //CORS: muss zwischen 'UseRouting' und 'UseEndpoints' liegen!
      app.UseCors(p =>
          p.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader()
      );

      app.UseAuthentication(); //<< WINDOWS-AUTH
      app.UseAuthorization();

      app.UseEndpoints(endpoints => {
        endpoints.MapControllers();
      });

      var allRoutesToAnnounce = DynamicUjmwControllerRegistrar.GetAllRegisteredServiceTypesByRoute();

    }

  }

}
