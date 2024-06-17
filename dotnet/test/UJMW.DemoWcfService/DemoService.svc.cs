
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Text;
using System.Threading;
using static System.Net.WebRequestMethods;
#if NET5_0_OR_GREATER
using Microsoft.AspNetCore.Http;
#else
using System.ServiceModel.Web;
#endif

namespace UJMW.DemoWcfService {

  //IMPORTANT: please take notice that with in markup of the 'DemoService.svc' the UJMW-Factory has been choosen:
  //YOU WILL FIND: ... Factory="System.Web.UJMW.UjmwServiceHostFactory" %>

#if NET5_0_OR_GREATER
  public class DemoService : IDemoService, IDemoFileService {
#else
  public class DemoService : IDemoService {
#endif

     private static AmbientField currentTenant = new AmbientField("currentTenant", true);

    /*
     *  To test the demo send a HTTP-POST to
     *  http://localhost:55202/DemoService.svc/GetData
     *  and provide a body with mime-type 'application/json' with content '{"value":42}'.
     *  As response, you should receive a HTTP-200 with a body content '{ "return": "You entered: 42"}'.
     */
    public string GetData(int value) {
      //return string.Format("You entered: {0}", value);
      //currentTenant.Value = currentTenant.Value + "_" + value.ToString();
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

    public int TestSuport(string otp, out string fileName, ref string fileContentType) {
      fileName = otp;
      fileContentType = otp;
      return 1;
    }

    public int ParamlessCall() {
      return 123;
    }

    public string BaseMethod() {
      throw new Exception("bbom");
      return "Foo";
    }

    public void Dispose() {
    }

#if NET5_0_OR_GREATER

    Stream IDemoFileService.Download1(string otp, out string fileName, ref string fileContentType) {
      fileName = "FOO.txt";
      fileContentType = " text/plain";
      return System.IO.File.OpenRead("C:\\Temp\\AFS-Demo\\FOO.txt");
    }
    Stream IDemoFileService.Download2(string otp, out string fileContentType) {
      fileContentType = " text/plain";
      return System.IO.File.OpenRead("C:\\Temp\\AFS-Demo\\FOO.txt");
    }
    Stream IDemoFileService.Download3(string otp, out string fileName) {
      fileName = "FOO.txt";
      return System.IO.File.OpenRead("C:\\Temp\\AFS-Demo\\FOO.txt");
    }
    Stream IDemoFileService.Download4(string otp) {
      return System.IO.File.OpenRead("C:\\Temp\\AFS-Demo\\FOO.txt");
    }

    void IDemoFileService.Upload(string otp, Stream file, string fileName, string fileContentType) {
      var filePath = Path.Combine("C:\\Temp", fileName);
      using (var stream = System.IO.File.Create(filePath)) {
        file.CopyTo(stream);
      }
    }
    void IDemoFileService.Upload(string otp, Stream file, string fileContentType) {
      var filePath = Path.Combine("C:\\Temp", DateTime.Now.ToFileTime().ToString());
      using (var stream = System.IO.File.Create(filePath)) {
        file.CopyTo(stream);
      }
    }
    void IDemoFileService.Upload(string otp, Stream file) {
      var filePath = Path.Combine("C:\\Temp", DateTime.Now.ToFileTime().ToString());
      using (var stream = System.IO.File.Create(filePath)) {
        file.CopyTo(stream);
      }
    }
    void IDemoFileService.Upload(string otp, Stream file1, string file1Name, string file1ContentType, Stream file2) {
      var filePath = Path.Combine("C:\\Temp", file1Name);
      using (var stream = System.IO.File.Create(filePath)) {
        file1.CopyTo(stream);
      }
      var filePath2 = Path.Combine("C:\\Temp", file1Name + ".NR2");
      using (var stream = System.IO.File.Create(filePath2)) {
        file2.CopyTo(stream);
      }
    }
    void IDemoFileService.Upload(string otp, Stream[] files, string[] fileNames, string[] fileContentTypes, Stream file2) {
      for (int i = 0;i < files.Length; i++) {
        var filePath = Path.Combine("C:\\Temp", fileNames[i]);
        using (var stream = System.IO.File.Create(filePath)) {
          files[i].CopyTo(stream);
        }
      }
    }

    Stream IDemoFileService.UpAndDown(string otp,
      Stream inputFile, string inputFileName, string inputFileContentType,
      out string fileName, out string fileContentType
    ) {
      var filePath = Path.Combine("C:\\Temp", inputFileName);
      using (var stream = System.IO.File.Create(filePath)) {
        inputFile.CopyTo(stream);
      }
      fileName = "FOO.txt";
      fileContentType = " text/plain";
      return System.IO.File.OpenRead("C:\\Temp\\AFS-Demo\\FOO.txt");
    }


#endif
  }

}
