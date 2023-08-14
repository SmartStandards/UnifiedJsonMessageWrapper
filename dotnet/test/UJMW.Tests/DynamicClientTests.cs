using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Policy;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static System.Web.UJMW.DynamicClientFactory;

namespace System.Web.UJMW {

  [TestClass]
  public class DynamicClientTests {

    internal const string dummyRootUrl = "https://dummy/";

    [TestMethod]
    public void DynamicClientTest1() {

      HttpPostMethod mockHttpCaller = (
        (string targetUrl, string rawJsonContent) => {
          string methodName = targetUrl.Substring(dummyRootUrl.Length);
          if (methodName == nameof(IMyService.ThisIsAVoid)) {
            return "{\n}";
          }
          return "{ \"fault\":\"UNKNOWN-METHOD\"}";
        }
      );

      var client = DynamicClientFactory.CreateInstance<IMyService>(mockHttpCaller, ()=>dummyRootUrl);


      client.ThisIsAVoid();

    }

  }

}
