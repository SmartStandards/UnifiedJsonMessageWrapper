using DistributedDataFlow;
using System;
using System.IO;
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
  [HasDataFlowSideChannel("tenant-identifiers"), HasDataFlowBackChannel("tenant-identifiers")]
  public interface IDemoService : IDisposable {
    

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

  public interface IDemoFileService {

    Stream Download1(string otp, out string fileName, out string fileContentType);
    Stream Download2(string otp, out string fileContentType);
    Stream Download3(string otp, out string fileName);
    Stream Download4(string otp);

    void Upload(string otp, Stream file, string fileName, string fileContentType);
    void Upload(string otp, Stream file, string fileContentType);
    void Upload(string otp, Stream file);
    void Upload(string otp, Stream file1, string file1Name, string file1ContentType, Stream file2);
    void Upload(string otp, Stream[] files, string[] fileNames, string[] fileContentTypes, Stream file2);

    Stream UpAndDown(string otp,
      Stream inputFile, string inputFileName, string inputFileContentType, 
      out string fileName, out string fileContentType
    );

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
