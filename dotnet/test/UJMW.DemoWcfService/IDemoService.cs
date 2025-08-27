﻿using DistributedDataFlow;
using System;
using System.Collections.Generic;
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
  [ServiceContract, SwaggerWcf("DemoService.svc"), SwaggerWcfServiceInfo("BaseContract", "v1")]
#endif
  [HasDataFlowSideChannel("tenant-identifiers"), HasDataFlowBackChannel("tenant-identifiers")]
  public interface IBaseContract {

#if NET5_0_OR_GREATER
#else
    [OperationContract, WebInvoke(Method = "POST")]
#endif
    string BaseMethod();

  }

#if NET5_0_OR_GREATER
#else
    [ServiceContract, SwaggerWcf("DemoService.svc"), SwaggerWcfServiceInfo("DemoService", "v1")]
#endif
  [HasDataFlowSideChannel("tenant-identifiers"), HasDataFlowBackChannel("tenant-identifiers")]
  public interface IDemoService : IDisposable, IBaseContract {
    

#if NET5_0_OR_GREATER
#else
    [OperationContract, WebInvoke(Method = "POST")]
#endif
    string GetData(int value);

    /// <summary>
    /// the
    /// multiline
    /// summary!!
    /// </summary>
    /// <param name="composite">the description for the composit type</param>
    /// <returns>this is the return</returns>
#if NET5_0_OR_GREATER
#else
    [OperationContract, WebInvoke(Method = "POST")]
#endif
    CompositeType GetDataUsingDataContract(CompositeType composite);

    /// <summary>
    /// eine methode mit 2x out
    /// An Unified Json Message Wrapper, which contains the following fields:
    /// An Unified Json Message Wrapper, which contains the following fields:An Unified Json Message Wrapper, which contains the following fields:An Unified Json Message Wrapper, which contains the following fields:
    /// </summary>
    /// <param name="otp">opt geht nur rein</param>
    /// <param name="fileName"> geht raus</param>
    /// <param name="fileContentType">geht rein und raus</param>
    /// <returns>retunernt eine nstream</returns>
#if NET5_0_OR_GREATER
#else
    [OperationContract, WebInvoke(Method = "POST")]
#endif
    int TestSuport(string otp, out string fileName, ref string fileContentType);

#if NET5_0_OR_GREATER
#else
    [OperationContract, WebInvoke(Method = "POST")]
#endif

    int ParamlessCall();

  }

  public interface IDemoFileService {

    ///// <summary>
    ///// eine methode mit 2x out
    ///// </summary>
    ///// <param name="otp">opt geht nur rein</param>
    ///// <param name="fileName"> geht raus</param>
    ///// <param name="fileContentType">geht rein und raus</param>
    ///// <returns>retunernt eine nstream</returns>
    Stream Download1(string otp, out string fileName, ref string fileContentType);

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

    /// <summary>
    /// THE BOOL
    /// </summary>
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

    [DataMember]
    public Dictionary<string,string> ADict { get; set; } = new Dictionary<string, string>();
   

    [DataMember]
    public Object AObj { get; set; } = null;


  }

}
