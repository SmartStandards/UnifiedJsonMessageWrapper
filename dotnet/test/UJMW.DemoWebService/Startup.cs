using Demo;
using DistributedDataFlow;
using Logging.SmartStandards;
using Logging.SmartStandards.AspSupport;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Security.AccessTokenHandling;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.UJMW;
using System.Web.UJMW.SelfAnnouncement;
using UJMW.DemoCommandLineExe;
using UJMW.DemoWcfService;

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
      services.AddSmartStandardsLogging(_Configuration);

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
          contract.IncludeExposedAmbientFieldInstances("dtHandle");
        }
      );

      UjmwHostConfiguration.UseCombinedDynamicAssembly = true;

      UjmwHostConfiguration.ConfigureRequestSidechannel(
        (serviceType, sideChannel) => {
          if (HasDataFlowSideChannelAttribute.TryReadFrom(serviceType, out string contractName)) {

            sideChannel.AcceptHttpHeader("my-ambient-data");
            sideChannel.AcceptUjmwUnderlineProperty();
            sideChannel.AcceptContextualArguments();

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

      UjmwClientConfiguration.ConfigureRequestSidechannel((serviceType, sideChannel) => {
        if (HasDataFlowSideChannelAttribute.TryReadFrom(serviceType, out string contractName)) {
          sideChannel.ProvideUjmwUnderlineProperty();
          sideChannel.CaptureDataVia(
            (snapshot) => AmbienceHub.CaptureCurrentValuesTo(snapshot, contractName)
          );
        }
        else {
          sideChannel.ProvideNoChannel();
        }
      });

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

      services.AddSingleton<IContextualizationDemo>(new ContextualizationDemo());





      services.AddSingleton<IDemoCliService>((sc) => {
        return DynamicClientFactory.CreateInstance<IDemoCliService>(
          new CommandLineExecutor(
            typeof(IDemoCliService),
            "..\\UJMW.DemoCommandLineExe\\bin\\Debug\\UJMW.DemoCommandLineExe.exe",
            CommandLineCallMode.Persistent
          )
        );
      });

      UjmwHostConfiguration.EnableApiGroupNameFallback = true;

      services.AddDynamicUjmwControllers(r => {

        r.AddControllerFor<IContextualizationDemo>((c) => {
          c.ApiGroupName = "Contextualization-Demo";
          c.BindContextualArgumentToRequestDto("dtHandle", propTypeIfGenerating: typeof(int));

          c.ContextualizationHook = (endpointContextualArguments, innerInvokeContextual) => {
            //ENTER THE CONTEXT
            innerInvokeContextual.Invoke();
            //LEAVE THE CONTEXT
          };

        });


        //NOTE: the '.svc' suffix is only to have the same url as in the WCF-Demo
        r.AddControllerFor<IDemoService>((c)=> {
          c.ApiGroupName = "Contextualization-Demo";
          c.ControllerRoute = "{tnt}/v1/[Controller].svc";
          c.BindContextualArgumentToRouteSegment("MandantAusRoute", "tnt");

          c.BindContextualArgument("skyfall", () => "007");
          c.BindContextualArgumentToHeaderValue("huhu", "hu-hu");

          c.ContextualizationHook = (endpointContextualArguments, innerInvokeContextual) => {
            //ENTER THE CONTEXT
            innerInvokeContextual.Invoke();
            //LEAVE THE CONTEXT
          };

        });

        r.AddControllerFor<IDemoFileService>(new DynamicUjmwControllerOptions {
          ControllerRoute = "FileStore",
          ApiGroupName = "Fileaccess-Demo"
        });

        var repoControllerOptions = new DynamicUjmwControllerOptions {
          ControllerRoute = "Repo/{0}",
          ControllerTitle = "Gen ({0})",
          ControllerNamePattern = "{0}Repository",
          ApiGroupName = "GenericRepo-Demo"
          
        };
        r.AddControllerFor<IGenericInterface<Foo, int>>(repoControllerOptions);
        r.AddControllerFor<IGenericInterface<Bar, string>>(repoControllerOptions);

        r.AddControllerFor<IFooStore>();
        r.AddControllerFor<IBarStore>();

        r.AddControllerFor<IDemoCliService>(new DynamicUjmwControllerOptions {
          ControllerRoute = "CliService",
          EnableAuthHeaderEvaluatorHook = false,
          ApiGroupName = "CLI-Demo"
        });

        r.AddAnnouncementTriggerEndpoint();

      });

      services.AddUjmwStandardSwaggerGen("Fileaccess-Demo");

    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(
      IApplicationBuilder app, IWebHostEnvironment env,
      ILoggerFactory loggerfactory, IHostApplicationLifetime lifetimeEvents
    ) {

      var logFileFullName = _Configuration.GetValue<string>("LogFileName");
      var logDir = Path.GetFullPath(Path.GetDirectoryName(logFileFullName));
      Directory.CreateDirectory(logDir);
      loggerfactory.AddFile(logFileFullName);



      //SmartStandardsTraceLogPipe.InitializeAsLoggerInput();
      //DevLogger.LogMethod = loggerfactory.CreateLogger<DevLogger>();

      //required for the www-root
      app.UseStaticFiles();

      app.UseAmbientFieldAdapterMiddleware();

      if (!_Configuration.GetValue<bool>("ProdMode")) {
        app.UseDeveloperExceptionPage();
      }

      var baseUrl = _Configuration.GetValue<string>("BaseUrl");

      
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

      //MUST BE AFTER 'UseEndpoints'/'MapControllers'
      app.UseUjmwStandardSwagger(_Configuration, "Fileaccess-Demo");

      SelfAnnouncementHelper.Configure(
        lifetimeEvents, app.ServerFeatures,
        (string[] baseUrls, EndpointInfo[] endpoints, bool act, ref string info) => {

          var sb = new StringBuilder();
          string timestamp = DateTime.Now.ToLongTimeString();

          Console.WriteLine("--------------------------------------");
          if (act) {
            Console.WriteLine("ANNOUNCE:");
          }
          else {
            Console.WriteLine("UN-ANNOUNCE:");
          }
          Console.WriteLine("--------------------------------------");
          foreach (EndpointInfo ep in endpoints) {
            foreach (string url in baseUrls) {
              Console.WriteLine(ep.ToString(url));
              sb.Append(ep.ToString(url));
              if (act) {
                sb.AppendLine(" >> ONLINE @" + timestamp);
              }
              else {
                sb.AppendLine(" >> offline @" + timestamp);
              }

            }
          }
          Console.WriteLine("--------------------------------------");

          File.WriteAllText("_AnnouncementInfo.txt", sb.ToString());

          info = "was additionally written into file '_AnnouncementInfo.txt'";

        },
        autoTriggerInterval: 1
      );


      //var ass = AppDomain.CurrentDomain.GetAssemblies().Where((a)=>a.IsDynamic).ToArray();
      //var tps = ass.First().GetTypes();
    }

  }

  public interface IFooStore : IGenericInterface<Foo, int> {
  }
  public interface IBarStore : IGenericInterface<Bar, string> {
  }
}
