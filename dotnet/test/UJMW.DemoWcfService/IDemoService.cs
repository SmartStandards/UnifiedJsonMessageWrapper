using System;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace UJMW.DemoWcfService {

  [ServiceContract]
  public interface IDemoService : IDisposable{

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
