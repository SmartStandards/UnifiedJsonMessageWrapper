using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;

namespace UJMW.DemoWcfService {
 
  [ServiceContract]
  public interface IDemoService {

    [OperationContract]
    string GetData(int value);

    [OperationContract]
    CompositeType GetDataUsingDataContract(CompositeType composite);

  }

  [DataContract]
  public class CompositeType {
    bool boolValue = true;
    string stringValue = "Hello ";

    [DataMember]
    public bool BoolValue {
      get { return boolValue; }
      set { boolValue = value; }
    }

    [DataMember]
    public string StringValue {
      get { return stringValue; }
      set { stringValue = value; }
    }
  }

}
