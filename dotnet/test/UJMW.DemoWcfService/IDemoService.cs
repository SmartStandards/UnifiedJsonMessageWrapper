using DistributedDataFlow;
using System;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Threading;
#if NET5_0_OR_GREATER
#else
  using SwaggerWcf.Attributes;
  using System.ServiceModel.Web;
#endif

namespace UJMW.DemoWcfService {

#if NET5_0_OR_GREATER
#else
  [ServiceContract, SwaggerWcf("DemoService.svc"), SwaggerWcfServiceInfo("DemoService", "v1")]
#endif
  [HasDataFlowSideChannel("tenant-identifiers")]
  public interface IDemoService : IDisposable{

#if NET5_0_OR_GREATER
#else
    [OperationContract, WebInvoke(Method = "POST")]
#endif
    string GetData(int value);

#if NET5_0_OR_GREATER
#else
    [OperationContract, WebInvoke(Method = "POST")]
#endif
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
