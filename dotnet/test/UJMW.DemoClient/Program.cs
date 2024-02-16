using DistributedDataFlow;
using System;
using System.Threading;
using System.Web.UJMW;

  Console.WriteLine("Please enter a number:");
  if(int.TryParse(Console.ReadLine(), out int number)) {

  #region " play arround with ambience & dataflow "

    AmbientField.ContextAdapter = new AmbienceToAppdomainAdapter();
    var currentTenant = new AmbientField("currentTenant", true);
    currentTenant.Value = "Rabbit";

    UjmwClientConfiguration.ConfigureRequestSidechannel((ctct, chnl) => {
      chnl.ProvideUjmwUnderlineProperty();
      chnl.CaptureDataVia(AmbienceHub.CaptureCurrentValuesTo);
    });

  //UjmwClientConfiguration.ConfigureResponseBackchannel((ctct, chnl) => {
  //  chnl.AcceptUjmwUnderlineProperty();
  //  chnl.ProcessDataVia(AmbienceHub.RestoreValuesFrom);
  //});

  #endregion

    UjmwClientConfiguration.DefaultAuthHeaderGetter = ((c) => "its me");

    var svc = DynamicClientFactory.CreateInstance<UJMW.DemoWcfService.IDemoService>(
      "http://localhost:55202/DemoService.svc"
    );

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