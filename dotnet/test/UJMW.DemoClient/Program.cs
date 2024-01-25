using DistributedDataFlow;
using System;
using System.Net.Http;
using System.Threading;
using System.Web.UJMW;

  Console.WriteLine("Please enter a number:");
  if(int.TryParse(Console.ReadLine(),out int number)) {

  AmbientField.ContextAdapter = new AmbienceToAppdomainAdapter();
  var currentTenant = new AmbientField("currentTenant", true);
  currentTenant.Value = "Rabbit";

  //var svc = DynamicClientFactory.CreateInstance<UJMW.DemoWcfService.IDemoService>("http://localhost:55202/DemoService.svc");

  var httpClient = new HttpClient();

  //var svc = DynamicClientFactory.CreateInstance<UJMW.DemoWcfService.IDemoService>(
  //  httpClient, () => "http://localhost:55202/DemoService.svc"
  //);

  UjmwClientConfiguration.ConfigureStandardUjmwRequestSidechannel(AmbienceHub.CaptureCurrentValuesTo);


  var svc = DynamicClientFactory.CreateInstance<UJMW.DemoWcfService.IDemoService>(
    httpClient, () => "http://localhost:55202/DemoService.svc"
  );

  httpClient.DefaultRequestHeaders.Add("Authorization", "its me");

    try {

      var result = svc.GetData(number);
      Console.WriteLine("RESPONSE from Service:");
      Console.WriteLine("\"" + result + "\"");
    }
    catch (Exception ex) {
      Console.WriteLine("EXCEPTION: " + ex.Message);
    }

    svc.Dispose();

  }
  else {
    Console.WriteLine("Invalid number!");
  }

  Console.ReadLine();