using System.Web.UJMW;

  Console.WriteLine("Please enter a number:");
  if(int.TryParse(Console.ReadLine(),out int number)) {

    var svc = DynamicClientFactory.CreateInstance<UJMW.DemoWcfService.IDemoService>("http://localhost:55202/DemoService.svc");

    try {

      var result = svc.GetData(number);
      Console.WriteLine("RESPONSE from Service:");
      Console.WriteLine("\"" + result + "\"");
    }
    catch (Exception ex) {
      Console.WriteLine("EXCEPTION: " + ex.Message);
    }

  }
  else {
    Console.WriteLine("Invalid number!");
  }
  Console.ReadLine();