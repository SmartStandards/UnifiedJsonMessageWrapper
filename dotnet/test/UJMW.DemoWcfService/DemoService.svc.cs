using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Text;
using System.Threading;
#if NET5_0_OR_GREATER
#else
using System.ServiceModel.Web;
#endif

namespace UJMW.DemoWcfService {

  //IMPORTANT: please take notice that with in markup of the 'DemoService.svc' the UJMW-Factory has been choosen:
  //YOU WILL FIND: ... Factory="System.Web.UJMW.UjmwServiceHostFactory" %>

  public class DemoService : IDemoService {

    AmbientField currentTenant = new AmbientField("currentTenant", true);

    /*
     *  To test the demo send a HTTP-POST to
     *  http://localhost:55202/DemoService.svc/GetData
     *  and provide a body with mime-type 'application/json' with content '{"value":42}'.
     *  As response, you should receive a HTTP-200 with a body content '{ "return": "You entered: 42"}'.
     */
    public string GetData(int value) {
      //return string.Format("You entered: {0}", value);
      return string.Format("You entered: {0} (current Tenant is {1})", value, currentTenant.Value);
    }

    public CompositeType GetDataUsingDataContract(CompositeType composite) {
      if (composite == null) {
        throw new ArgumentNullException("composite");
      }
      if (composite.BoolValue) {
        composite.StringValue += "Suffix";
      }
      return composite;
    }

    public void Dispose() {
    }

  }

}
